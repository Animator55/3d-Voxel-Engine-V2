using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace game
{
    public partial class ChunkManager
    {
        private readonly Dictionary<Vector3Int, Chunk>          _chunks;
        private readonly Dictionary<Vector3Int, LowPolyChunk>   _lowPolyChunks;
        private readonly Dictionary<Vector3Int, VeryLowPolyChunk> _veryLowPolyChunks;
        private readonly HashSet<Vector3Int>                    _pendingHqAfterLp;
        private readonly Queue<(Vector3Int pos, ChunkType type, int level)> _generationQueue;
        private const int MAX_QUEUE_SIZE = 50000;

        // ── Mesh-upload queues – locks SEPARADOS para no serializar uploads ──
        private readonly Queue<MeshData>           _meshDataQueue;
        private readonly Queue<MeshDataLowPoly>    _lowPolyMeshDataQueue;
        private readonly Queue<MeshDataVeryLowPoly> _veryLowPolyMeshDataQueue;

        private readonly object _hqMeshQueueLock  = new object();
        private readonly object _lpMeshQueueLock  = new object();
        private readonly object _vlpMeshQueueLock = new object();

        private readonly WorldGenerator _worldGenerator;
        private readonly GraphicsDevice _graphicsDevice;
        private Vector3Int _lastPlayerChunkPos;
        private Vector3    _lastPlayerPosition;
        private readonly int _chunkSize;
        private readonly int _loadDistance;
        private readonly int _lodDistance;
        private int LodDistXZ => Math.Min(_lodDistance, _loadDistance * 2 + 2);
        private const int LodDistY   = 1;
        private int VlpDistXZ => LodDistXZ + 4;
        private const int VlpDistY  = 1;
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
                    _lastPlayerChunkPos = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
                }
            }
        }

        private readonly object _chunkLock   = new object();
        private readonly object _queueLock   = new object();
        private int _activeGenerationTasks;
        private readonly int _maxConcurrentTasks = Math.Max(1, Environment.ProcessorCount - 1);
        private int _activeLevelFrameCounter = 0;
        private const int ACTIVE_LEVEL_THROTTLE = 20;

        // ── Render list cacheada (evita .ToList() y GC cada frame) ──────────
        private List<Chunk>            _renderHq  = new List<Chunk>(256);
        private List<LowPolyChunk>     _renderLp  = new List<LowPolyChunk>(1024);
        private List<VeryLowPolyChunk> _renderVlp = new List<VeryLowPolyChunk>(512);
        private bool _renderListDirty = true;

        public ChunkManager(GraphicsDevice graphicsDevice, int chunkSize = 16, int loadDistance = 4)
        {
            _graphicsDevice = graphicsDevice;
            _chunkSize      = chunkSize;
            _loadDistance   = loadDistance;
            _lodDistance    = loadDistance * 4;

            _chunks            = new Dictionary<Vector3Int, Chunk>(256);
            _lowPolyChunks     = new Dictionary<Vector3Int, LowPolyChunk>(1024);
            _veryLowPolyChunks = new Dictionary<Vector3Int, VeryLowPolyChunk>(512);
            _pendingHqAfterLp  = new HashSet<Vector3Int>();
            _generationQueue   = new Queue<(Vector3Int, ChunkType, int)>(MAX_QUEUE_SIZE);

            _meshDataQueue          = new Queue<MeshData>(256);
            _lowPolyMeshDataQueue   = new Queue<MeshDataLowPoly>(512);
            _veryLowPolyMeshDataQueue = new Queue<MeshDataVeryLowPoly>(256);

            _worldGenerator     = new WorldGenerator(seed: 42);
            _lastPlayerChunkPos = Vector3Int.Zero;
            _lastPlayerPosition = Vector3.Zero;
        }

        private int GetSimplificationLevel(float distXZ)
        {
            float t = distXZ / (LodDistXZ + 0.5f);
            if (t <= 0.33f) return 0;
            if (t <= 0.66f) return 1;
            return 2;
        }

        public void Update(Vector3 playerPosition, BoundingFrustum cameraFrustum = null)
        {
            _lastPlayerPosition = playerPosition;
            Vector3Int currentChunkPos = GetChunkCoordinates(playerPosition);

            if (currentChunkPos != _lastPlayerChunkPos)
            {
                _lastPlayerChunkPos  = currentChunkPos;
                _renderListDirty     = true;
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

            if (_pendingHqAfterLp.Count > 0)
            {
                var toEnqueue = new List<(Vector3Int, ChunkType, int)>();
                lock (_chunkLock) { PromotePendingToHQ(toEnqueue); }
                if (toEnqueue.Count > 0)
                {
                    lock (_queueLock)
                    {
                        var existing = _generationQueue.ToArray();
                        _generationQueue.Clear();
                        foreach (var e in toEnqueue)  _generationQueue.Enqueue(e);
                        foreach (var e in existing)   _generationQueue.Enqueue(e);
                    }
                    _renderListDirty = true;
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

        private void UpdateVisibleChunks(Vector3Int centerChunk, Vector3 playerPosition)
        {
            var highQualityZone = new HashSet<Vector3Int>();
            var lowPolyZone     = new HashSet<Vector3Int>();
            var veryLowPolyZone = new HashSet<Vector3Int>();
            var chunksToGenerate = new List<(Vector3Int pos, ChunkType type, int level)>();

            (Vector3Int pos, ChunkType type, int level)[] previousQueue;
            lock (_queueLock)
                previousQueue = _generationQueue.ToArray();

            const int HqDistY = 3;
            for (int x = -_loadDistance; x <= _loadDistance; x++)
                for (int y = -HqDistY; y <= HqDistY; y++)
                    for (int z = -_loadDistance; z <= _loadDistance; z++)
                    {
                        int cy = centerChunk.Y + y; if (cy < 0) continue;
                        if ((float)Math.Sqrt(x*x + z*z) <= _loadDistance + 0.5f)
                            highQualityZone.Add(new Vector3Int(centerChunk.X+x, cy, centerChunk.Z+z));
                    }

            int lodXZ = LodDistXZ;
            for (int x = -lodXZ; x <= lodXZ; x++)
                for (int y = -LodDistY; y <= LodDistY; y++)
                    for (int z = -lodXZ; z <= lodXZ; z++)
                    {
                        int cy = 1 + y; if (cy < 0) continue;
                        var cp = new Vector3Int(centerChunk.X+x, cy, centerChunk.Z+z);
                        if ((float)Math.Sqrt(x*x + z*z) > lodXZ + 0.5f) continue;
                        if (highQualityZone.Contains(cp)) continue;
                        lowPolyZone.Add(cp);
                    }

            if (_enableVeryLowPoly)
            {
                int vlpXZ = VlpDistXZ;
                for (int x = -vlpXZ; x <= vlpXZ; x++)
                    for (int y = -VlpDistY; y <= VlpDistY; y++)
                        for (int z = -vlpXZ; z <= vlpXZ; z++)
                        {
                            int cy = 1 + y; if (cy < 0) continue;
                            var cp = new Vector3Int(centerChunk.X+x, cy, centerChunk.Z+z);
                            if ((float)Math.Sqrt(x*x + z*z) > vlpXZ + 0.5f) continue;
                            if (highQualityZone.Contains(cp)) continue;
                            if (lowPolyZone.Contains(cp)) continue;
                            veryLowPolyZone.Add(cp);
                        }
            }

            lock (_chunkLock)
            {
                var stalePending = _pendingHqAfterLp
                    .Where(p => !highQualityZone.Contains(p) || !_lowPolyChunks.ContainsKey(p)).ToList();
                foreach (var p in stalePending) _pendingHqAfterLp.Remove(p);

                foreach (var cp in _lowPolyChunks.Keys.ToList())
                {
                    if (!highQualityZone.Contains(cp)) continue;
                    if (_chunks.ContainsKey(cp)) continue;
                    if (_pendingHqAfterLp.Contains(cp)) continue;
                    _chunks[cp] = new Chunk(cp.X, cp.Y, cp.Z, _chunkSize);
                    chunksToGenerate.Add((cp, ChunkType.HighQuality, 0));
                    RemoveVlpAt(cp);
                }

                foreach (var cp in highQualityZone)
                {
                    if (_chunks.ContainsKey(cp)) continue;
                    if (_pendingHqAfterLp.Contains(cp)) continue;
                    if (_lowPolyChunks.ContainsKey(cp))
                    {
                        _chunks[cp] = new Chunk(cp.X, cp.Y, cp.Z, _chunkSize);
                        chunksToGenerate.Add((cp, ChunkType.HighQuality, 0));
                        RemoveVlpAt(cp);
                    }
                    else
                    {
                        _lowPolyChunks[cp] = new LowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize);
                        chunksToGenerate.Add((cp, ChunkType.LowPoly, LowPolyChunk.LOD_LEVELS - 1));
                        _pendingHqAfterLp.Add(cp);
                    }
                }

                var hqToRemove = _chunks.Keys.Where(p => !highQualityZone.Contains(p)).ToList();
                foreach (var cp in hqToRemove)
                {
                    _chunks[cp].Dispose();
                    _chunks.Remove(cp);
                    _pendingHqAfterLp.Remove(cp);
                    if (lowPolyZone.Contains(cp))
                    {
                        if (!_lowPolyChunks.ContainsKey(cp))
                            _lowPolyChunks[cp] = new LowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize);
                        var lp = _lowPolyChunks[cp];
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            if (lp.NeedsMesh(lvl)) chunksToGenerate.Add((cp, ChunkType.LowPoly, lvl));
                        RemoveVlpAt(cp);
                    }
                }

                foreach (var cp in lowPolyZone)
                {
                    if (_chunks.ContainsKey(cp)) continue;
                    if (!_lowPolyChunks.ContainsKey(cp))
                    {
                        _lowPolyChunks[cp] = new LowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize);
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            chunksToGenerate.Add((cp, ChunkType.LowPoly, lvl));
                    }
                    else
                    {
                        var lp = _lowPolyChunks[cp];
                        for (int lvl = LowPolyChunk.LOD_LEVELS - 1; lvl >= 0; lvl--)
                            if (lp.NeedsMesh(lvl)) chunksToGenerate.Add((cp, ChunkType.LowPoly, lvl));
                        RemoveVlpAt(cp);
                    }
                }

                int lodXZFar = LodDistXZ + 2;
                var lpToRemove = _lowPolyChunks.Keys.Where(p =>
                {
                    int dx=p.X-centerChunk.X, dz=p.Z-centerChunk.Z, dy=p.Y-1;
                    return dx*dx+dz*dz > lodXZFar*lodXZFar || Math.Abs(dy) > LodDistY+1;
                }).ToList();
                foreach (var cp in lpToRemove)
                {
                    _pendingHqAfterLp.Remove(cp);
                    _lowPolyChunks[cp].Dispose();
                    _lowPolyChunks.Remove(cp);
                }

                if (_enableVeryLowPoly)
                {
                    foreach (var cp in veryLowPolyZone)
                    {
                        if (!_veryLowPolyChunks.ContainsKey(cp))
                        {
                            _veryLowPolyChunks[cp] = new VeryLowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize);
                            chunksToGenerate.Add((cp, ChunkType.VeryLowPoly, 0));
                        }
                        else
                        {
                            var vlp = _veryLowPolyChunks[cp];
                            if (!vlp.HasMesh && !vlp.IsMeshBuilding && vlp.IsDirty)
                                chunksToGenerate.Add((cp, ChunkType.VeryLowPoly, 0));
                        }
                    }

                    int vlpXZFar = VlpDistXZ + 3;
                    var vlpToRemove = _veryLowPolyChunks.Keys.Where(p =>
                    {
                        int dx=p.X-centerChunk.X, dz=p.Z-centerChunk.Z, dy=p.Y-1;
                        return dx*dx+dz*dz > vlpXZFar*vlpXZFar || Math.Abs(dy) > VlpDistY+1;
                    }).ToList();
                    foreach (var cp in vlpToRemove)
                    {
                        _veryLowPolyChunks[cp].Dispose();
                        _veryLowPolyChunks.Remove(cp);
                    }
                }
            }

            UpdateActiveLevels(centerChunk);

            // ── Merge con cola previa – pre-computa distancias ANTES del sort ─
            var finalSet  = new HashSet<(Vector3Int, ChunkType, int)>(chunksToGenerate);
            var finalList = new List<(Vector3Int pos, ChunkType type, int level)>(chunksToGenerate);

            lock (_chunkLock)
            {
                foreach (var e in previousQueue)
                {
                    if (finalSet.Contains(e)) continue;
                    bool stillNeeded = e.type switch
                    {
                        ChunkType.HighQuality => _chunks.TryGetValue(e.pos, out var hq)   && !hq.HasMesh  && !hq.IsMeshBuilding,
                        ChunkType.LowPoly     => _lowPolyChunks.TryGetValue(e.pos, out var lp) && lp.NeedsMesh(e.level),
                        _                     => _veryLowPolyChunks.TryGetValue(e.pos, out var vlp) && !vlp.HasMesh && !vlp.IsMeshBuilding,
                    };
                    if (!stillNeeded) continue;
                    finalSet.Add(e);
                    finalList.Add(e);
                }
            }

            // Pre-computa distancia una sola vez por entrada para evitar
            // recalcularla O(n log n) veces dentro del comparador.
            float px = (float)Math.Floor(playerPosition.X / _chunkSize);
            float pz = (float)Math.Floor(playerPosition.Z / _chunkSize);

            var distCache = new Dictionary<Vector3Int, float>(finalList.Count);
            foreach (var e in finalList)
            {
                if (!distCache.ContainsKey(e.pos))
                {
                    float dx = e.pos.X - px, dz = e.pos.Z - pz;
                    distCache[e.pos] = dx*dx + dz*dz;
                }
            }

            finalList.Sort((a, b) =>
            {
                int ta = TypePriority(a.type), tb = TypePriority(b.type);
                if (ta != tb) return ta.CompareTo(tb);
                int dc = distCache[a.pos].CompareTo(distCache[b.pos]);
                if (dc != 0) return dc;
                if (a.type == ChunkType.LowPoly) { int lc = b.level.CompareTo(a.level); if (lc != 0) return lc; }
                int cx = a.pos.X.CompareTo(b.pos.X); if (cx != 0) return cx;
                int cy = a.pos.Y.CompareTo(b.pos.Y); if (cy != 0) return cy;
                int cz = a.pos.Z.CompareTo(b.pos.Z); if (cz != 0) return cz;
                return a.level.CompareTo(b.level);
            });

            lock (_queueLock)
            {
                _generationQueue.Clear();
                foreach (var e in finalList) _generationQueue.Enqueue(e);
            }
        }

        private void RemoveVlpAt(Vector3Int cp)
        {
            if (_veryLowPolyChunks.TryGetValue(cp, out var vlp))
            {
                vlp.Dispose();
                _veryLowPolyChunks.Remove(cp);
            }
        }

        private static int TypePriority(ChunkType t) => t switch
        { ChunkType.HighQuality => 0, ChunkType.LowPoly => 1, _ => 2 };

        private void PromotePendingToHQ(List<(Vector3Int, ChunkType, int)> toEnqueue)
        {
            var promoted = new List<Vector3Int>();
            foreach (var cp in _pendingHqAfterLp)
            {
                if (!_lowPolyChunks.TryGetValue(cp, out var lp)) { promoted.Add(cp); continue; }
                if (!lp.HasMeshForLevel(LowPolyChunk.LOD_LEVELS - 1)) continue;
                if (!_chunks.ContainsKey(cp))
                {
                    _chunks[cp] = new Chunk(cp.X, cp.Y, cp.Z, _chunkSize);
                    toEnqueue.Add((cp, ChunkType.HighQuality, 0));
                    RemoveVlpAt(cp);
                }
                promoted.Add(cp);
            }
            foreach (var p in promoted) _pendingHqAfterLp.Remove(p);
        }

        private void UpdateActiveLevels(Vector3Int centerChunk)
        {
            lock (_chunkLock)
            {
                foreach (var (cp, lp) in _lowPolyChunks)
                {
                    int dx = cp.X - centerChunk.X, dz = cp.Z - centerChunk.Z;
                    float dist = (float)Math.Sqrt(dx*dx + dz*dz);
                    lp.ActiveLevel = FindBestAvailableLevel(lp, GetSimplificationLevel(dist));
                }
            }
        }

        private static int FindBestAvailableLevel(LowPolyChunk chunk, int desired)
        {
            if (chunk.HasMeshForLevel(desired)) return desired;
            for (int offset = 1; offset < LowPolyChunk.LOD_LEVELS; offset++)
            {
                int lower = desired - offset, upper = desired + offset;
                if (lower >= 0 && chunk.HasMeshForLevel(lower)) return lower;
                if (upper < LowPolyChunk.LOD_LEVELS && chunk.HasMeshForLevel(upper)) return upper;
            }
            return desired;
        }

        private bool IsChunkInFrustum(int cx, int cy, int cz, BoundingFrustum frustum)
        {
            if (frustum == null) return true;
            var min = new Vector3(cx * _chunkSize, cy * _chunkSize, cz * _chunkSize);
            return frustum.Intersects(new BoundingBox(min, min + new Vector3(_chunkSize)));
        }

        private enum ChunkType { HighQuality, LowPoly, VeryLowPoly }

        // ── Despacha TODOS los slots libres en un solo frame ─────────────────
        private void ProcessGenerationQueue()
        {
            while (true)
            {
                int active = Volatile.Read(ref _activeGenerationTasks);
                if (active >= _maxConcurrentTasks) return;

                (Vector3Int pos, ChunkType type, int level) entry;
                lock (_queueLock)
                {
                    if (_generationQueue.Count == 0) return;
                    entry = _generationQueue.Dequeue();
                }
                StartChunkGenerationTask(entry.pos, entry.type, entry.level);
            }
        }

        private void StartChunkGenerationTask(Vector3Int cp, ChunkType type, int level)
        {
            lock (_chunkLock)
            {
                bool exists = type switch
                {
                    ChunkType.HighQuality => _chunks.ContainsKey(cp),
                    ChunkType.LowPoly     => _lowPolyChunks.ContainsKey(cp),
                    _                     => _veryLowPolyChunks.ContainsKey(cp)
                };
                if (!exists) return;

                if (type == ChunkType.HighQuality && _chunks.TryGetValue(cp, out var hqCheck))
                {
                    if (hqCheck.HasMesh || hqCheck.IsMeshBuilding) return;
                    hqCheck.MarkMeshBuildStart();
                }
                if (type == ChunkType.LowPoly && _lowPolyChunks.TryGetValue(cp, out var lpCheck))
                {
                    if (lpCheck.HasMeshForLevel(level) || lpCheck.IsMeshBuildingForLevel(level)) return;
                    lpCheck.MarkMeshBuildStart(level);
                }
                if (type == ChunkType.VeryLowPoly && _veryLowPolyChunks.TryGetValue(cp, out var vlpCheck))
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
                        case ChunkType.HighQuality: GenerateNormalChunkTask(cp);        break;
                        case ChunkType.LowPoly:     GenerateLowPolyChunkTask(cp, level); break;
                        case ChunkType.VeryLowPoly: GenerateVeryLowPolyChunkTask(cp);  break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChunkManager] Error {type} {cp}: {ex.Message}");
                    if (type == ChunkType.HighQuality)
                        lock (_chunkLock) { if (_chunks.TryGetValue(cp, out var c)) c.MarkMeshEmpty(); }
                    if (type == ChunkType.VeryLowPoly)
                        lock (_chunkLock) { if (_veryLowPolyChunks.TryGetValue(cp, out var v)) v.MarkMeshFailed(); }
                }
                finally
                {
                    Interlocked.Decrement(ref _activeGenerationTasks);
                    _renderListDirty = true;
                }
            });
        }

        private void GenerateNormalChunkTask(Vector3Int cp)
        {
            byte[,,] blocks = _worldGenerator.GetOrGenerateChunk(cp.X, cp.Y, cp.Z, _chunkSize);
            Chunk chunk = null;
            lock (_chunkLock)
            {
                if (_chunks.TryGetValue(cp, out chunk))
                    chunk.SetBlocks(blocks);
            }
            if (chunk == null) return;

            Chunk[,,] neighbors = new Chunk[3, 3, 3];
            lock (_chunkLock)
            {
                for (int dx=-1;dx<=1;dx++) for (int dy=-1;dy<=1;dy++) for (int dz=-1;dz<=1;dz++)
                    _chunks.TryGetValue(new Vector3Int(cp.X+dx, cp.Y+dy, cp.Z+dz), out neighbors[dx+1,dy+1,dz+1]);
            }

            var mesher = new GreedyMesher(chunk, neighbors, _chunkSize);
            var (vertices, indices, debugInfo) = mesher.GenerateMesh();

            lock (_chunkLock) { if (!_chunks.ContainsKey(cp)) return; }

            if (vertices != null && indices != null)
                lock (_hqMeshQueueLock)
                    _meshDataQueue.Enqueue(new MeshData { ChunkPos=cp, Vertices=vertices, Indices=indices, DebugInfo=debugInfo });
            else
                lock (_chunkLock) { if (_chunks.TryGetValue(cp, out var c)) c.MarkMeshEmpty(); }
        }

        private void GenerateLowPolyChunkTask(Vector3Int cp, int level)
        {
            lock (_chunkLock)
            {
                if (!_lowPolyChunks.TryGetValue(cp, out var lpCheck)) return;
                if (lpCheck.HasMeshForLevel(level)) return;
            }
            byte[,,] blocks = _worldGenerator.GenerateLowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize, simplificationLevel: level);
            lock (_chunkLock) { if (_lowPolyChunks.TryGetValue(cp, out var lp)) lp.SetBlocksForLevel(blocks, level); }

            var tmp = new LowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize);
            tmp.SetBlocksForLevel(blocks, 0);
            var mesher = new SimpleLowPolyMesher(tmp, _chunkSize, simplificationLevel: level);
            var (vertices, indices) = mesher.GenerateMesh();

            if (vertices != null && indices != null)
                lock (_lpMeshQueueLock)
                    _lowPolyMeshDataQueue.Enqueue(new MeshDataLowPoly { ChunkPos=cp, Vertices=vertices, Indices=indices, Level=level });
            else
                lock (_chunkLock) { if (_lowPolyChunks.TryGetValue(cp, out var lp)) lp.SetMeshData(null, null, _graphicsDevice, level); }
        }

        private void GenerateVeryLowPolyChunkTask(Vector3Int cp)
        {
            int[,] heightMap = _worldGenerator.GenerateVeryLowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize);
            var tmp = new VeryLowPolyChunk(cp.X, cp.Y, cp.Z, _chunkSize);
            try { tmp.SetHeightMap(heightMap); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VLP] SetHeightMap failed {cp}: {ex.Message}");
                lock (_chunkLock) { if (_veryLowPolyChunks.TryGetValue(cp, out var v)) v.MarkMeshFailed(); }
                return;
            }
            var mesher = new VeryLowPolyMesher(tmp, _chunkSize);
            var (vertices, indices) = mesher.GenerateMesh();

            if (vertices != null && indices != null)
                lock (_vlpMeshQueueLock)
                    _veryLowPolyMeshDataQueue.Enqueue(new MeshDataVeryLowPoly { ChunkPos=cp, Vertices=vertices, Indices=indices });
            else
                lock (_chunkLock) { if (_veryLowPolyChunks.TryGetValue(cp, out var v)) v.MarkMeshFailed(); }
        }

        // ── Procesa cada queue con su propio lock (sin serialización cruzada) ─
        private void ProcessMeshDataQueue()
        {
            lock (_hqMeshQueueLock)
            {
                while (_meshDataQueue.Count > 0)
                {
                    var md = _meshDataQueue.Dequeue();
                    lock (_chunkLock)
                    {
                        if (_chunks.TryGetValue(md.ChunkPos, out var c) && !c.HasMesh)
                        {
                            c.SetMeshData(md.Vertices, md.Indices, _graphicsDevice, md.DebugInfo);
                            _renderListDirty = true;
                        }
                    }
                }
            }
            lock (_lpMeshQueueLock)
            {
                while (_lowPolyMeshDataQueue.Count > 0)
                {
                    var md = _lowPolyMeshDataQueue.Dequeue();
                    lock (_chunkLock)
                    {
                        if (_lowPolyChunks.TryGetValue(md.ChunkPos, out var c))
                        {
                            c.SetMeshData(md.Vertices, md.Indices, _graphicsDevice, md.Level);
                            _renderListDirty = true;
                        }
                    }
                }
            }
            lock (_vlpMeshQueueLock)
            {
                while (_veryLowPolyMeshDataQueue.Count > 0)
                {
                    var md = _veryLowPolyMeshDataQueue.Dequeue();
                    lock (_chunkLock)
                    {
                        if (_veryLowPolyChunks.TryGetValue(md.ChunkPos, out var c))
                        {
                            c.SetMeshData(md.Vertices, md.Indices, _graphicsDevice);
                            _renderListDirty = true;
                        }
                    }
                }
            }
        }

        // ── Reconstruye la render list solo cuando algo cambió ───────────────
        private void RebuildRenderListIfNeeded()
        {
            if (!_renderListDirty) return;
            _renderListDirty = false;

            lock (_chunkLock)
            {
                _renderHq.Clear();
                foreach (var c in _chunks.Values)
                    if (c.HasMesh) _renderHq.Add(c);

                _renderLp.Clear();
                foreach (var c in _lowPolyChunks.Values)
                    _renderLp.Add(c);

                _renderVlp.Clear();
                if (_enableVeryLowPoly)
                    foreach (var c in _veryLowPolyChunks.Values)
                        if (c.HasMesh) _renderVlp.Add(c);
            }
        }

        public void Draw(BasicEffect effect, BoundingFrustum cameraFrustum,
                         Vector3Int? currentChunk = null, bool wireframeOnly = false)
        {
            RebuildRenderListIfNeeded();

            lock (_chunkLock)
            {
                foreach (var chunk in _renderHq)
                {
                    if (wireframeOnly && currentChunk.HasValue &&
                        (chunk.X != currentChunk.Value.X || chunk.Z != currentChunk.Value.Z)) continue;
                    if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum)) continue;
                    effect.World = Matrix.CreateTranslation(chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);
                }

                foreach (var chunk in _renderLp)
                {
                    if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum)) continue;
                    var hqPos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                    if (_chunks.TryGetValue(hqPos, out var hq) && hq.HasMesh) continue;
                    int renderLevel = FindBestAvailableLevel(chunk, chunk.ActiveLevel);
                    if (!chunk.HasMeshForLevel(renderLevel)) continue;
                    int saved = chunk.ActiveLevel;
                    chunk.ActiveLevel = renderLevel;
                    effect.World = Matrix.CreateTranslation(chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);
                    chunk.ActiveLevel = saved;
                }

                foreach (var chunk in _renderVlp)
                {
                    var pos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                    if (_chunks.TryGetValue(pos, out var hq) && hq.HasMesh) continue;
                    if (_lowPolyChunks.TryGetValue(pos, out var lp))
                    {
                        int best = FindBestAvailableLevel(lp, lp.ActiveLevel);
                        if (lp.HasMeshForLevel(best)) continue;
                    }
                    if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum)) continue;
                    effect.World = Matrix.CreateTranslation(chunk.X * _chunkSize, chunk.Y * _chunkSize, chunk.Z * _chunkSize);
                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);
                }
            }
        }

        // ── API pública ──────────────────────────────────────────────────────

        public Chunk GetChunk(Vector3Int pos)
        { lock (_chunkLock) { _chunks.TryGetValue(pos, out var c); return c; } }

        public byte GetBlockAtWorldPosition(Vector3 worldPos)
        {
            var cp    = GetChunkCoordinates(worldPos);
            var chunk = GetChunk(cp);
            if (chunk == null) return BlockType.Air;
            int lx = (int)worldPos.X % _chunkSize, ly = (int)worldPos.Y % _chunkSize, lz = (int)worldPos.Z % _chunkSize;
            if (lx < 0) lx += _chunkSize; if (ly < 0) ly += _chunkSize; if (lz < 0) lz += _chunkSize;
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
                    foreach (var c in _veryLowPolyChunks.Values) if (c.HasMesh || c.IsMeshBuilding) vlp++;
                    return _chunks.Count + lp + vlp;
                }
            }
        }

        public int TotalChunkEntries   { get { lock (_chunkLock) return _chunks.Count + _lowPolyChunks.Count + _veryLowPolyChunks.Count; } }
        public int GenerationQueueCount { get { lock (_queueLock) return _generationQueue.Count; } }
        public int ActiveGenerationTasks => _activeGenerationTasks;
        public int PendingHqCount        { get { lock (_chunkLock) return _pendingHqAfterLp.Count; } }
        public int VeryLowPolyChunkCount { get { lock (_chunkLock) { int n=0; foreach (var c in _veryLowPolyChunks.Values) if (c.HasMesh) n++; return n; } } }

        public void Dispose()
        {
            lock (_chunkLock)
            {
                foreach (var c in _chunks.Values)            c.Dispose();
                foreach (var c in _lowPolyChunks.Values)     c.Dispose();
                foreach (var c in _veryLowPolyChunks.Values) c.Dispose();
                _chunks.Clear(); _lowPolyChunks.Clear(); _veryLowPolyChunks.Clear();
                _pendingHqAfterLp.Clear();
            }
        }

        private Vector3 ChunkCenter(Vector3Int pos) =>
            new Vector3(pos.X * _chunkSize + _chunkSize / 2f,
                        pos.Y * _chunkSize + _chunkSize / 2f,
                        pos.Z * _chunkSize + _chunkSize / 2f);
    }

    internal struct MeshData           { public Vector3Int ChunkPos; public VertexPositionNormalColor[] Vertices; public ushort[] Indices; public ChunkDebugInfo DebugInfo; }
    internal struct MeshDataLowPoly    { public Vector3Int ChunkPos; public VertexPositionNormalColor[] Vertices; public ushort[] Indices; public int Level; }
    internal struct MeshDataVeryLowPoly{ public Vector3Int ChunkPos; public VertexPositionNormalColor[] Vertices; public ushort[] Indices; }

    public struct Vector3Int : IEquatable<Vector3Int>
    {
        public int X, Y, Z;
        public Vector3Int(int x, int y, int z) { X=x; Y=y; Z=z; }
        public static Vector3Int Zero => new Vector3Int(0,0,0);
        public static Vector3Int One  => new Vector3Int(1,1,1);
        public override bool Equals(object obj) => obj is Vector3Int o && Equals(o);
        public bool Equals(Vector3Int o) => X==o.X && Y==o.Y && Z==o.Z;
        public override int GetHashCode() => HashCode.Combine(X,Y,Z);
        public static bool operator ==(Vector3Int a, Vector3Int b) => a.Equals(b);
        public static bool operator !=(Vector3Int a, Vector3Int b) => !a.Equals(b);
        public static Vector3Int operator +(Vector3Int a, Vector3Int b) => new Vector3Int(a.X+b.X, a.Y+b.Y, a.Z+b.Z);
        public override string ToString() => $"({X},{Y},{Z})";
    }
}