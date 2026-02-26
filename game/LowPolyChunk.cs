using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    /// <summary>
    /// Chunk LOD simplificado para renderizado a distancia.
    /// Solo genera y renderiza la superficie del terreno + pocas capas debajo.
    /// Mucho más ligero que Chunk normal para chunks lejanos.
    /// </summary>
    public class LowPolyChunk
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }

        private byte[,,] _blocks;           // Array 3D simplificado
        private readonly int _size;
        private readonly int _sampleRate;   // Reducción de detalle (ej: 2 = cada 2 bloques)

        private bool _isDirty;
        private bool _isMeshBuilding;

        private VertexPositionNormalColor[] _vertices;
        private ushort[] _indices;
        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;

        private BoundingBox _boundingBox;
        private readonly object _meshLock = new object();

        public bool IsDirty => _isDirty;
        public bool IsMeshBuilding => _isMeshBuilding;
        public bool HasMesh => _vertexBuffer != null && _indices != null;

        public LowPolyChunk(int x, int y, int z, int size = 16, int sampleRate = 2)
        {
            X = x;
            Y = y;
            Z = z;
            _size = size;
            _sampleRate = sampleRate;

            _blocks = new byte[size, size, size];
            Array.Clear(_blocks, 0, _blocks.Length);

            _isDirty = true;
            _isMeshBuilding = false;
            _vertices = null;
            _indices = null;
            _vertexBuffer = null;
            _indexBuffer = null;

            UpdateBoundingBox();
        }

        public byte GetBlock(int x, int y, int z)
        {
            if (x < 0 || x >= _size || y < 0 || y >= _size || z < 0 || z >= _size)
                return BlockType.Air;

            return _blocks[x, y, z];
        }

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

        public byte[,,] GetBlocks() => _blocks;

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

        public void MarkDirty()
        {
            _isDirty = true;
        }

        public void MarkMeshBuildStart()
        {
            lock (_meshLock)
            {
                _isMeshBuilding = true;
                _isDirty = false;
            }
        }

        /// <summary>
        /// Establece los datos de malla (vértices e índices) generados en thread worker.
        /// </summary>
        public void SetMeshData(VertexPositionNormalColor[] vertices, ushort[] indices, GraphicsDevice graphicsDevice)
        {
            lock (_meshLock)
            {
                _vertices = vertices;
                _indices = indices;

                _vertexBuffer?.Dispose();
                _indexBuffer?.Dispose();

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

        public void Draw(GraphicsDevice graphicsDevice, BoundingFrustum cameraFrustum)
        {
            lock (_meshLock)
            {
                if (_vertexBuffer == null || _indexBuffer == null)
                    return;

                graphicsDevice.SetVertexBuffer(_vertexBuffer);
                graphicsDevice.Indices = _indexBuffer;

                graphicsDevice.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    baseVertex: 0,
                    startIndex: 0,
                    primitiveCount: _indices.Length / 3);
            }
        }

        private void UpdateBoundingBox()
        {
            Vector3 min = new Vector3(X * _size, Y * _size, Z * _size);
            Vector3 max = min + new Vector3(_size, _size, _size);
            _boundingBox = new BoundingBox(min, max);
        }

        public BoundingBox GetBoundingBox() => _boundingBox;

        public float GetDistanceTo(Vector3 point)
        {
            Vector3 chunkCenter = new Vector3(
                (X * _size) + _size / 2f,
                (Y * _size) + _size / 2f,
                (Z * _size) + _size / 2f);

            return Vector3.Distance(point, chunkCenter);
        }

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
