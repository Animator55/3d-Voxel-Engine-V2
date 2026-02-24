using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace game
{
    /// <summary>
    /// Administra todos los chunks del mundo.
    /// 
    /// RESPONSABILIDADES:
    /// - Carga/descarga chunks según distancia al jugador
    /// - Genera chunks en background usando ThreadPool
    /// - Integra GreedyMeshing en threads workers (THREAD-SAFE)
    /// - Transfiere datos de malla al main thread para GPU
    /// - Frustum culling durante renderizado
    /// 
    /// MULTITHREADING:
    /// - Generación de bloques: ThreadPool (workers)
    /// - Greedy meshing: ThreadPool (workers)
    /// - Creación VertexBuffer/IndexBuffer: Main thread
    /// - Diccionario de chunks: Dictionary con locks donde es necesario
    /// </summary>
    public class ChunkManager
    {
        private readonly Dictionary<Vector3Int, Chunk> _chunks;
        private readonly Queue<Vector3Int> _generationQueue;
        private readonly Queue<MeshData> _meshDataQueue;  // Cola de mallas listas para procesar en main thread
        private readonly WorldGenerator _worldGenerator;
        private readonly GraphicsDevice _graphicsDevice;

        private Vector3Int _lastPlayerChunkPos;
        private Vector3 _lastPlayerPosition;  // Guardar posición para priorizar cercanos
        private readonly int _chunkSize;
        private readonly int _loadDistance;  // Chunks cargados en cada dirección
        private readonly object _chunkLock = new object();
        private readonly object _queueLock = new object();
        private readonly object _meshQueueLock = new object();

        private bool _isGenerating;
        private int _activeGenerationTasks;

        public ChunkManager(GraphicsDevice graphicsDevice, int chunkSize = 16, int loadDistance = 2)
        {
            _graphicsDevice = graphicsDevice;
            _chunkSize = chunkSize;
            _loadDistance = loadDistance;

            _chunks = new Dictionary<Vector3Int, Chunk>(256);
            _generationQueue = new Queue<Vector3Int>(256);
            _meshDataQueue = new Queue<MeshData>(256);
            _worldGenerator = new WorldGenerator(seed: 42);
            _lastPlayerChunkPos = Vector3Int.Zero;
            _lastPlayerPosition = Vector3.Zero;
            _isGenerating = true;
            _activeGenerationTasks = 0;
        }

        /// <summary>
        /// Actualiza qué chunks deben estar cargados según la posición del jugador.
        /// Se llama cada frame desde Update().
        /// </summary>
        public void Update(Vector3 playerPosition)
        {
            // Guardar posición para priorizar chunks cercanos
            _lastPlayerPosition = playerPosition;

            // Calcular en qué chunk está el jugador
            Vector3Int currentChunkPos = GetChunkCoordinates(playerPosition);

            // Si el jugador cambió de chunk, recalcular qué es visible
            if (currentChunkPos != _lastPlayerChunkPos)
            {
                _lastPlayerChunkPos = currentChunkPos;
                UpdateVisibleChunks(currentChunkPos, playerPosition);
            }

            // Procesar la cola de generación (no todos al mismo time, sino algunos pocos por frame)
            ProcessGenerationQueue();

            // Procesar datos de malla listos (MAIN THREAD - crear GPU buffers aquí)
            ProcessMeshDataQueue();
        }

        /// <summary>
        /// Calcula las coordenadas del chunk desde una posición mundial.
        /// Soporta números negativos correctamente.
        /// </summary>
        private Vector3Int GetChunkCoordinates(Vector3 worldPos)
        {
            int x = (int)Math.Floor(worldPos.X / _chunkSize);
            int y = (int)Math.Floor(worldPos.Y / _chunkSize);
            int z = (int)Math.Floor(worldPos.Z / _chunkSize);

            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Actualiza qué chunks deben estar cargados/descargados.
        /// Descarga chunks muy lejanos y encola los cercanos para generación (ordenados por distancia).
        /// </summary>
        private void UpdateVisibleChunks(Vector3Int centerChunk, Vector3 playerPosition)
        {
            var visibleChunks = new HashSet<Vector3Int>();
            var chunksToGenerate = new List<Vector3Int>();

            // Determinar qué chunks deben estar cargados
            for (int x = -_loadDistance; x <= _loadDistance; x++)
            {
                for (int y = -_loadDistance; y <= _loadDistance; y++)
                {
                    for (int z = -_loadDistance; z <= _loadDistance; z++)
                    {
                        visibleChunks.Add(new Vector3Int(
                            centerChunk.X + x,
                            centerChunk.Y + y,
                            centerChunk.Z + z));
                    }
                }
            }

            lock (_chunkLock)
            {
                // Descargar chunks que ya no son visibles
                var chunksToRemove = new List<Vector3Int>();
                foreach (var chunkPos in _chunks.Keys)
                {
                    if (!visibleChunks.Contains(chunkPos))
                    {
                        chunksToRemove.Add(chunkPos);
                    }
                }

                foreach (var chunkPos in chunksToRemove)
                {
                    _chunks[chunkPos].Dispose();
                    _chunks.Remove(chunkPos);
                }

                // Encolar chunks visibles que no existen aún, ordenados por distancia
                foreach (var chunkPos in visibleChunks)
                {
                    if (!_chunks.ContainsKey(chunkPos))
                    {
                        _chunks[chunkPos] = new Chunk(chunkPos.X, chunkPos.Y, chunkPos.Z, _chunkSize);
                        chunksToGenerate.Add(chunkPos);
                    }
                }
            }

            // Ordenar chunks por distancia a la cámara (más cercanos primero)
            chunksToGenerate.Sort((a, b) =>
            {
                Vector3 posA = new Vector3(a.X * _chunkSize + _chunkSize / 2, a.Y * _chunkSize + _chunkSize / 2, a.Z * _chunkSize + _chunkSize / 2);
                Vector3 posB = new Vector3(b.X * _chunkSize + _chunkSize / 2, b.Y * _chunkSize + _chunkSize / 2, b.Z * _chunkSize + _chunkSize / 2);
                float distA = Vector3.Distance(playerPosition, posA);
                float distB = Vector3.Distance(playerPosition, posB);
                return distA.CompareTo(distB);
            });

            // Encolar chunks ordenados por proximidad
            foreach (var chunkPos in chunksToGenerate)
            {
                EnqueueChunkGeneration(chunkPos);
            }
        }

        /// <summary>
        /// Encola un chunk para que se genere sus bloques.
        /// </summary>
        private void EnqueueChunkGeneration(Vector3Int chunkPos)
        {
            lock (_queueLock)
            {
                _generationQueue.Enqueue(chunkPos);
            }
        }

        /// <summary>
        /// Procesa algunos chunks de la cola de generación.
        /// Se llama cada frame para evitar generar demasiado simultáneamente.
        /// </summary>
        private void ProcessGenerationQueue()
        {
            // Limitar cantidad de tasks activas (permitir más tasks simultáneas)
            if (_activeGenerationTasks >= Environment.ProcessorCount)
                return;

            lock (_queueLock)
            {
                // Procesar múltiples chunks por frame para ir más rápido
                while (_generationQueue.Count > 0 && _activeGenerationTasks < Environment.ProcessorCount)
                {
                    var chunkPos = _generationQueue.Dequeue();
                    StartChunkGenerationTask(chunkPos);
                }
            }
        }

        /// <summary>
        /// Inicia una task en el ThreadPool para generar un chunk (bloques + malla).
        /// THREAD-SAFE: toda la lógica aquí es segura para threads.
        /// </summary>
        private void StartChunkGenerationTask(Vector3Int chunkPos)
        {
            Interlocked.Increment(ref _activeGenerationTasks);

            Task.Run(() =>
            {
                try
                {
                    // 1. GENERAR BLOQUES DEL CHUNK (en thread worker)
                    byte[,,] blocks = _worldGenerator.GenerateChunk(
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
                    var (vertices, indices) = mesher.GenerateMesh();

                    // 4. ENCOLAR DATOS DE MALLA PARA MAIN THREAD
                    // Esto se completa en el main thread en Update() después
                    if (vertices != null && indices != null)
                    {
                        // Encolar datos de malla para procesamiento en main thread
                        lock (_meshQueueLock)
                        {
                            _meshDataQueue.Enqueue(new MeshData
                            {
                                ChunkPos = chunkPos,
                                Vertices = vertices,
                                Indices = indices
                            });
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _activeGenerationTasks);
                }
            });
        }

        /// <summary>
        /// Procesa datos de malla listos en la cola y los asigna a chunks (MAIN THREAD).
        /// Crea VertexBuffer e IndexBuffer aquí, no en threads workers.
        /// </summary>
        private void ProcessMeshDataQueue()
        {
            lock (_meshQueueLock)
            {
                while (_meshDataQueue.Count > 0)
                {
                    var meshData = _meshDataQueue.Dequeue();

                    lock (_chunkLock)
                    {
                        if (_chunks.TryGetValue(meshData.ChunkPos, out var chunk))
                        {
                            // Crear buffers GPU aquí en el main thread (thread-safe)
                            chunk.SetMeshData(meshData.Vertices, meshData.Indices, _graphicsDevice);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Renderiza todos los chunks visibles.
        /// Aplicar frustum culling en el Draw del Chunk.
        /// </summary>
        public void Draw(BasicEffect effect, BoundingFrustum cameraFrustum)
        {
            lock (_chunkLock)
            {
                foreach (var chunk in _chunks.Values)
                {
                    if (chunk.HasMesh)
                    {
                        // Calcular posición del chunk en el mundo
                        Vector3 chunkWorldPosition = new Vector3(
                            chunk.X * _chunkSize,
                            chunk.Y * _chunkSize,
                            chunk.Z * _chunkSize);

                        // Aplicar matriz de transformación (posición del chunk)
                        effect.World = Matrix.CreateTranslation(chunkWorldPosition);
                        effect.CurrentTechnique.Passes[0].Apply();
                        chunk.Draw(_graphicsDevice, cameraFrustum);
                    }
                }
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
                    return _chunks.Count;
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
            _isGenerating = false;
            lock (_chunkLock)
            {
                foreach (var chunk in _chunks.Values)
                {
                    chunk.Dispose();
                }
                _chunks.Clear();
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
