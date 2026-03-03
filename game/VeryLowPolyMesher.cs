using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Generador ULTRA minimalista para VeryLowPolyChunk.
    ///
    /// FIX GAPS: el heightmap tiene dimensión (chunkSize+2)x(chunkSize+2),
    /// con 1 bloque de margen en cada borde. Esto hace que los vértices del
    /// borde de un chunk coincidan exactamente con los del chunk vecino.
    ///
    /// FIX CAPAS PLANAS FLOTANTES: si el terreno de un vértice está por debajo
    /// del chunk Y actual, ese vértice se clampea a localY=0. Si TODOS los
    /// vértices del grid quedan en localY=0 (todo el terreno está bajo este
    /// chunk), la malla no se emite → evita el plano fantasma flotante.
    ///
    /// Vértices en espacio LOCAL del chunk (0..chunkSize).
    /// ChunkManager aplica effect.World = translación del chunk.
    ///
    /// Con chunkSize=16, SampleRate=4: grid 4x4 = 16 quads = 32 triángulos.
    /// </summary>
    public class VeryLowPolyMesher
    {
        private readonly VeryLowPolyChunk _chunk;
        private readonly int _chunkSize;
        private readonly int _chunkWorldY;

        private const int SampleRate = 4;

        public VeryLowPolyMesher(VeryLowPolyChunk chunk, int chunkSize = 16)
        {
            _chunk       = chunk;
            _chunkSize   = chunkSize;
            _chunkWorldY = chunk.Y * chunkSize;
        }

        public (VertexPositionNormalColor[], ushort[]) GenerateMesh()
        {
            int[,] heightMap = _chunk.GetHeightMap();
            if (heightMap == null) return (null, null);

            int hmDim = heightMap.GetLength(0);

            // Detectar si tiene margen (chunkSize+2) o no (chunkSize)
            bool hasMargin = hmDim >= _chunkSize + 2;

            int steps     = _chunkSize / SampleRate;
            int gridWidth = steps + 1;

            var localYGrid = new float[gridWidth, gridWidth];

            // FIX: rastrear si algún vértice tiene altura real > 0
            // para detectar el caso "todo el terreno está bajo este chunk Y"
            bool anyVertexAboveBase = false;

            for (int gz = 0; gz <= steps; gz++)
            for (int gx = 0; gx <= steps; gx++)
            {
                int localX = gx * SampleRate;
                int localZ = gz * SampleRate;

                int hmX, hmZ;
                if (hasMargin)
                {
                    hmX = Math.Clamp(localX + 1, 0, hmDim - 1);
                    hmZ = Math.Clamp(localZ + 1, 0, hmDim - 1);
                }
                else
                {
                    hmX = Math.Clamp(localX, 0, _chunkSize - 1);
                    hmZ = Math.Clamp(localZ, 0, _chunkSize - 1);
                }

                float worldHeight = heightMap[hmX, hmZ];

                // FIX: si el terreno está por debajo del chunk, localY = 0.
                // Pero si TODOS quedan en 0 → malla fantasma → la rechazamos abajo.
                float localY = Math.Max(0f, worldHeight - _chunkWorldY);
                localY = Math.Min(localY, _chunkSize);

                localYGrid[gx, gz] = localY;

                if (localY > 0f)
                    anyVertexAboveBase = true;
            }

            // FIX: si ningún vértice tiene altura real en este chunk Y,
            // no emitir malla → evita la capa plana flotante en Y=0 del chunk.
            if (!anyVertexAboveBase)
                return (null, null);

            var vertices = new List<VertexPositionNormalColor>(gridWidth * gridWidth);
            var indices  = new List<ushort>(steps * steps * 6);

            for (int gz = 0; gz <= steps; gz++)
            for (int gx = 0; gx <= steps; gx++)
            {
                int localX = gx * SampleRate;
                int localZ = gz * SampleRate;

                int hmX, hmZ;
                if (hasMargin)
                {
                    hmX = Math.Clamp(localX + 1, 0, hmDim - 1);
                    hmZ = Math.Clamp(localZ + 1, 0, hmDim - 1);
                }
                else
                {
                    hmX = Math.Clamp(localX, 0, _chunkSize - 1);
                    hmZ = Math.Clamp(localZ, 0, _chunkSize - 1);
                }

                float worldHeight = heightMap[hmX, hmZ];

                vertices.Add(new VertexPositionNormalColor(
                    new Vector3(localX, localYGrid[gx, gz], localZ),
                    Vector3.UnitY,
                    GetColorForHeight((int)worldHeight)));
            }

            for (int gz = 0; gz < steps; gz++)
            for (int gx = 0; gx < steps; gx++)
            {
                int tl = gz * gridWidth + gx;
                int tr = tl + 1;
                int bl = tl + gridWidth;
                int br = bl + 1;

                indices.Add((ushort)tl);
                indices.Add((ushort)bl);
                indices.Add((ushort)tr);

                indices.Add((ushort)tr);
                indices.Add((ushort)bl);
                indices.Add((ushort)br);
            }

            if (vertices.Count == 0 || indices.Count == 0) return (null, null);
            return (vertices.ToArray(), indices.ToArray());
        }

        private static Color GetColorForHeight(int height)
        {
            if (height <= 22)  return new Color(94,  141, 228);
            if (height <= 30)  return new Color(210, 192, 140);
            if (height <= 60)  return new Color(76,  102, 25);
            if (height <= 85)  return new Color(55,  80,  20);
            if (height <= 105) return new Color(107, 78,  35);
            return                     new Color(220, 220, 230);
        }
    }
}