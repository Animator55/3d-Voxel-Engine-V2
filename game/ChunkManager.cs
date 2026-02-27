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
    /// CHUNK-STRIDE (nivel 3):
    ///   En la corona más lejana se saltean chunks alternos en XZ
    ///   (posiciones impares), reduciendo a ~1/4 los chunks generados.
    /// </summary>
    public class ChunkManager
    {
        // ── Diccionarios de chunks ───────────────────────────────────
        private readonly Dictionary<Vector3Int, Chunk>        _chunks;
        private readonly Dictionary<Vector3Int, LowPolyChunk> _lowPolyChunks;

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
            // Con esfera: subir en Y reducía el radio XZ disponible → chunks cercanos
            // en XZ desaparecían al volar. Con cilindro: el radio XZ es siempre fijo,
            // solo Y está acotado por un rango independiente.
            const int HqDistY = 3; // ±3 chunks en Y = ±96 bloques con chunkSize=32
            for (int x = -_loadDistance; x <= _loadDistance; x++)
            for (int y = -HqDistY; y <= HqDistY; y++)
            for (int z = -_loadDistance; z <= _loadDistance; z++)
            {
                int chunkY = centerChunk.Y + y;
                if (chunkY < 0) continue;

                // Radio XZ circular, Y independiente
                if ((float)Math.Sqrt(x*x + z*z) <= _loadDistance + 0.5f)
                    highQualityZone.Add(new Vector3Int(centerChunk.X + x, chunkY, centerChunk.Z + z));
            }

            // ZONA LP — radio XZ independiente, Y muy acotado.
            // Antes: esfera 3D de radio lodDistance=16 → 18k chunks.
            // Ahora: cilindro XZ con tapa Y → ~1800 chunks.
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
                // 1. Promover LP -> HQ
                foreach (var chunkPos in _lowPolyChunks.Keys.ToList())
                {
                    if (highQualityZone.Contains(chunkPos) && !_chunks.ContainsKey(chunkPos))
                    {
                        _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add((chunkPos, ChunkType.HighQuality, 0));
                    }
                }

                // 2. Crear HQ nuevos
                foreach (var chunkPos in highQualityZone)
                {
                    if (!_chunks.ContainsKey(chunkPos) && !_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add((chunkPos, ChunkType.HighQuality, 0));
                    }
                }

                // 3. Descartar HQ fuera de zona
                var hqToRemove = _chunks.Keys.Where(p => !highQualityZone.Contains(p)).ToList();
                foreach (var chunkPos in hqToRemove)
                {
                    _chunks[chunkPos].Dispose();
                    _chunks.Remove(chunkPos);

                    if (lowPolyZone.Contains(chunkPos) && !_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        // Encolar de peor a mejor calidad: el nivel 3 (más rápido) llega primero
                        // y cubre el chunk; los niveles mejores llegan después sin flashear.
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
                        // Mismo orden: peor calidad primero para que haya algo visible de inmediato
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

                // 5. Borrar LP muy lejanos.
                // lodXZFar = LodDistXZ + 2: margen mínimo para evitar thrashing
                // si el jugador se mueve justo en el borde. Antes era +6, lo que
                // dejaba una corona de ~600 chunks acumulados entre move y move.
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
                    _lowPolyChunks[chunkPos].Dispose();
                    _lowPolyChunks.Remove(chunkPos);
                }
            }

            // Actualizar niveles activos inmediatamente
            UpdateActiveLevels(centerChunk);

            // Orden de prioridad:
            //   1. HQ primero (zona cercana)
            //   2. LP por distancia XZ (más cercano antes)
            //   3. Dentro del mismo chunk LP, nivel más alto = peor calidad = más rápido,
            //      así el chunk tiene algo visible de inmediato y los niveles mejores
            //      lo van refinando sin producir flasheos.
            chunksToGenerate.Sort((a, b) =>
            {
                if (a.type != b.type)
                    return a.type == ChunkType.HighQuality ? -1 : 1;

                // Para LP: priorizar nivel alto (peor calidad) sobre nivel bajo
                // dentro del mismo chunk, para que haya coverage visual rápida.
                if (a.pos.Equals(b.pos))
                    return b.level.CompareTo(a.level); // nivel 3 antes que 0

                // Entre chunks distintos: más cercano primero
                float dA = (float)Math.Sqrt(
                    Math.Pow(a.pos.X - (int)Math.Floor(playerPosition.X / _chunkSize), 2) +
                    Math.Pow(a.pos.Z - (int)Math.Floor(playerPosition.Z / _chunkSize), 2));
                float dB = (float)Math.Sqrt(
                    Math.Pow(b.pos.X - (int)Math.Floor(playerPosition.X / _chunkSize), 2) +
                    Math.Pow(b.pos.Z - (int)Math.Floor(playerPosition.Z / _chunkSize), 2));
                return dA.CompareTo(dB);
            });

            // FIX: Limpiar la cola antes de encolar nuevas entradas.
            // Sin esto, cada cambio de chunk acumula miles de entradas obsoletas
            // que nunca se descartan, causando la explosión de pending/queued.
            lock (_queueLock)
            {
                _generationQueue.Clear();
                foreach (var entry in chunksToGenerate)
                    _generationQueue.Enqueue(entry);
            }
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
            // Sacar de la cola de a uno, respetando el límite real en cada paso.
            // El Volatile.Read garantiza que leemos el valor actualizado por los
            // threads de generación y no un valor cacheado por el compilador/CPU.
            while (Volatile.Read(ref _activeGenerationTasks) < _maxConcurrentTasks)
            {
                (Vector3Int pos, ChunkType type, int level) entry;

                lock (_queueLock)
                {
                    if (_generationQueue.Count == 0) return;
                    entry = _generationQueue.Dequeue();
                }

                // Intentar spawnear. Si el chunk ya no existe o ya tiene mesh,
                // StartChunkGenerationTask retorna sin incrementar — seguimos
                // sacando de la cola hasta llenar el slot o vaciarla.
                StartChunkGenerationTask(entry.pos, entry.type, entry.level);
            }
        }

        private void StartChunkGenerationTask(Vector3Int chunkPos, ChunkType type, int level)
        {
            // Verificar validez bajo lock ANTES de incrementar el contador.
            // Así el Increment solo ocurre cuando realmente vamos a spawnear.
            lock (_chunkLock)
            {
                bool exists = _chunks.ContainsKey(chunkPos) || _lowPolyChunks.ContainsKey(chunkPos);
                if (!exists) return;

                if (type == ChunkType.LowPoly &&
                    _lowPolyChunks.TryGetValue(chunkPos, out var lpCheck))
                {
                    if (lpCheck.HasMeshForLevel(level) || lpCheck.IsMeshBuildingForLevel(level))
                        return;

                    // Marcar como "en construcción" dentro del mismo lock que valida,
                    // evitando que otro thread o la próxima iteración del Update
                    // encole la misma tarea antes de que la task arranque.
                    lpCheck.MarkMeshBuildStart(level);
                }
            }

            // Incrementar solo cuando sabemos que vamos a lanzar la task.
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
                    // MarkMeshBuildStart DENTRO del lock: evita que el chunk sea
                    // descartado por UpdateVisibleChunks entre el TryGetValue y el Mark,
                    // lo que dejaba el chunk en estado "building" sin resolverse nunca.
                    chunk.MarkMeshBuildStart();
                }
            }
            // Si el chunk fue descartado antes de que llegáramos, no hay nada que hacer.
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

            // Verificar que el chunk sigue vivo antes de encolar la mesh.
            // Si fue descartado mientras generábamos, no encolamos nada
            // pero sí resolvemos el estado building para que no quede colgado.
            lock (_chunkLock)
            {
                if (!_chunks.ContainsKey(chunkPos))
                {
                    // Chunk descartado durante la generación: el objeto ya fue
                    // Dispose()-ado por UpdateVisibleChunks, no tocar su estado.
                    return;
                }
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
                // Chunk de aire puro (cueva completa, chunk muy alto, etc).
                // Sin este caso, IsMeshBuilding queda true para siempre
                // y el chunk aparece como "hueco" en el HUD y no se renderiza.
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
            // Verificar que el chunk sigue existiendo (pudo descartarse mientras esperaba en cola)
            lock (_chunkLock)
            {
                if (!_lowPolyChunks.TryGetValue(chunkPos, out var lpCheck)) return;
                if (lpCheck.HasMeshForLevel(level)) return;
                // MarkMeshBuildStart ya fue llamado en StartChunkGenerationTask bajo el mismo lock
            }

            // Generar bloques para este nivel (arboles con densidad distinta)
            byte[,,] blocks = _worldGenerator.GenerateLowPolyChunk(
                chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize,
                simplificationLevel: level);

            // Actualizar bloque-base del chunk real (nivel 0 = referencia compartida)
            lock (_chunkLock)
            {
                if (_lowPolyChunks.TryGetValue(chunkPos, out var lp))
                    lp.SetBlocksForLevel(blocks, level);
            }

            // Usar un chunk temporal para no pisar el estado del chunk real
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
                // FIX: Chunk vacío → SetMeshData(null, null) marca _levelFailed[level] = true
                // en LowPolyChunk, previniendo que NeedsMesh() lo re-encole en el futuro.
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

                // LP (solo si HQ no tiene mesh)
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
        /// Este es el número relevante para el HUD.
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
                        // Contar solo si tiene al menos un nivel con mesh o en construcción
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

        public void Dispose()
        {
            lock (_chunkLock)
            {
                foreach (var c in _chunks.Values)        c.Dispose();
                foreach (var c in _lowPolyChunks.Values) c.Dispose();
                _chunks.Clear();
                _lowPolyChunks.Clear();
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