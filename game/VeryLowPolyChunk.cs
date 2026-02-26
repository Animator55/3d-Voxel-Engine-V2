// using Microsoft.Xna.Framework;
// using Microsoft.Xna.Framework.Graphics;
// using System;

// namespace game
// {
//     /// <summary>
//     /// Chunk ULTRA simplificado para renderizado a MUCHA distancia.
//     /// Solo renderiza el heightmap (superficie del terreno).
//     /// Mucho más ligero que LowPolyChunk para chunks muy lejanos.
//     /// Compatible con el sistema actual de ChunkManager.
//     /// </summary>
//     public class VeryLowPolyChunk
//     {
//         public int X { get; private set; }
//         public int Y { get; private set; }
//         public int Z { get; private set; }

//         private int[,] _heightMap;  // Solo el mapa de alturas, sin datos 3D completos
//         private readonly int _size;

//         private bool _isDirty;
//         private bool _isMeshBuilding;

//         private VertexPositionNormalColor[] _vertices;
//         private ushort[] _indices;
//         private VertexBuffer _vertexBuffer;
//         private IndexBuffer _indexBuffer;

//         private BoundingBox _boundingBox;
//         private readonly object _meshLock = new object();

//         public bool IsDirty => _isDirty;
//         public bool IsMeshBuilding => _isMeshBuilding;
//         public bool HasMesh => _vertexBuffer != null && _indices != null;

//         public VeryLowPolyChunk(int x, int y, int z, int size = 16)
//         {
//             X = x;
//             Y = y;
//             Z = z;
//             _size = size;

//             _heightMap = new int[size, size];
//             Array.Clear(_heightMap, 0, _heightMap.Length);

//             _isDirty = true;
//             _isMeshBuilding = false;
//             _vertices = null;
//             _indices = null;
//             _vertexBuffer = null;
//             _indexBuffer = null;

//             UpdateBoundingBox();
//         }

//         public void SetHeightMap(int[,] heightMap)
//         {
//             if (heightMap.GetLength(0) != _size || heightMap.GetLength(1) != _size)
//             {
//                 throw new ArgumentException($"HeightMap debe ser {_size}x{_size}");
//             }

//             _heightMap = (int[,])heightMap.Clone();
//             _isDirty = true;
//         }

//         public int[,] GetHeightMap() => _heightMap;

//         public void MarkMeshBuildStart()
//         {
//             lock (_meshLock)
//             {
//                 _isMeshBuilding = true;
//                 _isDirty = false;
//             }
//         }

//         /// <summary>
//         /// Establece los datos de malla (vértices e índices) generados en thread worker.
//         /// Interfaz compatible con LowPolyChunk.
//         /// </summary>
//         public void SetMeshData(VertexPositionNormalColor[] vertices, ushort[] indices, GraphicsDevice graphicsDevice)
//         {
//             lock (_meshLock)
//             {
//                 _vertices = vertices;
//                 _indices = indices;

//                 _vertexBuffer?.Dispose();
//                 _indexBuffer?.Dispose();

//                 if (_vertices != null && _vertices.Length > 0 && _indices != null && _indices.Length > 0)
//                 {
//                     _vertexBuffer = new VertexBuffer(graphicsDevice, VertexPositionNormalColor.VertexDeclaration,
//                         _vertices.Length, BufferUsage.WriteOnly);
//                     _vertexBuffer.SetData(_vertices);

//                     _indexBuffer = new IndexBuffer(graphicsDevice, typeof(ushort),
//                         _indices.Length, BufferUsage.WriteOnly);
//                     _indexBuffer.SetData(_indices);
//                 }

//                 _isMeshBuilding = false;
//             }
//         }

//         public void Draw(GraphicsDevice graphicsDevice, BoundingFrustum cameraFrustum)
//         {
//             lock (_meshLock)
//             {
//                 if (_vertexBuffer == null || _indexBuffer == null)
//                     return;

//                 graphicsDevice.SetVertexBuffer(_vertexBuffer);
//                 graphicsDevice.Indices = _indexBuffer;

//                 graphicsDevice.DrawIndexedPrimitives(
//                     PrimitiveType.TriangleList,
//                     baseVertex: 0,
//                     startIndex: 0,
//                     primitiveCount: _indices.Length / 3);
//             }
//         }

//         private void UpdateBoundingBox()
//         {
//             Vector3 min = new Vector3(X * _size, Y * _size, Z * _size);
//             Vector3 max = min + new Vector3(_size, 256, _size);  // Y ajustado para terreno
//             _boundingBox = new BoundingBox(min, max);
//         }

//         public BoundingBox GetBoundingBox() => _boundingBox;

//         public float GetDistanceTo(Vector3 point)
//         {
//             Vector3 chunkCenter = new Vector3(
//                 (X * _size) + _size / 2f,
//                 (Y * _size) + _size / 2f,
//                 (Z * _size) + _size / 2f);

//             return Vector3.Distance(point, chunkCenter);
//         }

//         public void Dispose()
//         {
//             lock (_meshLock)
//             {
//                 _vertexBuffer?.Dispose();
//                 _indexBuffer?.Dispose();
//                 _vertexBuffer = null;
//                 _indexBuffer = null;
//                 _heightMap = null;
//             }
//         }
//     }
// }
