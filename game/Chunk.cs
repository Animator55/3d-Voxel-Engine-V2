using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    /// <summary>
    /// Representa un chunk único del mundo.
    /// Un chunk es un cubo de bloques (típicamente 16x16x16) que se renderiza como una unidad.
    /// 
    /// DISEÑO MULTITHREADING:
    /// - Los datos de bloques (blocks) pueden ser escritos desde threads workers.
    /// - isDirty se marca desde threads workers y se chequea en el main thread.
    /// - La creación de VertexBuffer/IndexBuffer se hace SOLO en el main thread.
    /// </summary>
    public class Chunk
    {
        // ============ Datos del Chunk ============
        public int X { get; private set; }  // Coordenada X en espacio de chunks
        public int Y { get; private set; }  // Coordenada Y en espacio de chunks
        public int Z { get; private set; }  // Coordenada Z en espacio de chunks

        private byte[,,] _blocks;           // Array 3D de bloques
        private readonly int _size;         // Tamaño del chunk (típicamente 16)

        // ============ Estado y Renderizado ============
        private bool _isDirty;              // ¿La malla necesita regenerarse?
        private bool _isMeshBuilding;       // ¿Se está construyendo la malla en otro thread?
        
        private VertexPositionNormalColor[] _vertices;
        private ushort[] _indices;
        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;
        
        private BoundingBox _boundingBox;   // Para frustum culling

        // ============ Lock para acceso según Thread ============
        private readonly object _meshLock = new object();

        // ============ Constructor ============
        public Chunk(int x, int y, int z, int size = 16)
        {
            X = x;
            Y = y;
            Z = z;
            _size = size;
            
            // Inicializar array de bloques
            _blocks = new byte[size, size, size];
            Array.Clear(_blocks, 0, _blocks.Length);
            
            _isDirty = true;
            _isMeshBuilding = false;
            _vertices = null;
            _indices = null;
            _vertexBuffer = null;
            _indexBuffer = null;

            // Calcular bounding box en coordenadas mundiales
            UpdateBoundingBox();
        }

        // ============ Acceso a Bloques ============
        /// <summary>
        /// Obtiene el tipo de bloque en la posición local del chunk.
        /// Retorna Air si está fuera de límites.
        /// </summary>
        public byte GetBlock(int x, int y, int z)
        {
            if (x < 0 || x >= _size || y < 0 || y >= _size || z < 0 || z >= _size)
                return BlockType.Air;

            return _blocks[x, y, z];
        }

        /// <summary>
        /// Establece el tipo de bloque en la posición local del chunk.
        /// Marca el chunk como dirty para que se regenere la malla.
        /// THREAD-SAFE: puede ser llamado desde threads workers.
        /// </summary>
        public void SetBlock(int x, int y, int z, byte blockType)
        {
            if (x < 0 || x >= _size || y < 0 || y >= _size || z < 0 || z >= _size)
                return;

            if (_blocks[x, y, z] != blockType)
            {
                _blocks[x, y, z] = blockType;
                _isDirty = true;
            }
        }

        /// <summary>
        /// Retorna el array completo de bloques.
        /// Usado durante generación procedural.
        /// </summary>
        public byte[,,] GetBlocks()
        {
            return _blocks;
        }

        /// <summary>
        /// Carga bloques desde un array generado externamente.
        /// Marca como dirty y sin malla construida.
        /// </summary>
        public void SetBlocks(byte[,,] blocks)
        {
            if (blocks.GetLength(0) != _size || 
                blocks.GetLength(1) != _size || 
                blocks.GetLength(2) != _size)
            {
                throw new ArgumentException("Tamaño de bloque incorrecto");
            }

            _blocks = (byte[,,])blocks.Clone();
            _isDirty = true;
        }

        // ============ Estado de Malla ============
        /// <summary>
        /// ¿Necesita este chunk que se regenere su malla?
        /// THREAD-SAFE: puede ser chequeado desde main thread.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// ¿Se está construyendo la malla en un thread worker?
        /// </summary>
        public bool IsMeshBuilding => _isMeshBuilding;

        /// <summary>
        /// ¿Tiene este chunk una malla lista para renderizar?
        /// </summary>
        public bool HasMesh => _vertexBuffer != null && _indices != null;

        /// <summary>
        /// Marca el chunk como "dirty" - necesita regeneración de malla.
        /// Usualmente llamado desde threads workers.
        /// </summary>
        public void MarkDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// Marca que se va a construir la malla (se usa en ChunkManager).
        /// </summary>
        public void MarkMeshBuildStart()
        {
            lock (_meshLock)
            {
                _isMeshBuilding = true;
                _isDirty = false;
            }
        }

        // ============ Construcción de Malla (Main Thread) ============
        /// <summary>
        /// Proporciona los datos de vértices e índices generados en un thread worker.
        /// DEBE ser llamado desde MAIN THREAD para crear VertexBuffer/IndexBuffer.
        /// 
        /// El ChunkManager pasará aquí los datos después de que GreedyMesher los genere.
        /// </summary>
        public void SetMeshData(VertexPositionNormalColor[] vertices, ushort[] indices, GraphicsDevice graphicsDevice)
        {
            lock (_meshLock)
            {
                _vertices = vertices;
                _indices = indices;

                // Limpiar búferes viejos
                _vertexBuffer?.Dispose();
                _indexBuffer?.Dispose();

                // Crear nuevos búferes en main thread
                if (_vertices != null && _vertices.Length > 0 && _indices != null && _indices.Length > 0)
                {
                    _vertexBuffer = new VertexBuffer(graphicsDevice, VertexPositionNormalColor.VertexDeclaration,
                        _vertices.Length, BufferUsage.WriteOnly);
                    _vertexBuffer.SetData(_vertices);

                    _indexBuffer = new IndexBuffer(graphicsDevice, typeof(ushort),
                        _indices.Length, BufferUsage.WriteOnly);
                    _indexBuffer.SetData(_indices);
                }

                _isMeshBuilding = false;
            }
        }

        // ============ Renderizado ============
        /// <summary>
        /// Dibuja el chunk si tiene malla y está dentro del frustum de la cámara.
        /// </summary>
        public void Draw(GraphicsDevice graphicsDevice, BoundingFrustum cameraFrustum)
        {
            // Frustum culling: no dibujar si está completamente fuera de vista
            // Temporralmente deshabilitado para debug
            // if (!cameraFrustum.Intersects(_boundingBox))
            //     return;

            lock (_meshLock)
            {
                if (_vertexBuffer == null || _indexBuffer == null)
                    return;

                graphicsDevice.SetVertexBuffer(_vertexBuffer);
                graphicsDevice.Indices = _indexBuffer;

                // DrawIndexedPrimitives dibuja triángulos usando los índices
                graphicsDevice.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    baseVertex: 0,
                    startIndex: 0,
                    primitiveCount: _indices.Length / 3);  // 3 índices por triángulo
            }
        }

        // ============ Utilidades ============
        /// <summary>
        /// Calcula el bounding box del chunk en coordenadas mundiales.
        /// Usado para frustum culling.
        /// </summary>
        private void UpdateBoundingBox()
        {
            Vector3 min = new Vector3(X * _size, Y * _size, Z * _size);
            Vector3 max = min + new Vector3(_size, _size, _size);
            _boundingBox = new BoundingBox(min, max);
        }

        /// <summary>
        /// Retorna el bounding box para frustum culling.
        /// </summary>
        public BoundingBox GetBoundingBox() => _boundingBox;

        /// <summary>
        /// Retorna la distancia desde un punto a este chunk (para LOD/priorización).
        /// </summary>
        public float GetDistanceTo(Vector3 point)
        {
            Vector3 chunkCenter = new Vector3(
                (X * _size) + _size / 2f,
                (Y * _size) + _size / 2f,
                (Z * _size) + _size / 2f);

            return Vector3.Distance(point, chunkCenter);
        }

        /// <summary>
        /// Libera recursos de GPU.
        /// </summary>
        public void Dispose()
        {
            lock (_meshLock)
            {
                _vertexBuffer?.Dispose();
                _indexBuffer?.Dispose();
                _vertexBuffer = null;
                _indexBuffer = null;
            }
        }
    }
}
