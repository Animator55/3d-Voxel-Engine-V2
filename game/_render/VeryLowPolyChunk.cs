using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
namespace game
{
    public class VeryLowPolyChunk
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }
        private int[,] _heightMap;
        private readonly int _size;
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
        public bool HasMesh => _vertexBuffer != null && _indexBuffer != null && _indices != null;
        public VeryLowPolyChunk(int x, int y, int z, int size = 16)
        {
            X = x; Y = y; Z = z;
            _size = size;
            _heightMap = new int[size, size];
            Array.Clear(_heightMap, 0, _heightMap.Length);
            _isDirty = true;
            _isMeshBuilding = false;
            _vertices = null;
            _indices = null;
            _vertexBuffer = null;
            _indexBuffer = null;
            UpdateBoundingBox();
        }
        public void SetHeightMap(int[,] heightMap)
        {
            int dim0 = heightMap.GetLength(0);
            int dim1 = heightMap.GetLength(1);
            if ((dim0 != _size && dim0 != _size + 2) ||
                (dim1 != _size && dim1 != _size + 2))
                throw new ArgumentException(
                    $"HeightMap debe ser {_size}x{_size} o {_size + 2}x{_size + 2}, recibido {dim0}x{dim1}");
            _heightMap = (int[,])heightMap.Clone();
            _isDirty = true;
        }
        public int[,] GetHeightMap() => _heightMap;
        public void MarkMeshBuildStart()
        {
            lock (_meshLock)
            {
                _isMeshBuilding = true;
                _isDirty = false;
            }
        }
        public void MarkMeshEmpty()
        {
            lock (_meshLock)
            {
                _isMeshBuilding = false;


            }
        }
        public void MarkMeshFailed()
        {
            lock (_meshLock)
            {
                _isMeshBuilding = false;
                _isDirty = true;
            }
        }
        public void SetMeshData(VertexPositionNormalColor[] vertices, ushort[] indices,
                                GraphicsDevice graphicsDevice)
        {
            lock (_meshLock)
            {
                _vertices = vertices;
                _indices = indices;
                _vertexBuffer?.Dispose();
                _indexBuffer?.Dispose();
                _vertexBuffer = null;
                _indexBuffer = null;
                if (_vertices != null && _vertices.Length > 0
                    && _indices != null && _indices.Length > 0)
                {
                    _vertexBuffer = new VertexBuffer(
                        graphicsDevice, VertexPositionNormalColor.VertexDeclaration,
                        _vertices.Length, BufferUsage.WriteOnly);
                    _vertexBuffer.SetData(_vertices);
                    _indexBuffer = new IndexBuffer(
                        graphicsDevice, typeof(ushort),
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
                if (_vertexBuffer == null || _indexBuffer == null || _indices == null)
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
            Vector3 min = new Vector3(X * _size, 0, Z * _size);
            Vector3 max = new Vector3(X * _size + _size, 256, Z * _size + _size);
            _boundingBox = new BoundingBox(min, max);
        }
        public BoundingBox GetBoundingBox() => _boundingBox;
        public float GetDistanceTo(Vector3 point)
        {
            Vector3 chunkCenter = new Vector3(
                X * _size + _size / 2f,
                Y * _size + _size / 2f,
                Z * _size + _size / 2f);
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
                _heightMap = null;
            }
        }
    }
}