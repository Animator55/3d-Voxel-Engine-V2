// using Microsoft.Xna.Framework;
// using System;
// using System.Collections.Generic;

// namespace game
// {
//     /// <summary>
//     /// Generador ULTRA minimalista para VeryLowPolyChunk.
//     /// Grid MUY REDUCIDO - baja prioridad, mínimo impacto en FPS.
//     /// </summary>
//     public class VeryLowPolyMesher
//     {
//         private readonly VeryLowPolyChunk _chunk;
//         private readonly int _chunkSize;

//         public VeryLowPolyMesher(VeryLowPolyChunk chunk, int chunkSize = 16)
//         {
//             _chunk = chunk;
//             _chunkSize = chunkSize;
//         }

//         public (VertexPositionNormalColor[], ushort[]) GenerateMesh()
//         {
//             int[,] heightMap = _chunk.GetHeightMap();
//             if (heightMap == null)
//                 return (null, null);

//             var vertices = new List<VertexPositionNormalColor>();
//             var indices = new List<ushort>();

//             int worldX = _chunk.X * _chunkSize;
//             int worldZ = _chunk.Z * _chunkSize;

//             // SUPER REDUCIDO: Grid de 4x4 en lugar de 16x16
//             // Esto da solo 25 vértices por chunk VeryLP
//             int sampleRate = 4;

//             // Crear vértices
//             for (int z = 0; z <= _chunkSize; z += sampleRate)
//             {
//                 for (int x = 0; x <= _chunkSize; x += sampleRate)
//                 {
//                     int hmX = Math.Min(x, _chunkSize - 1);
//                     int hmZ = Math.Min(z, _chunkSize - 1);
//                     int height = heightMap[hmX, hmZ];

//                     Vector3 position = new Vector3(worldX + x, height, worldZ + z);
//                     Color color = GetColorForHeight(height);
//                     Vector3 normal = Vector3.Up;  // Normal simplificado

//                     vertices.Add(new VertexPositionNormalColor(position, normal, color));
//                 }
//             }

//             // Crear triángulos
//             int gridWidth = (_chunkSize / sampleRate) + 1;
//             for (int z = 0; z < _chunkSize; z += sampleRate)
//             {
//                 for (int x = 0; x < _chunkSize; x += sampleRate)
//                 {
//                     int gridZ = z / sampleRate;
//                     int gridX = x / sampleRate;

//                     int topLeft = gridZ * gridWidth + gridX;
//                     int topRight = topLeft + 1;
//                     int bottomLeft = topLeft + gridWidth;
//                     int bottomRight = bottomLeft + 1;

//                     if (topRight >= vertices.Count || bottomLeft >= vertices.Count || bottomRight >= vertices.Count)
//                         continue;

//                     indices.Add((ushort)topLeft);
//                     indices.Add((ushort)bottomLeft);
//                     indices.Add((ushort)topRight);

//                     indices.Add((ushort)topRight);
//                     indices.Add((ushort)bottomLeft);
//                     indices.Add((ushort)bottomRight);
//                 }
//             }

//             if (vertices.Count == 0 || indices.Count == 0)
//                 return (null, null);

//             return (vertices.ToArray(), indices.ToArray());
//         }

//         private Color GetColorForHeight(int height)
//         {
//             if (height < 20)
//                 return new Color(94, 141, 228);
//             else if (height < 60)
//                 return new Color(76, 102, 25);
//             else if (height < 100)
//                 return new Color(107, 78, 35);
//             else
//                 return new Color(150, 150, 150);
//         }
//     }
// }
