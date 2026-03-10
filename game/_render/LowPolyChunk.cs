using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
namespace game
{
    public class LowPolyChunk
    {
        public const int LOD_LEVELS = 3;
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }
        private byte[,,] _blocks;
        private readonly int _size;

        private readonly bool[] _levelDirty = new bool[LOD_LEVELS];
        private readonly bool[] _levelMeshBuilding = new bool[LOD_LEVELS];


        private readonly bool[] _levelFailed = new bool[LOD_LEVELS];

        private readonly VertexPositionNormalColor[][] _levelVertices = new VertexPositionNormalColor[LOD_LEVELS][];
        private readonly ushort[][] _levelIndices = new ushort[LOD_LEVELS][];
        private readonly VertexBuffer[] _vertexBuffers = new VertexBuffer[LOD_LEVELS];
        private readonly IndexBuffer[] _indexBuffers = new IndexBuffer[LOD_LEVELS];

        private int _activeLevel = 0;
        private BoundingBox _boundingBox;
        private readonly object _meshLock = new object();

        public bool IsDirty => _levelDirty[_activeLevel];
        public bool IsMeshBuilding => _levelMeshBuilding[_activeLevel];



        public bool HasMesh => _vertexBuffers[_activeLevel] != null
                                     && _levelIndices[_activeLevel] != null;
        public int ActiveLevel
        {
            get => _activeLevel;
            set => _activeLevel = Math.Clamp(value, 0, LOD_LEVELS - 1);
        }



        public int CachedLevelCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < LOD_LEVELS; i++)
                    if (_vertexBuffers[i] != null) count++;
                return count;
            }
        }
        public LowPolyChunk(int x, int y, int z, int size = 16)
        {
            X = x;
            Y = y;
            Z = z;
            _size = size;
            _blocks = new byte[size, size, size];
            Array.Clear(_blocks, 0, _blocks.Length);
            for (int i = 0; i < LOD_LEVELS; i++)
            {
                _levelDirty[i] = true;
                _levelMeshBuilding[i] = false;
                _levelFailed[i] = false;
            }
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


                for (int i = 0; i < LOD_LEVELS; i++)
                {
                    _levelDirty[i] = true;
                    _levelFailed[i] = false;
                }
            }
        }
        public byte[,,] GetBlocks() => _blocks;





        public void SetBlocksForLevel(byte[,,] blocks, int level)
        {
            if (blocks.GetLength(0) != _size ||
                blocks.GetLength(1) != _size ||
                blocks.GetLength(2) != _size)
                throw new ArgumentException("Tamaño de bloque incorrecto");


            if (level == 0)
                _blocks = (byte[,,])blocks.Clone();
            _levelDirty[level] = true;
            _levelFailed[level] = false;
        }
        public void MarkDirty()
        {
            for (int i = 0; i < LOD_LEVELS; i++)
            {
                _levelDirty[i] = true;
                _levelFailed[i] = false;
            }
        }
        public void MarkDirtyLevel(int level)
        {
            _levelDirty[level] = true;
            _levelFailed[level] = false;
        }
        public bool NeedsMesh(int level) => _vertexBuffers[level] == null
                                         && !_levelMeshBuilding[level]
                                         && !_levelFailed[level];
        public bool IsMeshBuildingForLevel(int level) => _levelMeshBuilding[level];
        public bool HasMeshForLevel(int level) => _vertexBuffers[level] != null
                                                      && _levelIndices[level] != null;
        public void MarkMeshBuildStart(int level)
        {
            lock (_meshLock)
            {
                _levelMeshBuilding[level] = true;
                _levelDirty[level] = false;
            }
        }

        // public void MarkMeshBuildStart() => MarkMeshBuildStart(_activeLevel);

        public void SetMeshData(VertexPositionNormalColor[] vertices, ushort[] indices,
                                GraphicsDevice graphicsDevice, int level)
        {
            lock (_meshLock)
            {
                _levelVertices[level] = vertices;
                _levelIndices[level] = indices;
                _vertexBuffers[level]?.Dispose();
                _indexBuffers[level]?.Dispose();
                _vertexBuffers[level] = null;
                _indexBuffers[level] = null;
                if (vertices != null && vertices.Length > 0 &&
                    indices != null && indices.Length > 0)
                {
                    _vertexBuffers[level] = new VertexBuffer(
                        graphicsDevice,
                        VertexPositionNormalColor.VertexDeclaration,
                        vertices.Length,
                        BufferUsage.WriteOnly);
                    _vertexBuffers[level].SetData(vertices);
                    _indexBuffers[level] = new IndexBuffer(
                        graphicsDevice,
                        typeof(ushort),
                        indices.Length,
                        BufferUsage.WriteOnly);
                    _indexBuffers[level].SetData(indices);
                }
                else
                {
                    _levelFailed[level] = true;
                }
                _levelMeshBuilding[level] = false;
            }
        }

        public void SetMeshData(VertexPositionNormalColor[] vertices, ushort[] indices,
                                GraphicsDevice graphicsDevice)
            => SetMeshData(vertices, indices, graphicsDevice, _activeLevel);

        public void Draw(GraphicsDevice graphicsDevice, BoundingFrustum cameraFrustum)
        {
            lock (_meshLock)
            {
                var vb = _vertexBuffers[_activeLevel];
                var ib = _indexBuffers[_activeLevel];
                var idx = _levelIndices[_activeLevel];
                if (vb == null || ib == null || idx == null)
                    return;
                graphicsDevice.SetVertexBuffer(vb);
                graphicsDevice.Indices = ib;
                graphicsDevice.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    baseVertex: 0,
                    startIndex: 0,
                    primitiveCount: idx.Length / 3);
            }
        }

        private void UpdateBoundingBox()
        {
            Vector3 min = new Vector3(X * _size, Y * _size, Z * _size);
            _boundingBox = new BoundingBox(min, min + new Vector3(_size));
        }
        public BoundingBox GetBoundingBox() => _boundingBox;
        public float GetDistanceTo(Vector3 point)
        {
            Vector3 center = new Vector3(
                X * _size + _size / 2f,
                Y * _size + _size / 2f,
                Z * _size + _size / 2f);
            return Vector3.Distance(point, center);
        }
        public void Dispose()
        {
            lock (_meshLock)
            {
                for (int i = 0; i < LOD_LEVELS; i++)
                {
                    _vertexBuffers[i]?.Dispose();
                    _indexBuffers[i]?.Dispose();
                    _vertexBuffers[i] = null;
                    _indexBuffers[i] = null;
                }
            }
        }
    }
}