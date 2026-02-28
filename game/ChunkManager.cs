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
    /// SISTEMA DE CAPAS:
    ///   HQ  → detalle completo, zona cercana
    ///   LP  → LowPolyChunk con 4 meshes pre-cacheadas (una por nivel LOD)
    ///         Al alejarse/acercarse solo se cambia ActiveLevel: cero regeneración.
    ///
    /// GENERACIÓN LP:
    ///   Al crear un LP chunk se encolan los 4 niveles de una vez.
    ///   Cada nivel se procesa en su propia task y sube su mesh a GPU
    ///   independientemente. El nivel activo se actualiza cada frame.
    ///
    /// GENERACIÓN PROGRESIVA LP → HQ:
    ///   Los chunks nuevos dentro de la zona HQ se crean primero como LP
    ///   (nivel más rápido = LOD_LEVELS-1). Una vez el LP está listo y visible,
    ///   se promueven automáticamente a HQ. Esto elimina los frames en negro
    ///   mientras el HQ se construye.
    ///
    /// CHUNK-STRIDE (nivel 3):
    ///   En la corona más lejana se saltean chunks alternos en XZ
    ///   (posiciones impares), reduciendo a ~1/4 los chunks generados.
    /// </summary>
    public class ChunkManager
    {
        // ── Diccionarios de chunks ───────────────────────────────────
        private readonly Dictionary<Vector3Int, Chunk>        _chunks;
        private readonly Dictionary<Vector3Int, LowPolyChunk> _lowPolyChunks;

        // ── Chunks HQ esperando a que su LP de cobertura esté listo ──
        // Clave: posición del chunk. Valor: true cuando el LP ya está listo
        // y el HQ fue encolado para generarse.
        private readonly HashSet<Vector3Int> _pendingHqAfterLp;

        // ── Cola de generación ───────────────────────────────────────
        //   Cada entrada lleva el tipo + el nivel LOD (solo relevante para LP)
        private readonly Queue<(Vector3Int pos, ChunkType type, int level)> _generationQueue;
        private const int MAX_QUEUE_SIZE = 50000;

        // ── Colas de malla (main thread) ─────────────────────────────
        private readonly Queue<MeshData>        _meshDataQueue;
        private readonly Queue<MeshDataLowPoly> _lowPolyMeshDataQueue;

        private readonly WorldGenerator _worldGenerator;
        private readonly GraphicsDevice _graphicsDevice;

        private Vector3Int _lastPlayerChunkPos;
        private Vector3    _lastPlayerPosition;
        private readonly int _chunkSize;
        private readonly int _loadDistance;
        private readonly int _lodDistance;

        // Radio XZ del LOD: con loadDistance=4 → 10 chunks de radio horizontal.
        private int LodDistXZ => Math.Min(_lodDistance, _loadDistance * 2 + 2);

        // Radio Y del LOD: el terreno va hasta MaxHeight=120 ≈ chunkY 3.
        // Con LodDistY=1 y el jugador en chunkY≈2 se cubren chunkY 1–3.
        // LodDistY=3 cargaba chunkY 4–5 (Y 128–191) que son SIEMPRE aire puro:
        // se marcaban _levelFailed pero seguían en el dict inflando TotalChunkEntries.
        private const int LodDistY = 1;

        private readonly object _chunkLock     = new object();
        private readonly object _queueLock     = new object();
        private readonly object _meshQueueLock = new object();

        private int _activeGenerationTasks;

        public ChunkManager(GraphicsDevice graphicsDevice, int chunkSize = 16, int loadDistance = 4)
        {
            _graphicsDevice = graphicsDevice;
            _chunkSize      = chunkSize;
            _loadDistance   = loadDistance;
            _lodDistance    = loadDistance * 4;

            _chunks        = new Dictionary<Vector3Int, Chunk>(256);
            _lowPolyChunks = new Dictionary<Vector3Int, LowPolyChunk>(1024);

            _pendingHqAfterLp = new HashSet<Vector3Int>();

            _generationQueue      = new Queue<(Vector3Int, ChunkType, int)>(MAX_QUEUE_SIZE);
            _meshDataQueue        = new Queue<MeshData>(256);
            _lowPolyMeshDataQueue = new Queue<MeshDataLowPoly>(512);

            _worldGenerator     = new WorldGenerator(seed: 42);
            _lastPlayerChunkPos = Vector3Int.Zero;
            _lastPlayerPosition = Vector3.Zero;
        }

        // ============================================================
        //  SIMPLIFICATION LEVEL
        // ============================================================

        /// <summary>
        /// Nivel LOD según distancia XZ en chunks al jugador. Rango: 0–2.
        ///   0 → dist <= 33%  (más detalle)
        ///   1 → dist <= 66%
        ///   2 → dist <= 100% (menos detalle)
        /// </summary>
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
                // Aunque no se movio de chunk, actualizar niveles activos
                UpdateActiveLevels(currentChunkPos);
            }

            // Promover LP→HQ cada frame aunque no haya cambio de chunk
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
                        // Insertar al frente: los HQ promovidos tienen prioridad máxima
                        // porque ya tienen LP de cobertura visible.
                        // Como Queue no tiene InsertFront, reconstruimos con ellos primero.
                        var existing = _generationQueue.ToArray();
                        _generationQueue.Clear();
                        foreach (var entry in toEnqueue)
                            _generationQueue.Enqueue(entry);
                        foreach (var entry in existing)
                            _generationQueue.Enqueue(entry);
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
            var highQualityZone  = new HashSet<Vector3Int>();
            var lowPolyZone      = new HashSet<Vector3Int>();
            var chunksToGenerate = new List<(Vector3Int pos, ChunkType type, int level)>();

            // ZONA HQ — cilindro XZ, no esfera 3D.
            const int HqDistY = 3;
            for (int x = -_loadDistance; x <= _loadDistance; x++)
            for (int y = -HqDistY; y <= HqDistY; y++)
            for (int z = -_loadDistance; z <= _loadDistance; z++)
            {
                int chunkY = centerChunk.Y + y;
                if (chunkY < 0) continue;

                if ((float)Math.Sqrt(x*x + z*z) <= _loadDistance + 0.5f)
                    highQualityZone.Add(new Vector3Int(centerChunk.X + x, chunkY, centerChunk.Z + z));
            }

            // ZONA LP — radio XZ independiente, Y muy acotado.
            int lodXZ = LodDistXZ;
            for (int x = -lodXZ; x <= lodXZ; x++)
            for (int y = -LodDistY; y <= LodDistY; y++)
            for (int z = -lodXZ; z <= lodXZ; z++)
            {
                int chunkY = 1 + y;
                if (chunkY < 0) continue;

                var chunkPos = new Vector3Int(centerChunk.X + x, chunkY, centerChunk.Z + z);
                float distXZ = (float)Math.Sqrt(x*x + z*z);
                if (distXZ > lodXZ + 0.5f)               continue;
                if (highQualityZone.Contains(chunkPos))  continue;

                lowPolyZone.Add(chunkPos);
            }

            lock (_chunkLock)
            {
                // Limpiar pendientes que ya no están en zona HQ
                var stalePending = _pendingHqAfterLp
                    .Where(p => !highQualityZone.Contains(p))
                    .ToList();
                foreach (var p in stalePending)
                    _pendingHqAfterLp.Remove(p);

                // 1. Promover LP -> HQ (chunks que tenían LP y ahora entran en zona HQ)
                foreach (var chunkPos in _lowPolyChunks.Keys.ToList())
                {
                    if (highQualityZone.Contains(chunkPos) && !_chunks.ContainsKey(chunkPos))
                    {
                        // Si ya teníamos este LP como pendiente de promoción, se procesará
                        // en PromotePendingToHQ. Si no estaba pendiente, crearlo directamente
                        // (es un LP que existía de antes, ya tiene mesh).
                        if (!_pendingHqAfterLp.Contains(chunkPos))
                        {
                            _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                            chunksToGenerate.Add((chunkPos, ChunkType.HighQuality, 0));
                        }
                        // Si está en _pendingHqAfterLp, PromotePendingToHQ lo manejará
                    }
                }

                // 2. Crear HQ nuevos — PROGRESIVO: primero LP de cobertura, luego HQ
                foreach (var chunkPos in highQualityZone)
                {
                    if (_chunks.ContainsKey(chunkPos)) continue;          // ya existe HQ
                    if (_pendingHqAfterLp.Contains(chunkPos)) continue;   // ya en pipeline progresivo

                    if (_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        // Tiene LP preexistente (venía de la zona LP): promover directamente
                        _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add((chunkPos, ChunkType.HighQuality, 0));
                    }
                    else
                    {
                        // Chunk completamente nuevo: crear LP de cobertura rápida primero
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(
                            chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        // Solo el nivel más rápido (peor calidad) para tener cobertura visual
                        chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, LowPolyChunk.LOD_LEVELS - 1));
                        // Marcar para promover a HQ en cuanto el LP esté listo
                        _pendingHqAfterLp.Add(chunkPos);
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
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, lvl));
                    }
                }

                // 4. Crear LP nuevos o encolar niveles faltantes
                foreach (var chunkPos in lowPolyZone)
                {
                    if (_chunks.ContainsKey(chunkPos)) continue;

                    if (!_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, lvl));
                    }
                    else
                    {
                        var lpChunk = _lowPolyChunks[chunkPos];
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                        {
                            if (lpChunk.NeedsMesh(lvl))
                                chunksToGenerate.Add((chunkPos, ChunkType.LowPoly, lvl));
                        }
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
                        return dx*dx + dz*dz > lodXZFar * lodXZFar
                            || Math.Abs(dy) > LodDistY + 1;
                    })
                    .ToList();

                foreach (var chunkPos in lpToRemove)
                {
                    // Si estaba pendiente de promoción a HQ, cancelar
                    _pendingHqAfterLp.Remove(chunkPos);
                    _lowPolyChunks[chunkPos].Dispose();
                    _lowPolyChunks.Remove(chunkPos);
                }
            }

            // Actualizar niveles activos inmediatamente
            UpdateActiveLevels(centerChunk);

            // Orden de prioridad:
            //   1. HQ primero (zona cercana)
            //   2. LP por distancia XZ (más cercano antes)
            //   3. Dentro del mismo chunk LP, nivel más alto primero (más rápido)
            chunksToGenerate.Sort((a, b) =>
            {
                if (a.type != b.type)
                    return a.type == ChunkType.HighQuality ? -1 : 1;

                if (a.pos.Equals(b.pos))
                    return b.level.CompareTo(a.level);

                float dA = (float)Math.Sqrt(
                    Math.Pow(a.pos.X - (int)Math.Floor(playerPosition.X / _chunkSize), 2) +
                    Math.Pow(a.pos.Z - (int)Math.Floor(playerPosition.Z / _chunkSize), 2));
                float dB = (float)Math.Sqrt(
                    Math.Pow(b.pos.X - (int)Math.Floor(playerPosition.X / _chunkSize), 2) +
                    Math.Pow(b.pos.Z - (int)Math.Floor(playerPosition.Z / _chunkSize), 2));
                return dA.CompareTo(dB);
            });

            lock (_queueLock)
            {
                _generationQueue.Clear();
                foreach (var entry in chunksToGenerate)
                    _generationQueue.Enqueue(entry);
            }
        }

        // ============================================================
        //  PROMOCIÓN PROGRESIVA LP → HQ
        // ============================================================

        /// <summary>
        /// Revisa los chunks en _pendingHqAfterLp. Si el LP de cobertura ya tiene
        /// mesh lista (nivel más rápido = LOD_LEVELS-1), crea el HQ encima y encola
        /// su generación. Se llama desde el main thread cada frame.
        /// PRECONDICIÓN: llamar dentro de lock(_chunkLock).
        /// </summary>
        private void PromotePendingToHQ(List<(Vector3Int, ChunkType, int)> toEnqueue)
        {
            var promoted = new List<Vector3Int>();

            foreach (var chunkPos in _pendingHqAfterLp)
            {
                if (!_lowPolyChunks.TryGetValue(chunkPos, out var lp))
                {
                    // El LP fue descartado antes de terminar (p.ej. el jugador se alejó
                    // rápido). Limpiar sin crear HQ.
                    promoted.Add(chunkPos);
                    continue;
                }

                // Esperar a que el nivel más rápido tenga mesh para garantizar
                // cobertura visual antes de promover.
                if (!lp.HasMeshForLevel(LowPolyChunk.LOD_LEVELS - 1))
                    continue;

                // LP listo → crear el chunk HQ encima
                if (!_chunks.ContainsKey(chunkPos))
                {
                    _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                    toEnqueue.Add((chunkPos, ChunkType.HighQuality, 0));
                }

                promoted.Add(chunkPos);
            }

            foreach (var p in promoted)
                _pendingHqAfterLp.Remove(p);
        }

        /// <summary>
        /// Recalcula el ActiveLevel de cada LP chunk segun la distancia actual.
        /// Costo: O(LP chunks), solo aritmetica entera — muy barato por frame.
        /// Si el nivel deseado no tiene mesh aun, usa el mejor disponible.
        /// </summary>
        private void UpdateActiveLevels(Vector3Int centerChunk)
        {
            lock (_chunkLock)
            {
                foreach (var (chunkPos, lpChunk) in _lowPolyChunks)
                {
                    int dx = chunkPos.X - centerChunk.X;
                    int dz = chunkPos.Z - centerChunk.Z;
                    float distXZ = (float)Math.Sqrt(dx*dx + dz*dz);

                    int desired     = GetSimplificationLevel(distXZ);
                    int activeLevel = FindBestAvailableLevel(lpChunk, desired);
                    lpChunk.ActiveLevel = activeLevel;
                }
            }
        }

        /// <summary>
        /// Busca el nivel con mesh disponible mas cercano al deseado.
        /// Prioriza niveles mas bajos (mas calidad) si estan disponibles.
        /// </summary>
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

            return desired; // Ningun nivel listo: HasMesh = false, no se renderiza
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

        private enum ChunkType { HighQuality, LowPoly }

        private readonly int _maxConcurrentTasks = Math.Max(1, Environment.ProcessorCount - 1);

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
                bool exists = _chunks.ContainsKey(chunkPos) || _lowPolyChunks.ContainsKey(chunkPos);
                if (!exists) return;

                if (type == ChunkType.LowPoly &&
                    _lowPolyChunks.TryGetValue(chunkPos, out var lpCheck))
                {
                    if (lpCheck.HasMeshForLevel(level) || lpCheck.IsMeshBuildingForLevel(level))
                        return;

                    lpCheck.MarkMeshBuildStart(level);
                }
            }

            Interlocked.Increment(ref _activeGenerationTasks);

            Task.Run(() =>
            {
                try
                {
                    if (type == ChunkType.HighQuality)
                        GenerateNormalChunkTask(chunkPos);
                    else
                        GenerateLowPolyChunkTask(chunkPos, level);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Error generating chunk {chunkPos} level {level}: {ex.Message}");
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
                    _chunks.TryGetValue(np, out neighbors[dx+1, dy+1, dz+1]);
                }
            }

            var mesher = new GreedyMesher(chunk, neighbors, _chunkSize);
            var (vertices, indices, debugInfo) = mesher.GenerateMesh();

            lock (_chunkLock)
            {
                if (!_chunks.ContainsKey(chunkPos))
                    return;
            }

            if (vertices != null && indices != null)
            {
                lock (_meshQueueLock)
                {
                    _meshDataQueue.Enqueue(new MeshData
                    {
                        ChunkPos  = chunkPos,
                        Vertices  = vertices,
                        Indices   = indices,
                        DebugInfo = debugInfo
                    });
                }
            }
            else
            {
                lock (_chunkLock)
                {
                    if (_chunks.TryGetValue(chunkPos, out var c))
                        c.MarkMeshEmpty();
                }
            }
        }

        // ============================================================
        //  GENERACION LP - un nivel a la vez
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
                        Indices  = indices,
                        Level    = level
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
        //  PROCESAR MALLAS (MAIN THREAD)
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
                // HQ
                foreach (var chunk in _chunks.Values.ToList())
                {
                    if (wireframeOnly && currentChunk.HasValue &&
                        (chunk.X != currentChunk.Value.X || chunk.Z != currentChunk.Value.Z))
                        continue;

                    if (chunk.HasMesh && IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                    {
                        effect.World = Matrix.CreateTranslation(
                            chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                        effect.CurrentTechnique.Passes[0].Apply();
                        chunk.Draw(_graphicsDevice, cameraFrustum);
                    }
                }

                // LP — visible si HQ no tiene mesh aún (incluye los chunks pendientes de promoción)
                foreach (var chunk in _lowPolyChunks.Values.ToList())
                {
                    if (!chunk.HasMesh ||
                        !IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                        continue;

                    var hqPos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                    if (_chunks.TryGetValue(hqPos, out var hqChunk) && hqChunk.HasMesh)
                        continue;

                    effect.World = Matrix.CreateTranslation(
                        chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);
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
            var chunk    = GetChunk(chunkPos);
            if (chunk == null) return BlockType.Air;

            int lx = (int)worldPos.X % _chunkSize;
            int ly = (int)worldPos.Y % _chunkSize;
            int lz = (int)worldPos.Z % _chunkSize;
            if (lx < 0) lx += _chunkSize;
            if (ly < 0) ly += _chunkSize;
            if (lz < 0) lz += _chunkSize;

            return chunk.GetBlock(lx, ly, lz);
        }

        /// <summary>
        /// Chunks con mesh real en GPU (los que se renderizan o están construyéndose).
        /// Excluye los LP marcados como fallidos (chunks de aire puro sin geometría).
        /// </summary>
        public int LoadedChunkCount
        {
            get
            {
                lock (_chunkLock)
                {
                    int lp = 0;
                    foreach (var c in _lowPolyChunks.Values)
                    {
                        bool active = false;
                        for (int i = 0; i < LowPolyChunk.LOD_LEVELS; i++)
                            if (c.HasMeshForLevel(i) || c.IsMeshBuildingForLevel(i)) { active = true; break; }
                        if (active) lp++;
                    }
                    return _chunks.Count + lp;
                }
            }
        }

        /// <summary>Total de entradas en los diccionarios (incluye chunks aire puro).</summary>
        public int TotalChunkEntries
        {
            get { lock (_chunkLock) return _chunks.Count + _lowPolyChunks.Count; }
        }

        public int GenerationQueueCount
        {
            get { lock (_queueLock) return _generationQueue.Count; }
        }

        /// <summary>Tasks de generación activas en el ThreadPool en este momento.</summary>
        public int ActiveGenerationTasks => _activeGenerationTasks;

        /// <summary>Chunks HQ esperando a que su LP de cobertura esté listo.</summary>
        public int PendingHqCount
        {
            get { lock (_chunkLock) return _pendingHqAfterLp.Count; }
        }

        public void Dispose()
        {
            lock (_chunkLock)
            {
                foreach (var c in _chunks.Values)        c.Dispose();
                foreach (var c in _lowPolyChunks.Values) c.Dispose();
                _chunks.Clear();
                _lowPolyChunks.Clear();
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
        public static Vector3Int One  => new Vector3Int(1, 1, 1);

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