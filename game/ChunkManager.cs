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
    /// Administra todos los chunks del mundo - NUEVA ARQUITECTURA:
    /// 
    /// SISTEMA DE CAPAS:
    /// - LowPoly Chunks: PERSISTENTES, cacheados, son el "fondo" siempre visible
    ///   └─ Se generan en todo el mapa visible, mesh CACHEADA
    ///   └─ Se descartan solo cuando MUY lejanos
    /// 
    /// - HighQuality Chunks: TEMPORALES, reemplazan a lowPoly en renderizado
    ///   └─ Se generan solo en zona cercana (_loadDistance)
    ///   └─ Se descartan cuando salen de zona
    ///   └─ Cuando tienen mesh, reemplazan al lowPoly en render
    /// 
    /// VENTAJAS:
    /// - Queue pequeña (máx 500) — solo genera cercanos
    /// - LowPoly persistente = seamless transitions
    /// - No genera bajo tierra en lowPoly
    /// - Conversión automática: highQuality > lowPoly en render
    /// 
    /// MULTITHREADING:
    /// - Generación de bloques: ThreadPool + cache global
    /// - Greedy meshing: ThreadPool (workers)
    /// - Creación GPU: Main thread
    /// </summary>
    public class ChunkManager
    {
        // CAPAS DE CHUNKS (3 niveles de LOD)
        private readonly Dictionary<Vector3Int, Chunk> _chunks;  // HighQuality (cercano)
        private readonly Dictionary<Vector3Int, LowPolyChunk> _lowPolyChunks;  // LowPoly (medio)
        // private readonly Dictionary<Vector3Int, VeryLowPolyChunk> _veryLowPolyChunks;  // VeryLowPoly (lejano)
        
        // GENERACIÓN CON LÍMITE
        private readonly Queue<Vector3Int> _generationQueue;  // MAX 5000
        private const int MAX_QUEUE_SIZE = 5000;  // LÍMITE DURO para evitar spam
        
        // DATOS DE MALLA
        private readonly Queue<MeshData> _meshDataQueue;
        private readonly Queue<MeshDataLowPoly> _lowPolyMeshDataQueue;
        // private readonly Queue<MeshDataVeryLowPoly> _veryLowPolyMeshDataQueue;
        
        private readonly WorldGenerator _worldGenerator;
        private readonly GraphicsDevice _graphicsDevice;

        private Vector3Int _lastPlayerChunkPos;
        private Vector3 _lastPlayerPosition;
        private readonly int _chunkSize;
        private readonly int _loadDistance;      // Distancia para HighQuality
        private readonly int _lodDistance;       // Distancia para LowPoly (= loadDistance * 2)
        // private readonly int _veryLodDistance;   // Distancia para VeryLowPoly (= loadDistance * 4)
        
        private readonly object _chunkLock = new object();
        private readonly object _queueLock = new object();
        private readonly object _meshQueueLock = new object();

        private int _activeGenerationTasks;

        public ChunkManager(GraphicsDevice graphicsDevice, int chunkSize = 16, int loadDistance = 4)
        {
            _graphicsDevice = graphicsDevice;
            _chunkSize = chunkSize;
            _loadDistance = loadDistance;
            _lodDistance = loadDistance * 2;
            // _veryLodDistance = loadDistance * 4;

            _chunks = new Dictionary<Vector3Int, Chunk>(256);
            _lowPolyChunks = new Dictionary<Vector3Int, LowPolyChunk>(1024);
            // _veryLowPolyChunks = new Dictionary<Vector3Int, VeryLowPolyChunk>(2048);  // Más espacio para ultra lejano
            _generationQueue = new Queue<Vector3Int>(MAX_QUEUE_SIZE);
            _meshDataQueue = new Queue<MeshData>(256);
            _lowPolyMeshDataQueue = new Queue<MeshDataLowPoly>(512);
            // _veryLowPolyMeshDataQueue = new Queue<MeshDataVeryLowPoly>(512);
            _worldGenerator = new WorldGenerator(seed: 42);
            _lastPlayerChunkPos = Vector3Int.Zero;
            _lastPlayerPosition = Vector3.Zero;
            _activeGenerationTasks = 0;
        }

        /// <summary>
        /// Actualiza qué chunks deben estar cargados según la posición del jugador.
        /// NUEVA LÓGICA:
        /// - HighQuality (0 a loadDistance): temporal, se descarta al salir
        /// - LowPoly (0 a lodDistance): persistente, cacheado, siempre visible
        /// 
        /// Se llama cada frame desde Update().
        /// </summary>
        public void Update(Vector3 playerPosition, BoundingFrustum cameraFrustum = null)
        {
            _lastPlayerPosition = playerPosition;

            Vector3Int currentChunkPos = GetChunkCoordinates(playerPosition);

            if (currentChunkPos != _lastPlayerChunkPos)
            {
                _lastPlayerChunkPos = currentChunkPos;
                UpdateVisibleChunks(currentChunkPos, playerPosition);
            }

            ProcessGenerationQueue();
            ProcessMeshDataQueue();
        }

        /// <summary>
        /// Calcula las coordenadas del chunk desde una posición mundial.
        /// Soporta números negativos correctamente.
        /// </summary>
        public Vector3Int GetChunkCoordinates(Vector3 worldPos)
        {
            int x = (int)Math.Floor(worldPos.X / _chunkSize);
            int y = (int)Math.Floor(worldPos.Y / _chunkSize);
            int z = (int)Math.Floor(worldPos.Z / _chunkSize);

            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Actualiza qué chunks deben estar cargados (3 niveles de LOD).
        /// ARQUITECTURA:
        /// 1. HighQuality (cercano): Detalle completo
        /// 2. LowPoly (medio): Superficie simplificada (cubos)
        /// 3. VeryLowPoly (lejano): SOLO heightmap (grid)
        /// Todos con distancia RADIAL (esférica) y mínimo Y=0
        /// </summary>
        private void UpdateVisibleChunks(Vector3Int centerChunk, Vector3 playerPosition)
        {
            var highQualityZone = new HashSet<Vector3Int>();
            var lowPolyZone = new HashSet<Vector3Int>();
            var veryLowPolyZone = new HashSet<Vector3Int>();
            var chunksToGenerate = new List<Vector3Int>();

            // ZONA 1: HighQuality (cercano) - DISTANCIA RADIAL (esférica)
            for (int x = -_loadDistance; x <= _loadDistance; x++)
            {
                for (int y = -_loadDistance; y <= _loadDistance; y++)
                {
                    for (int z = -_loadDistance; z <= _loadDistance; z++)
                    {
                        int chunkY = centerChunk.Y + y;
                        
                        // No generar chunks debajo de Y=0
                        if (chunkY < 0)
                            continue;

                        // Distancia euclidiana para carga RADIAL (esférica)
                        float distChunk = (float)Math.Sqrt(x * x + y * y + z * z);

                        // Generar en radio de _loadDistance chunks
                        if (distChunk <= _loadDistance + 0.5f)  // +0.5f para incluir chunks en la esfera
                        {
                            highQualityZone.Add(new Vector3Int(
                                centerChunk.X + x,
                                chunkY,
                                centerChunk.Z + z));
                        }
                    }
                }
            }

            // ZONA 2: LowPoly (zona medio) - DISTANCIA RADIAL (esférica)
            for (int x = -_lodDistance; x <= _lodDistance; x++)
            {
                for (int y = -_lodDistance; y <= _lodDistance; y++)
                {
                    for (int z = -_lodDistance; z <= _lodDistance; z++)
                    {
                        int chunkY = centerChunk.Y + y;
                        
                        // No generar chunks debajo de Y=0
                        if (chunkY < 0)
                            continue;

                        var chunkPos = new Vector3Int(
                            centerChunk.X + x,
                            chunkY,
                            centerChunk.Z + z);

                        // Distancia euclidiana para carga RADIAL (esférica)
                        float distChunk = (float)Math.Sqrt(x * x + y * y + z * z);

                        // Generar en radio de _lodDistance chunks, y NO en highQualityZone
                        if (distChunk <= _lodDistance + 0.5f && !highQualityZone.Contains(chunkPos))
                        {
                            lowPolyZone.Add(chunkPos);
                        }
                    }
                }
            }

            // ZONA 3: VeryLowPoly (zona muy lejana) - DISTANCIA RADIAL (esférica)
            // for (int x = -_veryLodDistance; x <= _veryLodDistance; x++)
            // {
            //     for (int y = -_veryLodDistance; y <= _veryLodDistance; y++)
            //     {
            //         for (int z = -_veryLodDistance; z <= _veryLodDistance; z++)
            //         {
            //             int chunkY = centerChunk.Y + y;
                        
            //             // No generar chunks debajo de Y=0
            //             if (chunkY < 0)
            //                 continue;

            //             var chunkPos = new Vector3Int(
            //                 centerChunk.X + x,
            //                 chunkY,
            //                 centerChunk.Z + z);

            //             // Distancia euclidiana para carga RADIAL (esférica)
            //             float distChunk = (float)Math.Sqrt(x * x + y * y + z * z);

            //             // Generar en radio de _veryLodDistance chunks, y NO en otras zonas
            //             if (distChunk <= _veryLodDistance + 0.5f && 
            //                 !highQualityZone.Contains(chunkPos) && 
            //                 !lowPolyZone.Contains(chunkPos))
            //             {
            //                 veryLowPolyZone.Add(chunkPos);
            //             }
            //         }
            //     }
            // }

            lock (_chunkLock)
            {
                // 1. PROMOCIONAR LP → HQ (entraron en zona cercana)
                // Si un chunk que era LP entra en HQ zone, convertirlo a HQ
                var lpToPromote = new List<Vector3Int>();
                foreach (var chunkPos in _lowPolyChunks.Keys.ToList())
                {
                    if (highQualityZone.Contains(chunkPos) && !_chunks.ContainsKey(chunkPos))
                    {
                        lpToPromote.Add(chunkPos);
                    }
                }

                foreach (var chunkPos in lpToPromote)
                {
                    // MANTENER LP como respaldo mientras HQ se genera y procesa su mesh
                    // Crear HQ en paralelo - LP seguirá siendo visible hasta que HQ tenga mesh
                    _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                    chunksToGenerate.Add(chunkPos);
                    
                    // LP se borrará automáticamente cuando HQ esté muy lejano (sección 5)
                }

                // 2. CREAR HQ NUEVOS si no existen en ningún lado
                foreach (var chunkPos in highQualityZone)
                {
                    if (!_chunks.ContainsKey(chunkPos) && !_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add(chunkPos);
                    }
                }

                // 3. DESCARTAR HQ que salieron de la zona
                // Si aún están en lowPolyZone, crear LP para reemplazar
                var hqToRemove = new List<Vector3Int>();
                foreach (var chunkPos in _chunks.Keys.ToList())
                {
                    if (!highQualityZone.Contains(chunkPos))
                    {
                        hqToRemove.Add(chunkPos);
                    }
                }

                foreach (var chunkPos in hqToRemove)
                {
                    _chunks[chunkPos].Dispose();
                    _chunks.Remove(chunkPos);
                    
                    // Si aún está en lowPolyZone, crear LP como respaldo
                    if (lowPolyZone.Contains(chunkPos) && !_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add(chunkPos);
                    }
                }

                // 4. CREAR LP NUEVOS en zona LOD si no existen
                foreach (var chunkPos in lowPolyZone)
                {
                    if (!_chunks.ContainsKey(chunkPos) && !_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        _lowPolyChunks[chunkPos] = new LowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add(chunkPos);
                    }
                }

                // 5. PROMOCIONAR LP → VeryLP (salieron de zona LP pero aún en VeryLP)
                // NOTA: NO encolaremos para generar aún - baja prioridad, se generarán después
                // var lpToPromoteVLP = new List<Vector3Int>();
                // foreach (var chunkPos in _lowPolyChunks.Keys.ToList())
                // {
                //     if (veryLowPolyZone.Contains(chunkPos) && !_veryLowPolyChunks.ContainsKey(chunkPos))
                //     {
                //         lpToPromoteVLP.Add(chunkPos);
                //     }
                // }

                // foreach (var chunkPos in lpToPromoteVLP)
                // {
                //     // Crear VeryLowPoly pero NO encolar aún (baja prioridad)
                //     _veryLowPolyChunks[chunkPos] = new VeryLowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                // }

                // // 6. CREAR VeryLP NUEVOS en zona muy lejana si no existen
                // // NOTA: Baja prioridad - no se encolan automáticamente
                // foreach (var chunkPos in veryLowPolyZone)
                // {
                //     if (!_chunks.ContainsKey(chunkPos) && !_lowPolyChunks.ContainsKey(chunkPos) && !_veryLowPolyChunks.ContainsKey(chunkPos))
                //     {
                //         _veryLowPolyChunks[chunkPos] = new VeryLowPolyChunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                //     }
                // }

                // 7. BORRAR LP super lejano (PERMISIVO) - distancia euclidiana
                float superFarDistSq = (_lodDistance + 10) * (_lodDistance + 10);  // Usar squared para evitar sqrt
                var lpToRemove = new List<Vector3Int>();
                foreach (var chunkPos in _lowPolyChunks.Keys.ToList())
                {
                    int dx = chunkPos.X - centerChunk.X;
                    int dy = chunkPos.Y - centerChunk.Y;
                    int dz = chunkPos.Z - centerChunk.Z;
                    float distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq > superFarDistSq)
                    {
                        lpToRemove.Add(chunkPos);
                    }
                }

                foreach (var chunkPos in lpToRemove)
                {
                    _lowPolyChunks[chunkPos].Dispose();
                    _lowPolyChunks.Remove(chunkPos);
                }

                // 8. BORRAR VeryLP super lejano
                // float superFarDistVLPSq = (_veryLodDistance + 15) * (_veryLodDistance + 15);
                // var vlpToRemove = new List<Vector3Int>();
                // foreach (var chunkPos in _veryLowPolyChunks.Keys.ToList())
                // {
                //     int dx = chunkPos.X - centerChunk.X;
                //     int dy = chunkPos.Y - centerChunk.Y;
                //     int dz = chunkPos.Z - centerChunk.Z;
                //     float distSq = dx * dx + dy * dy + dz * dz;

                //     if (distSq > superFarDistVLPSq)
                //     {
                //         vlpToRemove.Add(chunkPos);
                //     }
                // }

                // foreach (var chunkPos in vlpToRemove)
                // {
                //     _veryLowPolyChunks[chunkPos].Dispose();
                //     _veryLowPolyChunks.Remove(chunkPos);
                // }
            }

            // PRIORIZAR por distancia
            chunksToGenerate.Sort((a, b) =>
            {
                Vector3 posA = new Vector3(a.X * _chunkSize + _chunkSize / 2, a.Y * _chunkSize + _chunkSize / 2, a.Z * _chunkSize + _chunkSize / 2);
                Vector3 posB = new Vector3(b.X * _chunkSize + _chunkSize / 2, b.Y * _chunkSize + _chunkSize / 2, b.Z * _chunkSize + _chunkSize / 2);
                float distA = Vector3.Distance(playerPosition, posA);
                float distB = Vector3.Distance(playerPosition, posB);
                return distA.CompareTo(distB);
            });

            // Encolar TODOS los chunks principales (HQ y LP) - alta prioridad
            lock (_queueLock)
            {
                foreach (var chunkPos in chunksToGenerate)
                {
                    _generationQueue.Enqueue(chunkPos);
                }

                // BAJA PRIORIDAD: Encolar VeryLP SOLO cuando la cola está casi vacía
                // Esto asegura que VeryLP no afecte los FPS ni los chunks importantes
                // if (_generationQueue.Count < 10)  // Solo encolar si hay muy poco trabajo pendiente
                // {
                //     var veryLpToGenerate = new List<Vector3Int>();
                    
                    // Buscar VeryLP chunks que aún no tienen mesh
                    // lock (_chunkLock)
                    // {
                    //     foreach (var chunkPos in _veryLowPolyChunks.Keys.ToList())
                    //     {
                    //         if (!_veryLowPolyChunks[chunkPos].HasMesh)
                    //         {
                    //             veryLpToGenerate.Add(chunkPos);
                    //         }
                    //     }
                    // }

                    // Encolar los VeryLP más cercanos (máx 3 por frame)
                    // veryLpToGenerate.Sort((a, b) =>
                    // {
                    //     Vector3 posA = new Vector3(a.X * _chunkSize + _chunkSize / 2, a.Y * _chunkSize + _chunkSize / 2, a.Z * _chunkSize + _chunkSize / 2);
                    //     Vector3 posB = new Vector3(b.X * _chunkSize + _chunkSize / 2, b.Y * _chunkSize + _chunkSize / 2, b.Z * _chunkSize + _chunkSize / 2);
                    //     float distA = Vector3.Distance(playerPosition, posA);
                    //     float distB = Vector3.Distance(playerPosition, posB);
                    //     return distA.CompareTo(distB);
                    // });

                    // int maxVeryLp = Math.Min(3, veryLpToGenerate.Count);  // Máximo 3 VeryLP por frame
                    // for (int i = 0; i < maxVeryLp; i++)
                    // {
                    //     _generationQueue.Enqueue(veryLpToGenerate[i]);
                    // }
                // }
            }
        }

        /// <summary>
        /// Verifica si un chunk está dentro del frustum de la cámara.
        /// </summary>
        private bool IsChunkInFrustum(int chunkX, int chunkY, int chunkZ, BoundingFrustum frustum)
        {
            // Si no hay frustum, renderizar todo (backward compatibility)
            if (frustum == null)
                return true;

            Vector3 chunkMin = new Vector3(chunkX * _chunkSize, chunkY * _chunkSize, chunkZ * _chunkSize);
            Vector3 chunkMax = chunkMin + new Vector3(_chunkSize, _chunkSize, _chunkSize);
            var boundingBox = new BoundingBox(chunkMin, chunkMax);

            return frustum.Intersects(boundingBox);
        }

        /// <summary>

        /// Procesa algunos chunks de la cola de generación.
        /// Se llama cada frame para evitar generar demasiado simultáneamente.
        /// </summary>
        /// <summary>
        /// Procesa algunos chunks de la cola de generación.
        /// Se llama cada frame para evitar generar demasiado simultáneamente.
        /// </summary>
        private enum ChunkType { HighQuality, LowPoly, VeryLowPoly }

        private void ProcessGenerationQueue()
        {
            // Si hay muchas tasks activas, esperar a que terminen
            if (_activeGenerationTasks >= Environment.ProcessorCount * 2)
                return;

            List<(Vector3Int pos, ChunkType type)> toProcess = new();

            // Extraer chunks de la cola SIN hacer locks anidados
            lock (_queueLock)
            {
                while (_generationQueue.Count > 0 && _activeGenerationTasks < Environment.ProcessorCount * 2)
                {
                    var chunkPos = _generationQueue.Dequeue();
                    toProcess.Add((chunkPos, ChunkType.HighQuality));  // Determinaremos tipo después
                }
            }

            // AHORA determinar tipo fuera del lock de queue
            foreach (var (chunkPos, _) in toProcess)
            {
                ChunkType chunkType = ChunkType.HighQuality;
                lock (_chunkLock)
                {
                    // Prioridad: HQ > LP > VeryLP
                    // Un chunk promovido estará en múltiples diccionarios, pero prioridad a HQ
                    if (_chunks.ContainsKey(chunkPos))
                    {
                        chunkType = ChunkType.HighQuality;
                    }
                    else if (_lowPolyChunks.ContainsKey(chunkPos))
                    {
                        chunkType = ChunkType.LowPoly;
                    }
                    // else if (_veryLowPolyChunks.ContainsKey(chunkPos))
                    // {
                    //     chunkType = ChunkType.VeryLowPoly;
                    // }
                }

                StartChunkGenerationTask(chunkPos, chunkType);
            }
        }

        /// <summary>
        /// Inicia una task en el ThreadPool para generar un chunk (bloques + malla).
        /// El tipo de chunk determina qué tipo de generación usar.
        /// </summary>
        private void StartChunkGenerationTask(Vector3Int chunkPos, ChunkType chunkType)
        {
            // Verificación de seguridad: el chunk debe existir en algún diccionario
            lock (_chunkLock)
            {
                bool exists = _chunks.ContainsKey(chunkPos) || _lowPolyChunks.ContainsKey(chunkPos);
                if (!exists)
                {
                    // Chunk fue descargado antes de empezar a generar, ignorar
                    return;
                }
            }

            Interlocked.Increment(ref _activeGenerationTasks);

            Task.Run(() =>
            {
                try
                {
                    switch (chunkType)
                    {
                        case ChunkType.HighQuality:
                            GenerateNormalChunkTask(chunkPos);
                            break;
                        case ChunkType.LowPoly:
                            GenerateLowPolyChunkTask(chunkPos);
                            break;
                        // case ChunkType.VeryLowPoly:
                        //     GenerateVeryLowPolyChunkTask(chunkPos);
                        //     break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error generating chunk {chunkPos}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref _activeGenerationTasks);
                }
            });
        }

        /// <summary>
        /// Genera un chunk normal con detalle completo.
        /// </summary>
        private void GenerateNormalChunkTask(Vector3Int chunkPos)
        {
            // 1. GENERAR BLOQUES DEL CHUNK (en thread worker, con cache global)
            byte[,,] blocks = _worldGenerator.GetOrGenerateChunk(
                chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);

            // 2. OBTENER REFERENCIA AL CHUNK
            Chunk chunk = null;
            lock (_chunkLock)
            {
                if (_chunks.TryGetValue(chunkPos, out chunk))
                {
                    // Establecer los bloques
                    chunk.SetBlocks(blocks);
                }
            }

            if (chunk == null)
                return;  // Chunk fue descargado mientras se generaba

            // 3. GENERAR MALLA USANDO GREEDY MESHING (en thread worker)
            chunk.MarkMeshBuildStart();

            // Obtener chunks vecinos para oclusión (thread-safe: solo lectura)
            Chunk[,,] neighbors = new Chunk[3, 3, 3];
            lock (_chunkLock)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var neighborPos = new Vector3Int(
                                chunkPos.X + dx,
                                chunkPos.Y + dy,
                                chunkPos.Z + dz);

                            _chunks.TryGetValue(neighborPos, out var neighbor);
                            neighbors[dx + 1, dy + 1, dz + 1] = neighbor;
                        }
                    }
                }
            }

            var mesher = new GreedyMesher(chunk, neighbors, _chunkSize);
            var (vertices, indices, debugInfo) = mesher.GenerateMesh();

            // 4. ENCOLAR DATOS DE MALLA PARA MAIN THREAD
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
        }

        /// <summary>
        /// Genera un chunk LOD simplificado para renderizado a distancia.
        /// IMPORTANTE: Obtiene bloques del CACHE completo y usa GenerateLowPolyChunk
        /// para generar SOLO la superficie (sin underground).
        /// </summary>
        private void GenerateLowPolyChunkTask(Vector3Int chunkPos)
        {
            // 1. GENERAR BLOQUES SIMPLIFICADOS (solo superficie, sin underground)
            byte[,,] blocks = _worldGenerator.GenerateLowPolyChunk(
                chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);

            // 2. OBTENER REFERENCIA AL LOWPOLY CHUNK
            LowPolyChunk chunk = null;
            lock (_chunkLock)
            {
                if (_lowPolyChunks.TryGetValue(chunkPos, out chunk))
                {
                    chunk.SetBlocks(blocks);
                }
            }

            if (chunk == null)
                return;

            // 3. GENERAR MALLA SIMPLE (sin greedy meshing, solo cubos básicos)
            chunk.MarkMeshBuildStart();

            var simpleMesher = new SimpleLowPolyMesher(chunk, _chunkSize);
            var (vertices, indices) = simpleMesher.GenerateMesh();

            if (vertices != null && indices != null)
            {
                lock (_meshQueueLock)
                {
                    _lowPolyMeshDataQueue.Enqueue(new MeshDataLowPoly
                    {
                        ChunkPos = chunkPos,
                        Vertices = vertices,
                        Indices = indices
                    });
                }
            }
        }

        /// <summary>
        /// Genera un chunk ULTRA simplificado (solo heightmap) para renderizado a MUCHA distancia.
        /// </summary>
        // private void GenerateVeryLowPolyChunkTask(Vector3Int chunkPos)
        // {
        //     // 1. GENERAR HEIGHTMAP (solo alturas, sin datos 3D completos)
        //     int[,] heightMap = _worldGenerator.GenerateVeryLowPolyChunk(
        //         chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);

        //     // 2. OBTENER REFERENCIA AL VERYLOWPOLY CHUNK
        //     VeryLowPolyChunk chunk = null;
        //     lock (_chunkLock)
        //     {
        //         if (_veryLowPolyChunks.TryGetValue(chunkPos, out chunk))
        //         {
        //             chunk.SetHeightMap(heightMap);
        //         }
        //     }

        //     if (chunk == null)
        //         return;

        //     // 3. GENERAR MALLA ULTRA SIMPLIFICADA (grid basado en heightmap)
        //     chunk.MarkMeshBuildStart();

        //     var verySimpleMesher = new VeryLowPolyMesher(chunk, _chunkSize);
        //     var (vertices, indices) = verySimpleMesher.GenerateMesh();

        //     if (vertices != null && indices != null)
        //     {
        //         lock (_meshQueueLock)
        //         {
        //             _veryLowPolyMeshDataQueue.Enqueue(new MeshDataVeryLowPoly
        //             {
        //                 ChunkPos = chunkPos,
        //                 Vertices = vertices,
        //                 Indices = indices
        //             });
        //         }
        //     }
        // }

        /// <summary>
        /// Procesa datos de malla listos en la cola y los asigna a chunks (MAIN THREAD).
        /// Crea VertexBuffer e IndexBuffer aquí, no en threads workers.
        /// 
        /// IMPORTANTE: LP es PERSISTENTE como fallback. Se mantiene en memoria.
        /// Solo se renderiza si HQ no tiene mesh en la misma posición.
        /// Se borra permanentemente solo cuando está muy lejano (en UpdateVisibleChunks).
        /// </summary>
        private void ProcessMeshDataQueue()
        {
            lock (_meshQueueLock)
            {
                // Procesar colas de chunks normales (HighQuality)
                while (_meshDataQueue.Count > 0)
                {
                    var meshData = _meshDataQueue.Dequeue();

                    lock (_chunkLock)
                    {
                        if (_chunks.TryGetValue(meshData.ChunkPos, out var chunk))
                        {
                            chunk.SetMeshData(meshData.Vertices, meshData.Indices, _graphicsDevice, meshData.DebugInfo);
                            // LP se mantiene como respaldo y solo se renderiza si HQ no tiene mesh
                        }
                    }
                }

                // Procesar colas de chunks lowpoly
                while (_lowPolyMeshDataQueue.Count > 0)
                {
                    var meshData = _lowPolyMeshDataQueue.Dequeue();

                    lock (_chunkLock)
                    {
                        if (_lowPolyChunks.TryGetValue(meshData.ChunkPos, out var chunk))
                        {
                            chunk.SetMeshData(meshData.Vertices, meshData.Indices, _graphicsDevice);
                        }
                    }
                }

                // // Procesar colas de chunks verylowpoly
                // while (_veryLowPolyMeshDataQueue.Count > 0)
                // {
                //     var meshData = _veryLowPolyMeshDataQueue.Dequeue();

                //     lock (_chunkLock)
                //     {
                //         if (_veryLowPolyChunks.TryGetValue(meshData.ChunkPos, out var chunk))
                //         {
                //             chunk.SetMeshData(meshData.Vertices, meshData.Indices, _graphicsDevice);
                //         }
                //     }
                // }
            }
        }

        /// <summary>
        /// Renderiza todos los chunks visibles (normales y lowpoly).
        /// 
        /// LÓGICA DE RENDERIZADO:
        /// 1. Renderiza HQ si existe y tiene mesh → está en frustum
        /// 2. Renderiza LP solo si:
        ///    - NO existe HQ en la misma posición (o HQ sin mesh aún)
        ///    - LP tiene mesh
        ///    - LP está en frustum
        /// 
        /// LP es respaldo permanente, nunca se elimina forzadamente cuando HQ está listo.
        /// </summary>
        public void Draw(BasicEffect effect, BoundingFrustum cameraFrustum, Vector3Int? currentChunk = null, bool wireframeOnly = false)
        {
            lock (_chunkLock)
            {
                // Renderizar chunks de alta calidad (HQ)
                foreach (var chunk in _chunks.Values.ToList())
                {
                    if (wireframeOnly)
                    {
                        if (currentChunk.HasValue && 
                            (chunk.X != currentChunk.Value.X || 
                             chunk.Z != currentChunk.Value.Z))
                            continue;
                    }

                    // Solo renderizar si está en frustum y tiene mesh
                    if (chunk.HasMesh && IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                    {
                        Vector3 chunkWorldPosition = new Vector3(
                            chunk.X * _chunkSize,
                            chunk.Y * _chunkSize,
                            chunk.Z * _chunkSize);

                        effect.World = Matrix.CreateTranslation(chunkWorldPosition);
                        effect.CurrentTechnique.Passes[0].Apply();
                        chunk.Draw(_graphicsDevice, cameraFrustum);
                    }
                }

                // Renderizar chunks lowpoly (respaldo si no hay HQ o HQ sin mesh)
                foreach (var chunk in _lowPolyChunks.Values.ToList())
                {
                    if (!chunk.HasMesh || !IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                        continue;

                    var hqPos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                    bool hqExists = _chunks.TryGetValue(hqPos, out var hqChunk);
                    bool hqHasMesh = hqExists && hqChunk.HasMesh;

                    // Solo renderizar LP si NO hay HQ con mesh en la misma posición
                    if (hqHasMesh)
                        continue;

                    Vector3 chunkWorldPosition = new Vector3(
                        chunk.X * _chunkSize,
                        chunk.Y * _chunkSize,
                        chunk.Z * _chunkSize);

                    effect.World = Matrix.CreateTranslation(chunkWorldPosition);
                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);
                }

                // // Renderizar chunks verylowpoly (respaldo si no hay HQ ni LP)
                // foreach (var chunk in _veryLowPolyChunks.Values.ToList())
                // {
                //     if (!chunk.HasMesh || !IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum))
                //         continue;

                //     var hqPos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                //     bool hqExists = _chunks.TryGetValue(hqPos, out var hqChunk);
                //     bool hqHasMesh = hqExists && hqChunk.HasMesh;
                //     bool lpExists = _lowPolyChunks.TryGetValue(hqPos, out var lpChunk);
                //     bool lpHasMesh = lpExists && lpChunk.HasMesh;

                //     // Solo renderizar VeryLP si NO hay HQ ni LP con mesh en la misma posición
                //     if (hqHasMesh || lpHasMesh)
                //         continue;

                //     Vector3 chunkWorldPosition = new Vector3(
                //         chunk.X * _chunkSize,
                //         chunk.Y * _chunkSize,
                //         chunk.Z * _chunkSize);

                //     effect.World = Matrix.CreateTranslation(chunkWorldPosition);
                //     effect.CurrentTechnique.Passes[0].Apply();
                //     chunk.Draw(_graphicsDevice, cameraFrustum);
                // }
            }
        }

        /// <summary>
        /// Obtiene un chunk en una posición (con bounds checking).
        /// </summary>
        public Chunk GetChunk(Vector3Int pos)
        {
            lock (_chunkLock)
            {
                _chunks.TryGetValue(pos, out var chunk);
                return chunk;
            }
        }

        /// <summary>
        /// Obtiene el bloque en coordenadas mundiales.
        /// </summary>
        public byte GetBlockAtWorldPosition(Vector3 worldPos)
        {
            Vector3Int chunkPos = GetChunkCoordinates(worldPos);
            var chunk = GetChunk(chunkPos);

            if (chunk == null)
                return BlockType.Air;

            int localX = (int)worldPos.X % _chunkSize;
            int localY = (int)worldPos.Y % _chunkSize;
            int localZ = (int)worldPos.Z % _chunkSize;

            // Manejar coordenadas negativas
            if (localX < 0) localX += _chunkSize;
            if (localY < 0) localY += _chunkSize;
            if (localZ < 0) localZ += _chunkSize;

            return chunk.GetBlock(localX, localY, localZ);
        }

        /// <summary>
        /// Retorna el número total de chunks cargados.
        /// </summary>
        public int LoadedChunkCount
        {
            get
            {
                lock (_chunkLock)
                {
                    return _chunks.Count + _lowPolyChunks.Count;
                }
            }
        }

        /// <summary>
        /// Retorna el número de chunks en cola de generación.
        /// </summary>
        public int GenerationQueueCount
        {
            get
            {
                lock (_queueLock)
                {
                    return _generationQueue.Count;
                }
            }
        }

        /// <summary>
        /// Limpia todos los recursos.
        /// </summary>
        public void Dispose()
        {
            lock (_chunkLock)
            {
                foreach (var chunk in _chunks.Values)
                {
                    chunk.Dispose();
                }
                _chunks.Clear();

                foreach (var chunk in _lowPolyChunks.Values)
                {
                    chunk.Dispose();
                }
                _lowPolyChunks.Clear();

                // foreach (var chunk in _veryLowPolyChunks.Values)
                // {
                //     chunk.Dispose();
                // }
                // _veryLowPolyChunks.Clear();
            }
        }
    }

    /// <summary>
    /// Datos de malla listos para ser asignados a un chunk (thread-safe transfer).
    /// Se crea en thread worker y se procesa en main thread.
    /// </summary>
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
    }

    internal struct MeshDataVeryLowPoly
    {
        public Vector3Int ChunkPos;
        public VertexPositionNormalColor[] Vertices;
        public ushort[] Indices;
    }

    /// <summary>
    /// Vector 3D con enteros. Usado para coordenadas de chunks.
    /// No existe en MonoGame, así que lo implementamos.
    /// </summary>
    public struct Vector3Int : IEquatable<Vector3Int>
    {
        public int X, Y, Z;

        public Vector3Int(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3Int Zero => new Vector3Int(0, 0, 0);
        public static Vector3Int One => new Vector3Int(1, 1, 1);

        public override bool Equals(object obj) => obj is Vector3Int other && Equals(other);
        public bool Equals(Vector3Int other) => X == other.X && Y == other.Y && Z == other.Z;
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public static bool operator ==(Vector3Int left, Vector3Int right) => left.Equals(right);
        public static bool operator !=(Vector3Int left, Vector3Int right) => !left.Equals(right);

        public static Vector3Int operator +(Vector3Int a, Vector3Int b) 
            => new Vector3Int(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}