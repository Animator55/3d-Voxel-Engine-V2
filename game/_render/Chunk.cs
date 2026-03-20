using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    public class Chunk
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }
        private byte[,,] _blocks;
        private readonly int _size;

        private bool _isDirty;
        private bool _isMeshBuilding;

        // ── Opaque mesh ────────────────────────────────────────────────
        private VertexPositionNormalColor[] _vertices;
        private ushort[] _indices;
        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;

        // ── Water / transparent mesh (océano ≤ SeaLevel) ──────────────
        private VertexPositionNormalColor[] _waterVertices;
        private ushort[] _waterIndices;
        private VertexBuffer _waterVertexBuffer;
        private IndexBuffer _waterIndexBuffer;

        // ── River / opaque-water mesh (ríos y cascadas > SeaLevel) ────
        private VertexPositionNormalColor[] _riverVertices;
        private ushort[] _riverIndices;
        private VertexBuffer _riverVertexBuffer;
        private IndexBuffer _riverIndexBuffer;

        private BoundingBox _boundingBox;
        private ChunkDebugInfo _debugInfo;
        private readonly object _meshLock = new object();

        public Chunk(int x, int y, int z, int size = 16)
        {
            X = x; Y = y; Z = z;
            _size = size;
            _blocks = new byte[size, size, size];
            Array.Clear(_blocks, 0, _blocks.Length);
            _isDirty = true;
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
            if (_blocks[x, y, z] != blockType) { _blocks[x, y, z] = blockType; _isDirty = true; }
        }

        public byte[,,] GetBlocks() => _blocks;

        public void SetBlocks(byte[,,] blocks)
        {
            if (blocks.GetLength(0) != _size || blocks.GetLength(1) != _size || blocks.GetLength(2) != _size)
                throw new ArgumentException("Tamaño de bloque incorrecto");
            _blocks = (byte[,,])blocks.Clone();
            _isDirty = true;
        }

        public bool IsDirty        => _isDirty;
        public bool IsMeshBuilding => _isMeshBuilding;
        public bool HasMesh        => _vertexBuffer      != null && _indices      != null;
        public bool HasWaterMesh   => _waterVertexBuffer != null && _waterIndices != null;
        public bool HasRiverMesh   => _riverVertexBuffer != null && _riverIndices != null;
        public ChunkDebugInfo DebugInfo => _debugInfo;

        public void MarkDirty() => _isDirty = true;

        public void MarkMeshBuildStart()
        {
            lock (_meshLock) { _isMeshBuilding = true; _isDirty = false; }
        }

        public void MarkMeshEmpty() => _isMeshBuilding = false;

        // ── Upload opaque + water + river meshes atomically ────────────
        public void SetMeshData(
            VertexPositionNormalColor[] vertices,    ushort[] indices,
            VertexPositionNormalColor[] waterVerts,  ushort[] waterIdx,
            VertexPositionNormalColor[] riverVerts,  ushort[] riverIdx,
            GraphicsDevice graphicsDevice,
            ChunkDebugInfo debugInfo = null)
        {
            lock (_meshLock)
            {
                // ---- opaque ----
                _vertices = vertices;
                _indices  = indices;
                _debugInfo = debugInfo ?? new ChunkDebugInfo();
                _debugInfo.VertexCount = vertices?.Length ?? 0;
                _debugInfo.IndexCount  = indices?.Length  ?? 0;
                _debugInfo.LastMeshGenerationTime = DateTime.Now;

                _vertexBuffer?.Dispose();
                _indexBuffer?.Dispose();
                _vertexBuffer = null;
                _indexBuffer  = null;

                if (_vertices != null && _vertices.Length > 0 && _indices != null && _indices.Length > 0)
                {
                    _vertexBuffer = new VertexBuffer(graphicsDevice,
                        VertexPositionNormalColor.VertexDeclaration,
                        _vertices.Length, BufferUsage.WriteOnly);
                    _vertexBuffer.SetData(_vertices);
                    _indexBuffer = new IndexBuffer(graphicsDevice, typeof(ushort),
                        _indices.Length, BufferUsage.WriteOnly);
                    _indexBuffer.SetData(_indices);
                }

                // ---- water (transparente, océano) ----
                _waterVertices = waterVerts;
                _waterIndices  = waterIdx;

                _waterVertexBuffer?.Dispose();
                _waterIndexBuffer?.Dispose();
                _waterVertexBuffer = null;
                _waterIndexBuffer  = null;

                if (_waterVertices != null && _waterVertices.Length > 0 &&
                    _waterIndices  != null && _waterIndices.Length  > 0)
                {
                    _waterVertexBuffer = new VertexBuffer(graphicsDevice,
                        VertexPositionNormalColor.VertexDeclaration,
                        _waterVertices.Length, BufferUsage.WriteOnly);
                    _waterVertexBuffer.SetData(_waterVertices);
                    _waterIndexBuffer = new IndexBuffer(graphicsDevice, typeof(ushort),
                        _waterIndices.Length, BufferUsage.WriteOnly);
                    _waterIndexBuffer.SetData(_waterIndices);
                }

                // ---- river (opaco, ríos/cascadas sobre SeaLevel) ----
                _riverVertices = riverVerts;
                _riverIndices  = riverIdx;

                _riverVertexBuffer?.Dispose();
                _riverIndexBuffer?.Dispose();
                _riverVertexBuffer = null;
                _riverIndexBuffer  = null;

                if (_riverVertices != null && _riverVertices.Length > 0 &&
                    _riverIndices  != null && _riverIndices.Length  > 0)
                {
                    _riverVertexBuffer = new VertexBuffer(graphicsDevice,
                        VertexPositionNormalColor.VertexDeclaration,
                        _riverVertices.Length, BufferUsage.WriteOnly);
                    _riverVertexBuffer.SetData(_riverVertices);
                    _riverIndexBuffer = new IndexBuffer(graphicsDevice, typeof(ushort),
                        _riverIndices.Length, BufferUsage.WriteOnly);
                    _riverIndexBuffer.SetData(_riverIndices);
                }

                _isMeshBuilding = false;
            }
        }

        // ── Legacy overloads para compatibilidad ───────────────────────
        public void SetMeshData(
            VertexPositionNormalColor[] vertices,   ushort[] indices,
            VertexPositionNormalColor[] waterVerts, ushort[] waterIdx,
            GraphicsDevice graphicsDevice,
            ChunkDebugInfo debugInfo = null)
            => SetMeshData(vertices, indices, waterVerts, waterIdx, null, null, graphicsDevice, debugInfo);

        public void SetMeshData(
            VertexPositionNormalColor[] vertices, ushort[] indices,
            GraphicsDevice graphicsDevice,
            ChunkDebugInfo debugInfo = null)
            => SetMeshData(vertices, indices, null, null, null, null, graphicsDevice, debugInfo);

        // ── Draw opaque geometry ───────────────────────────────────────
        public void Draw(GraphicsDevice graphicsDevice, BoundingFrustum cameraFrustum)
        {
            lock (_meshLock)
            {
                if (_vertexBuffer == null || _indexBuffer == null) return;
                graphicsDevice.SetVertexBuffer(_vertexBuffer);
                graphicsDevice.Indices = _indexBuffer;
                graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    0, 0, _indices.Length / 3);
            }
        }

        // ── Draw water geometry (transparente) ────────────────────────
        public void DrawWater(GraphicsDevice graphicsDevice, BoundingFrustum cameraFrustum)
        {
            lock (_meshLock)
            {
                if (_waterVertexBuffer == null || _waterIndexBuffer == null) return;
                graphicsDevice.SetVertexBuffer(_waterVertexBuffer);
                graphicsDevice.Indices = _waterIndexBuffer;
                graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    0, 0, _waterIndices.Length / 3);
            }
        }

        // ── Draw river geometry (opaco) ───────────────────────────────
        public void DrawRiver(GraphicsDevice graphicsDevice, BoundingFrustum cameraFrustum)
        {
            lock (_meshLock)
            {
                if (_riverVertexBuffer == null || _riverIndexBuffer == null) return;
                graphicsDevice.SetVertexBuffer(_riverVertexBuffer);
                graphicsDevice.Indices = _riverIndexBuffer;
                graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    0, 0, _riverIndices.Length / 3);
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
            Vector3 c = new Vector3(
                X * _size + _size / 2f,
                Y * _size + _size / 2f,
                Z * _size + _size / 2f);
            return Vector3.Distance(point, c);
        }

        public void Dispose()
        {
            lock (_meshLock)
            {
                _vertexBuffer?.Dispose();      _indexBuffer?.Dispose();
                _waterVertexBuffer?.Dispose(); _waterIndexBuffer?.Dispose();
                _riverVertexBuffer?.Dispose(); _riverIndexBuffer?.Dispose();
                _vertexBuffer      = null;
                _waterVertexBuffer = null;
                _riverVertexBuffer = null;
            }
        }
    }
}