using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace game
{
    /// <summary>
    /// Administra todos los chunks del mundo.
    ///
    /// SISTEMA DE CAPAS (interior → exterior):
    ///   HQ  → detalle completo, zona cercana
    ///   LP  → LowPolyChunk con 3 meshes pre-cacheadas (LOD 0–2)
    ///   VLP → VeryLowPolyChunk, heightmap plano, zona MUY lejana
    ///         Solo activo cuando EnableVeryLowPoly = true.
    ///
    /// TOGGLE VLP:
    ///   ChunkManager.EnableVeryLowPoly = true/false en tiempo real.
    ///   Al desactivar descarta todos los VLP; al activar los encola
    ///   en el siguiente Update.
    ///
    /// FIXES:
    ///   - VLP zombis: re-encola VLPs con IsDirty=true que nunca se generaron.
    ///   - Capas planas flotantes: elimina VLPs solapados al crear LP/HQ en esa posición.
    ///   - Race condition IsMeshBuilding: MarkMeshBuildStart movido dentro del lock
    ///     y el double-check redundante en el task eliminado.
    /// </summary>
    public class ChunkManager
    {
        // ── Diccionarios de chunks ───────────────────────────────────
        private readonly Dictionary<Vector3Int, Chunk> _chunks;
        private readonly Dictionary<Vector3Int, LowPolyChunk> _lowPolyChunks;
        private readonly Dictionary<Vector3Int, VeryLowPolyChunk> _veryLowPolyChunks;

        // ── Chunks HQ esperando a que su LP de cobertura esté listo ──
        private readonly HashSet<Vector3Int> _pendingHqAfterLp;

        // ── Cola de generación ───────────────────────────────────────
        private readonly Queue<(Vector3Int pos, ChunkType type, int level)> _generationQueue;
        private const int MAX_QUEUE_SIZE = 50000;

        // ── Colas de malla (main thread) ─────────────────────────────
        private readonly Queue<MeshData> _meshDataQueue;
        private readonly Queue<MeshDataLowPoly> _lowPolyMeshDataQueue;
        private readonly Queue<MeshDataVeryLowPoly> _veryLowPolyMeshDataQueue;

        private readonly WorldGenerator _worldGenerator;
        private readonly GraphicsDevice _graphicsDevice;

        private Vector3Int _lastPlayerChunkPos;
        private Vector3 _lastPlayerPosition;
        private readonly int _chunkSize;
        private readonly int _loadDistance;
        private readonly int _lodDistance;

        private int LodDistXZ => Math.Min(_lodDistance, _loadDistance * 2 + 2);
        private const int LodDistY = 1;

        private int VlpDistXZ => LodDistXZ + 8;
        private const int VlpDistY = 1;

        // ── Toggle VeryLowPoly ───────────────────────────────────────
        private bool _enableVeryLowPoly = true;

        public bool EnableVeryLowPoly
        {
            get => _enableVeryLowPoly;
            set
            {
                if (_enableVeryLowPoly == value) return;
                _enableVeryLowPoly = value;

                if (!value)
                {
                    lock (_chunkLock)
                    {
                        foreach (var c in _veryLowPolyChunks.Values) c.Dispose();
                        _veryLowPolyChunks.Clear();
                    }
                }
                else
                {
                    // Forzar recálculo inmediato
                    _lastPlayerChunkPos = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
                }
            }
        }

        private readonly object _chunkLock = new object();
        private readonly object _queueLock = new object();
        private readonly object _meshQueueLock = new object();

        private int _activeGenerationTasks;
        private readonly int _maxConcurrentTasks = Math.Max(1, Environment.ProcessorCount - 1);

        // Throttle UpdateActiveLevels — solo cada N frames
        private int _activeLevelFrameCounter = 0;
        private const int ACTIVE_LEVEL_THROTTLE = 20;

        public ChunkManager(GraphicsDevice graphicsDevice, int chunkSize = 16, int loadDistance = 4)
        {
            _graphicsDevice = graphicsDevice;
            _chunkSize = chunkSize;
            _loadDistance = loadDistance;
            _lodDistance = loadDistance * 4;

            _chunks = new Dictionary<Vector3Int, Chunk>(256);
            _lowPolyChunks = new Dictionary<Vector3Int, LowPolyChunk>(1024);
            _veryLowPolyChunks = new Dictionary<Vector3Int, VeryLowPolyChunk>(512);

            _pendingHqAfterLp = new HashSet<Vector3Int>();

            _generationQueue = new Queue<(Vector3Int, ChunkType, int)>(MAX_QUEUE_SIZE);
            _meshDataQueue = new Queue<MeshData>(256);
            _lowPolyMeshDataQueue = new Queue<MeshDataLowPoly>(512);
            _veryLowPolyMeshDataQueue = new Queue<MeshDataVeryLowPoly>(256);

            _worldGenerator = new WorldGenerator(seed: 42);
            _lastPlayerChunkPos = Vector3Int.Zero;
            _lastPlayerPosition = Vector3.Zero;
        }

        // ============================================================
        //  SIMPLIFICATION LEVEL
        // ============================================================

        private int GetSimplificationLevel(float distXZ)
        {
            float t = distXZ / (LodDistXZ + 0.5f);
            if (t <= 0.33f) return 0;
            if (t <= 0.66f) return 1;
            return 2;
        }

        // ============================================================
        //  UPDATE
        // ============================================================

        public void Update(Vector3 playerPosition, BoundingFrustum cameraFrustum = null)
        {
            _lastPlayerPosition = playerPosition;
            Vector3Int currentChunkPos = GetChunkCoordinates(playerPosition);

            if (currentChunkPos != _lastPlayerChunkPos)
            {
                _lastPlayerChunkPos = currentChunkPos;
                UpdateVisibleChunks(currentChunkPos, playerPosition);
            }
            else
            {
                if (++_activeLevelFrameCounter >= ACTIVE_LEVEL_THROTTLE)
                {
                    _activeLevelFrameCounter = 0;
                    UpdateActiveLevels(currentChunkPos);
                }
            }

            // Promover LP→HQ cada frame
            if (_pendingHqAfterLp.Count > 0)
            {
                var toEnqueue = new List<(Vector3Int, ChunkType, int)>();
                lock (_chunkLock)
                {
                    PromotePendingToHQ(toEnqueue);
                }
                if (toEnqueue.Count > 0)
                {
                    lock (_queueLock)
                    {
                        var existing = _generationQueue.ToArray();
                        _generationQueue.Clear();
                        foreach (var entry in toEnqueue) _generationQueue.Enqueue(entry);
                        foreach (var entry in existing) _generationQueue.Enqueue(entry);
                    }
                }
            }

            ProcessGenerationQueue();
            ProcessMeshDataQueue();
        }

        public Vector3Int GetChunkCoordinates(Vector3 worldPos)
        {
            int x = (int)Math.Floor(worldPos.X / _chunkSize);
            int y = (int)Math.Floor(worldPos.Y / _chunkSize);
            int z = (int)Math.Floor(worldPos.Z / _chunkSize);
            return new Vector3Int(x, y, z);
        }

        // ============================================================
        //  ACTUALIZAR CHUNKS VISIBLES
        // ============================================================

        private void UpdateVisibleChunks(Vector3Int centerChunk, Vector3 playerPosition)
        {
            var highQualityZone = new HashSet<Vector3Int>();
            var lowPolyZone = new HashSet<Vector3Int>();
            var veryLowPolyZone = new HashSet<Vector3Int>();
            var chunksToGenerate = new List<(Vector3Int pos, ChunkType type, int level)>();

            // ── ZONA HQ ──────────────────────────────────────────────
            const int HqDistY = 3;
            for (int x = -_loadDistance; x <= _loadDistance; x++)
                for (int y = -HqDistY; y <= HqDistY; y++)
                    for (int z = -_loadDistance; z <= _loadDistance; z++)
                    {
                        int chunkY = centerChunk.Y + y;
                        if (chunkY < 0) continue;
                        if ((float)Math.Sqrt(x * x + z * z) <= _loadDistance + 0.5f)
                            highQualityZone.Add(new Vector3Int(centerChunk.X + x, chunkY, centerChunk.Z + z));
                    }

            // ── ZONA LP ───────────────────────────────────────────────
            int lodXZ = LodDistXZ;
            for (int x = -lodXZ; x <= lodXZ; x++)
                for (int y = -LodDistY; y <= LodDistY; y++)
                    for (int z = -lodXZ; z <= lodXZ; z++)
                    {
                        int chunkY = 1 + y;
                        if (chunkY < 0) continue;
                        var chunkPos = new Vector3Int(centerChunk.X + x, chunkY, centerChunk.Z + z);
                        if ((float)Math.Sqrt(x * x + z * z) > lodXZ + 0.5f) continue;
                        if (highQualityZone.Contains(chunkPos)) continue;
                        lowPolyZone.Add(chunkPos);
                    }

            // ── ZONA VLP ─────────────────────────────────────────────
            if (_enableVeryLowPoly)
            {
                int vlpXZ = VlpDistXZ;
                for (int x = -vlpXZ; x <= vlpXZ; x++)
                    for (int y = -VlpDistY; y <= VlpDistY; y++)
                        for (int z = -vlpXZ; z <= vlpXZ; z++)
                        {
                            int chunkY = 1 + y;
                            if (chunkY < 0) continue;
                            var chunkPos = new Vector3Int(centerChunk.X + x, chunkY, centerChunk.Z + z);
                            if ((float)Math.Sqrt(x * x + z * z) > vlpXZ + 0.5f) continue;
                            if (highQualityZone.Contains(chunkPos)) continue;
                            if (lowPolyZone.Contains(chunkPos)) continue;
                            veryLowPolyZone.Add(chunkPos);
                        }
            }

            lock (_chunkLock)
            {
                // Limpiar pendientes que ya no están en zona HQ
                var stalePending = _pendingHqAfterLp
                    .Where(p => !highQualityZone.Contains(p)).ToList();
                foreach (var p in stalePending) _pendingHqAfterLp.Remove(p);

                // 1. LP preexistente → HQ
                foreach (var chunkPos in _lowPolyChunks.Keys.ToList())
                {
                    if (!highQualityZone.Contains(chunkPos)) continue;
                    if (_chunks.ContainsKey(chunkPos)) continue;
                    if (_pendingHqAfterLp.Contains(chunkPos)) continue;

                    _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                    chunksToGenerate.Add((chunkPos, ChunkType.HighQuality, 0));

                    // FIX: destruir VLP solapado al promover a HQ
                    RemoveVlpAt(chunkPos);
                }

                // 2. HQ nuevos — progresivo
                foreach (var chunkPos in highQualityZone)
                {
                    if (_chunks.ContainsKey(chunkPos)) continue;
                    if (_pendingHqAfterLp.Contains(chunkPos)) continue;

                    if (_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add((chunkPos, ChunkType.HighQuality, 0));

                        // FIX: destruir VLP solapado al promover a HQ
                        RemoveVlpAt(chunkPos);
                    }
                    else
                    {
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(
                            chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, LowPolyChunk.LOD_LEVELS - 1));
                        _pendingHqAfterLp.Add(chunkPos);

                        // FIX: destruir VLP solapado al crear LP para este slot
                        RemoveVlpAt(chunkPos);
                    }
                }

                // 3. Descartar HQ fuera de zona
                var hqToRemove = _chunks.Keys.Where(p => !highQualityZone.Contains(p)).ToList();
                foreach (var chunkPos in hqToRemove)
                {
                    _chunks[chunkPos].Dispose();
                    _chunks.Remove(chunkPos);
                    _pendingHqAfterLp.Remove(chunkPos);

                    if (lowPolyZone.Contains(chunkPos) && !_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(
                            chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, lvl));

                        // FIX: destruir VLP solapado al crear LP en esta posición
                        RemoveVlpAt(chunkPos);
                    }
                }

                // 4. LP nuevos / niveles faltantes
                foreach (var chunkPos in lowPolyZone)
                {
                    if (_chunks.ContainsKey(chunkPos)) continue;

                    if (!_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(
                            chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, lvl));

                        // FIX: destruir VLP solapado al crear LP nuevo
                        RemoveVlpAt(chunkPos);
                    }
                    else
                    {
                        var lpChunk = _lowPolyChunks[chunkPos];
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            if (lpChunk.NeedsMesh(lvl))
                                chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, lvl));
                    }
                }

                // 5. Borrar LP muy lejanos
                int lodXZFar = LodDistXZ + 2;
                var lpToRemove = _lowPolyChunks.Keys
                    .Where(p =>
                    {
                        int dx = p.X - centerChunk.X;
                        int dz = p.Z - centerChunk.Z;
                        int dy = p.Y - 1;
                        return dx * dx + dz * dz > lodXZFar * lodXZFar
                            || Math.Abs(dy) > LodDistY + 1;
                    }).ToList();
                foreach (var chunkPos in lpToRemove)
                {
                    _pendingHqAfterLp.Remove(chunkPos);
                    _lowPolyChunks[chunkPos].Dispose();
                    _lowPolyChunks.Remove(chunkPos);
                }

                // ── VLP ──────────────────────────────────────────────
                if (_enableVeryLowPoly)
                {
                    // 6. VLP nuevos — FIX: también re-encolar los que fallaron (IsDirty=true)
                    foreach (var chunkPos in veryLowPolyZone)
                    {
                        if (!_veryLowPolyChunks.ContainsKey(chunkPos))
                        {
                            _veryLowPolyChunks[chunkPos] = new VeryLowPolyChunk(
                                chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                            chunksToGenerate.Add((chunkPos, ChunkType.VeryLowPoly, 0));
                        }
                        else
                        {
                            // FIX: re-encolar VLPs que fallaron o quedaron zombis
                            var vlp = _veryLowPolyChunks[chunkPos];
                            if (!vlp.HasMesh && !vlp.IsMeshBuilding && vlp.IsDirty)
                                chunksToGenerate.Add((chunkPos, ChunkType.VeryLowPoly, 0));
                        }
                    }

                    // 7. Borrar VLP muy lejanos
                    int vlpXZFar = VlpDistXZ + 3;
                    var vlpToRemove = _veryLowPolyChunks.Keys
                        .Where(p =>
                        {
                            int dx = p.X - centerChunk.X;
                            int dz = p.Z - centerChunk.Z;
                            int dy = p.Y - 1;
                            return dx * dx + dz * dz > vlpXZFar * vlpXZFar
                                || Math.Abs(dy) > VlpDistY + 1;
                        }).ToList();
                    foreach (var chunkPos in vlpToRemove)
                    {
                        _veryLowPolyChunks[chunkPos].Dispose();
                        _veryLowPolyChunks.Remove(chunkPos);
                    }
                }
            }

            UpdateActiveLevels(centerChunk);

            // Sort determinista
            chunksToGenerate.Sort((a, b) =>
            {
                // 1. Tipo: HQ > LP > VLP
                int ta = TypePriority(a.type), tb = TypePriority(b.type);
                if (ta != tb) return ta.CompareTo(tb);

                // 2. Distancia XZ
                float da = ChunkDistXZSq(a.pos, playerPosition);
                float db = ChunkDistXZSq(b.pos, playerPosition);
                int dc = da.CompareTo(db);
                if (dc != 0) return dc;

                // 3. Mismo chunk LP: nivel más alto primero
                if (a.type == ChunkType.LowPoly)
                {
                    int lc = b.level.CompareTo(a.level);
                    if (lc != 0) return lc;
                }

                // 4. Desempate determinista
                int cx = a.pos.X.CompareTo(b.pos.X);
                if (cx != 0) return cx;
                int cy = a.pos.Y.CompareTo(b.pos.Y);
                if (cy != 0) return cy;
                int cz = a.pos.Z.CompareTo(b.pos.Z);
                if (cz != 0) return cz;
                return a.level.CompareTo(b.level);
            });

            lock (_queueLock)
            {
                _generationQueue.Clear();
                foreach (var entry in chunksToGenerate)
                    _generationQueue.Enqueue(entry);
            }
        }

        /// <summary>
        /// FIX helper: elimina el VLP en una posición si existe.
        /// Llamar dentro de _chunkLock.
        /// </summary>
        private void RemoveVlpAt(Vector3Int chunkPos)
        {
            if (_veryLowPolyChunks.TryGetValue(chunkPos, out var vlp))
            {
                vlp.Dispose();
                _veryLowPolyChunks.Remove(chunkPos);
            }
        }

        private static int TypePriority(ChunkType t) => t switch
        {
            ChunkType.HighQuality => 0,
            ChunkType.LowPoly => 1,
            _ => 2
        };

        private float ChunkDistXZSq(Vector3Int pos, Vector3 playerPos)
        {
            float px = (float)Math.Floor(playerPos.X / _chunkSize);
            float pz = (float)Math.Floor(playerPos.Z / _chunkSize);
            float dx = pos.X - px;
            float dz = pos.Z - pz;
            return dx * dx + dz * dz;
        }

        // ============================================================
        //  PROMOCIÓN PROGRESIVA LP → HQ
        // ============================================================

        private void PromotePendingToHQ(List<(Vector3Int, ChunkType, int)> toEnqueue)
        {
            var promoted = new List<Vector3Int>();

            foreach (var chunkPos in _pendingHqAfterLp)
            {
                if (!_lowPolyChunks.TryGetValue(chunkPos, out var lp))
                {
                    promoted.Add(chunkPos);
                    continue;
                }
                if (!lp.HasMeshForLevel(LowPolyChunk.LOD_LEVELS - 1))
                    continue;

                if (!_chunks.ContainsKey(chunkPos))
                {
                    _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                    toEnqueue.Add((chunkPos, ChunkType.HighQuality, 0));

                    // FIX: destruir VLP solapado al promover LP→HQ
                    RemoveVlpAt(chunkPos);
                }
                promoted.Add(chunkPos);
            }

            foreach (var p in promoted) _pendingHqAfterLp.Remove(p);
        }

        private void UpdateActiveLevels(Vector3Int centerChunk)
        {
            lock (_chunkLock)
            {
                foreach (var (chunkPos, lpChunk) in _lowPolyChunks)
                {
                    int dx = chunkPos.X - centerChunk.X;
                    int dz = chunkPos.Z - centerChunk.Z;
                    float distXZ = (float)Math.Sqrt(dx * dx + dz * dz);
                    int desired = GetSimplificationLevel(distXZ);
                    lpChunk.ActiveLevel = FindBestAvailableLevel(lpChunk, desired);
                }
            }
        }

        private static int FindBestAvailableLevel(LowPolyChunk chunk, int desired)
        {
            if (chunk.HasMeshForLevel(desired)) return desired;

            for (int offset = 1; offset < LowPolyChunk.LOD_LEVELS; offset++)
            {
                int lower = desired - offset;
                int upper = desired + offset;
                if (lower >= 0 && chunk.HasMeshForLevel(lower)) return lower;
                if (upper < LowPolyChunk.LOD_LEVELS && chunk.HasMeshForLevel(upper)) return upper;
            }

            return desired;
        }

        // ============================================================
        //  FRUSTUM
        // ============================================================

        private bool IsChunkInFrustum(int cx, int cy, int cz, BoundingFrustum frustum)
        {
            if (frustum == null) return true;
            var min = new Vector3(cx * _chunkSize, cy * _chunkSize, cz * _chunkSize);
            return frustum.Intersects(new BoundingBox(min, min + new Vector3(_chunkSize)));
        }

        // ============================================================
        //  COLA DE GENERACION
        // ============================================================

        private enum ChunkType { HighQuality, LowPoly, VeryLowPoly }

        private void ProcessGenerationQueue()
        {
            while (Volatile.Read(ref _activeGenerationTasks) < _maxConcurrentTasks)
            {
                (Vector3Int pos, ChunkType type, int level) entry;
                lock (_queueLock)
                {
                    if (_generationQueue.Count == 0) return;
                    entry = _generationQueue.Dequeue();
                }
                StartChunkGenerationTask(entry.pos, entry.type, entry.level);
            }
        }

        private void StartChunkGenerationTask(Vector3Int chunkPos, ChunkType type, int level)
        {
            lock (_chunkLock)
            {
                bool exists = type switch
                {
                    ChunkType.HighQuality => _chunks.ContainsKey(chunkPos),
                    ChunkType.LowPoly => _lowPolyChunks.ContainsKey(chunkPos),
                    _ => _veryLowPolyChunks.ContainsKey(chunkPos)
                };
                if (!exists) return;

                if (type == ChunkType.LowPoly &&
                    _lowPolyChunks.TryGetValue(chunkPos, out var lpCheck))
                {
                    if (lpCheck.HasMeshForLevel(level) || lpCheck.IsMeshBuildingForLevel(level))
                        return;
                    lpCheck.MarkMeshBuildStart(level);
                }

                // FIX: MarkMeshBuildStart para VLP también dentro del lock,
                // eliminando la race condition donde el task podía empezar
                // sin que IsMeshBuilding estuviera seteado aún.
                if (type == ChunkType.VeryLowPoly &&
                    _veryLowPolyChunks.TryGetValue(chunkPos, out var vlpCheck))
                {
                    if (vlpCheck.HasMesh || vlpCheck.IsMeshBuilding) return;
                    vlpCheck.MarkMeshBuildStart();
                }
            }

            Interlocked.Increment(ref _activeGenerationTasks);

            Task.Run(() =>
            {
                try
                {
                    switch (type)
                    {
                        case ChunkType.HighQuality:  GenerateNormalChunkTask(chunkPos);        break;
                        case ChunkType.LowPoly:      GenerateLowPolyChunkTask(chunkPos, level); break;
                        case ChunkType.VeryLowPoly:  GenerateVeryLowPolyChunkTask(chunkPos);   break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ChunkManager] Error {type} chunk {chunkPos}: {ex.Message}");

                    // FIX: asegurar que un crash inesperado no deje el VLP zombi
                    if (type == ChunkType.VeryLowPoly)
                    {
                        lock (_chunkLock)
                        {
                            if (_veryLowPolyChunks.TryGetValue(chunkPos, out var vlp))
                                vlp.MarkMeshFailed();
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _activeGenerationTasks);
                }
            });
        }

        // ============================================================
        //  GENERACION HQ
        // ============================================================

        private void GenerateNormalChunkTask(Vector3Int chunkPos)
        {
            byte[,,] blocks = _worldGenerator.GetOrGenerateChunk(
                chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);

            Chunk chunk = null;
            lock (_chunkLock)
            {
                if (_chunks.TryGetValue(chunkPos, out chunk))
                {
                    chunk.SetBlocks(blocks);
                    chunk.MarkMeshBuildStart();
                }
            }
            if (chunk == null) return;

            Chunk[,,] neighbors = new Chunk[3, 3, 3];
            lock (_chunkLock)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var np = new Vector3Int(chunkPos.X + dx, chunkPos.Y + dy, chunkPos.Z + dz);
                            _chunks.TryGetValue(np, out neighbors[dx + 1, dy + 1, dz + 1]);
                        }
            }

            var mesher = new GreedyMesher(chunk, neighbors, _chunkSize);
            var (vertices, indices, debugInfo) = mesher.GenerateMesh();

            lock (_chunkLock) { if (!_chunks.ContainsKey(chunkPos)) return; }

            if (vertices != null && indices != null)
            {
                lock (_meshQueueLock)
                {
                    _meshDataQueue.Enqueue(new MeshData
                    {
                        ChunkPos = chunkPos,
                        Vertices = vertices,
                        Indices = indices,
                        DebugInfo = debugInfo
                    });
                }
            }
            else
            {
                lock (_chunkLock)
                {
                    if (_chunks.TryGetValue(chunkPos, out var c)) c.MarkMeshEmpty();
                }
            }
        }

        // ============================================================
        //  GENERACION LP
        // ============================================================

        private void GenerateLowPolyChunkTask(Vector3Int chunkPos, int level)
        {
            lock (_chunkLock)
            {
                if (!_lowPolyChunks.TryGetValue(chunkPos, out var lpCheck)) return;
                if (lpCheck.HasMeshForLevel(level)) return;
            }

            byte[,,] blocks = _worldGenerator.GenerateLowPolyChunk(
                chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize,
                simplificationLevel: level);

            lock (_chunkLock)
            {
                if (_lowPolyChunks.TryGetValue(chunkPos, out var lp))
                    lp.SetBlocksForLevel(blocks, level);
            }

            var tempChunk = new LowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
            tempChunk.SetBlocksForLevel(blocks, 0);

            var mesher = new SimpleLowPolyMesher(tempChunk, _chunkSize, simplificationLevel: level);
            var (vertices, indices) = mesher.GenerateMesh();

            if (vertices != null && indices != null)
            {
                lock (_meshQueueLock)
                {
                    _lowPolyMeshDataQueue.Enqueue(new MeshDataLowPoly
                    {
                        ChunkPos = chunkPos,
                        Vertices = vertices,
                        Indices = indices,
                        Level = level
                    });
                }
            }
            else
            {
                lock (_chunkLock)
                {
                    if (_lowPolyChunks.TryGetValue(chunkPos, out var lp))
                        lp.SetMeshData(null, null, _graphicsDevice, level);
                }
            }
        }

        // ============================================================
        //  GENERACION VLP
        // ============================================================

        private void GenerateVeryLowPolyChunkTask(Vector3Int chunkPos)
        {
            // FIX: NO hay double-check aquí. El check de HasMesh/IsMeshBuilding
            // ya se hizo dentro del lock en StartChunkGenerationTask, y
            // MarkMeshBuildStart() también se llamó ahí. Re-lockear y
            // re-chequear introducía la race condition original.

            int[,] heightMap = _worldGenerator.GenerateVeryLowPolyChunk(
                chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);

            var tempChunk = new VeryLowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);

            try
            {
                tempChunk.SetHeightMap(heightMap);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VLP] SetHeightMap falló {chunkPos}: {ex.Message}");
                lock (_chunkLock)
                {
                    if (_veryLowPolyChunks.TryGetValue(chunkPos, out var vlp))
                        vlp.MarkMeshFailed();
                }
                return;
            }

            var mesher = new VeryLowPolyMesher(tempChunk, _chunkSize);
            var (vertices, indices) = mesher.GenerateMesh();

            if (vertices != null && indices != null)
            {
                lock (_meshQueueLock)
                {
                    _veryLowPolyMeshDataQueue.Enqueue(new MeshDataVeryLowPoly
                    {
                        ChunkPos = chunkPos,
                        Vertices = vertices,
                        Indices = indices
                    });
                }
            }
            else
            {
                lock (_chunkLock)
                {
                    if (_veryLowPolyChunks.TryGetValue(chunkPos, out var vlp))
                        vlp.MarkMeshFailed();
                }
            }
        }

        // ============================================================
        //  PROCESAR COLAS DE MESH
        // ============================================================

        private void ProcessMeshDataQueue()
        {
            lock (_meshQueueLock)
            {
                while (_meshDataQueue.Count > 0)
                {
                    var md = _meshDataQueue.Dequeue();
                    lock (_chunkLock)
                    {
                        if (_chunks.TryGetValue(md.ChunkPos, out var chunk))
                            chunk.SetMeshData(md.Vertices, md.Indices, _graphicsDevice, md.DebugInfo);
                    }
                }

                while (_lowPolyMeshDataQueue.Count > 0)
                {
                    var md = _lowPolyMeshDataQueue.Dequeue();
                    lock (_chunkLock)
                    {
                        if (_lowPolyChunks.TryGetValue(md.ChunkPos, out var chunk))
                            chunk.SetMeshData(md.Vertices, md.Indices, _graphicsDevice, md.Level);
                    }
                }

                while (_veryLowPolyMeshDataQueue.Count > 0)
                {
                    var md = _veryLowPolyMeshDataQueue.Dequeue();
                    lock (_chunkLock)
                    {
                        if (_veryLowPolyChunks.TryGetValue(md.ChunkPos, out var chunk))
                            chunk.SetMeshData(md.Vertices, md.Indices, _graphicsDevice);
                    }
                }
            }
        }

        // ============================================================
        //  DRAW
        // ============================================================

        public void Draw(BasicEffect effect, BoundingFrustum cameraFrustum,
                         Vector3Int? currentChunk = null, bool wireframeOnly = false)
        {
            lock (_chunkLock)
            {
                // ── HQ ────────────────────────────────────────────────
                foreach (var chunk in _chunks.Values.ToList())
                {
                    if (wireframeOnly && currentChunk.HasValue &&
                        (chunk.X != currentChunk.Value.X || chunk.Z != currentChunk.Value.Z))
                        continue;

                    if (chunk.HasMesh &&
                        IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                    {
                        effect.World = Matrix.CreateTranslation(
                            chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                        effect.CurrentTechnique.Passes[0].Apply();
                        chunk.Draw(_graphicsDevice, cameraFrustum);
                    }
                }

                // ── LP ────────────────────────────────────────────────
                foreach (var chunk in _lowPolyChunks.Values.ToList())
                {
                    if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                        continue;

                    var hqPos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                    if (_chunks.TryGetValue(hqPos, out var hqChunk) && hqChunk.HasMesh)
                        continue;

                    int renderLevel = FindBestAvailableLevel(chunk, chunk.ActiveLevel);
                    if (!chunk.HasMeshForLevel(renderLevel))
                        continue;

                    int saved = chunk.ActiveLevel;
                    chunk.ActiveLevel = renderLevel;

                    effect.World = Matrix.CreateTranslation(
                        chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);

                    chunk.ActiveLevel = saved;
                }

                // ── VLP ───────────────────────────────────────────────
                if (_enableVeryLowPoly)
                {
                    foreach (var chunk in _veryLowPolyChunks.Values.ToList())
                    {
                        if (!chunk.HasMesh) continue;

                        var pos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);

                        if (_chunks.TryGetValue(pos, out var hq) && hq.HasMesh)
                            continue;
                        if (_lowPolyChunks.TryGetValue(pos, out var lp))
                        {
                            int bestLp = FindBestAvailableLevel(lp, lp.ActiveLevel);
                            if (lp.HasMeshForLevel(bestLp)) continue;
                        }

                        if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                            continue;

                        effect.World = Matrix.CreateTranslation(
                            chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                        effect.CurrentTechnique.Passes[0].Apply();
                        chunk.Draw(_graphicsDevice, cameraFrustum);
                    }
                }
            }
        }

        // ============================================================
        //  HELPERS PUBLICOS
        // ============================================================

        public Chunk GetChunk(Vector3Int pos)
        {
            lock (_chunkLock) { _chunks.TryGetValue(pos, out var c); return c; }
        }

        public byte GetBlockAtWorldPosition(Vector3 worldPos)
        {
            var chunkPos = GetChunkCoordinates(worldPos);
            var chunk = GetChunk(chunkPos);
            if (chunk == null) return BlockType.Air;

            int lx = (int)worldPos.X % _chunkSize;
            int ly = (int)worldPos.Y % _chunkSize;
            int lz = (int)worldPos.Z % _chunkSize;
            if (lx < 0) lx += _chunkSize;
            if (ly < 0) ly += _chunkSize;
            if (lz < 0) lz += _chunkSize;

            return chunk.GetBlock(lx, ly, lz);
        }

        public int LoadedChunkCount
        {
            get
            {
                lock (_chunkLock)
                {
                    int lp = 0;
                    foreach (var c in _lowPolyChunks.Values)
                        for (int i = 0; i < LowPolyChunk.LOD_LEVELS; i++)
                            if (c.HasMeshForLevel(i) || c.IsMeshBuildingForLevel(i)) { lp++; break; }
                    int vlp = 0;
                    foreach (var c in _veryLowPolyChunks.Values)
                        if (c.HasMesh || c.IsMeshBuilding) vlp++;
                    return _chunks.Count + lp + vlp;
                }
            }
        }

        public int TotalChunkEntries
        {
            get { lock (_chunkLock) return _chunks.Count + _lowPolyChunks.Count + _veryLowPolyChunks.Count; }
        }

        public int GenerationQueueCount
        {
            get { lock (_queueLock) return _generationQueue.Count; }
        }

        public int ActiveGenerationTasks => _activeGenerationTasks;

        public int PendingHqCount
        {
            get { lock (_chunkLock) return _pendingHqAfterLp.Count; }
        }

        public int VeryLowPolyChunkCount
        {
            get
            {
                lock (_chunkLock)
                {
                    int n = 0;
                    foreach (var c in _veryLowPolyChunks.Values)
                        if (c.HasMesh) n++;
                    return n;
                }
            }
        }

        public void Dispose()
        {
            lock (_chunkLock)
            {
                foreach (var c in _chunks.Values) c.Dispose();
                foreach (var c in _lowPolyChunks.Values) c.Dispose();
                foreach (var c in _veryLowPolyChunks.Values) c.Dispose();
                _chunks.Clear();
                _lowPolyChunks.Clear();
                _veryLowPolyChunks.Clear();
                _pendingHqAfterLp.Clear();
            }
        }

        private Vector3 ChunkCenter(Vector3Int pos) =>
            new Vector3(pos.X * _chunkSize + _chunkSize / 2f,
                        pos.Y * _chunkSize + _chunkSize / 2f,
                        pos.Z * _chunkSize + _chunkSize / 2f);
    }

    // ============================================================
    //  STRUCTS DE TRANSFERENCIA
    // ============================================================

    internal struct MeshData
    {
        public Vector3Int ChunkPos;
        public VertexPositionNormalColor[] Vertices;
        public ushort[] Indices;
        public ChunkDebugInfo DebugInfo;
    }

    internal struct MeshDataLowPoly
    {
        public Vector3Int ChunkPos;
        public VertexPositionNormalColor[] Vertices;
        public ushort[] Indices;
        public int Level;
    }

    internal struct MeshDataVeryLowPoly
    {
        public Vector3Int ChunkPos;
        public VertexPositionNormalColor[] Vertices;
        public ushort[] Indices;
    }

    // ============================================================
    //  VECTOR3INT
    // ============================================================

    public struct Vector3Int : IEquatable<Vector3Int>
    {
        public int X, Y, Z;
        public Vector3Int(int x, int y, int z) { X = x; Y = y; Z = z; }

        public static Vector3Int Zero => new Vector3Int(0, 0, 0);
        public static Vector3Int One => new Vector3Int(1, 1, 1);

        public override bool Equals(object obj) => obj is Vector3Int o && Equals(o);
        public bool Equals(Vector3Int o) => X == o.X && Y == o.Y && Z == o.Z;
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public static bool operator ==(Vector3Int a, Vector3Int b) => a.Equals(b);
        public static bool operator !=(Vector3Int a, Vector3Int b) => !a.Equals(b);
        public static Vector3Int operator +(Vector3Int a, Vector3Int b)
            => new Vector3Int(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}