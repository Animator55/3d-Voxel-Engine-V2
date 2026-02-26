using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace game
{
    public class GreedyMesher : IAOProvider
    {
        private readonly Chunk _chunk;
        private readonly Chunk[,,] _neighborChunks;
        private readonly int _size;

        private List<VertexPositionNormalColor> _vertices;
        private List<ushort> _indices;

        // AO
        private readonly AmbientOcclusionCalculator _ao;
        private readonly float[] _aoBuffer = new float[4];

        public GreedyMesher(Chunk chunk, Chunk[,,] neighborChunks, int size = 16)
        {
            _chunk = chunk;
            _neighborChunks = neighborChunks;
            _size = size;
            _ao = new AmbientOcclusionCalculator(this, aoStrength: 0.4f);
        }

        // ── IAOProvider ──────────────────────────────────────────────────────
        public bool IsSolid(int x, int y, int z)
        {
            // Dentro del chunk propio
            if (x >= 0 && x < _size && y >= 0 && y < _size && z >= 0 && z < _size)
                return BlockType.IsSolid(_chunk.GetBlock(x, y, z));

            // Cruzar a chunk vecino
            var (neighbor, lx, ly, lz) = GetNeighborChunk(x, y, z);
            if (neighbor == null) return false;
            return BlockType.IsSolid(neighbor.GetBlock(lx, ly, lz));
        }

        private (Chunk chunk, int lx, int ly, int lz) GetNeighborChunk(int x, int y, int z)
        {
            int cx = x < 0 ? -1 : x >= _size ? 1 : 0;
            int cy = y < 0 ? -1 : y >= _size ? 1 : 0;
            int cz = z < 0 ? -1 : z >= _size ? 1 : 0;

            var neighbor = _neighborChunks[cx + 1, cy + 1, cz + 1];
            if (neighbor == null) return (null, 0, 0, 0);

            int lx = ((x % _size) + _size) % _size;
            int ly = ((y % _size) + _size) % _size;
            int lz = ((z % _size) + _size) % _size;

            return (neighbor, lx, ly, lz);
        }
        // ─────────────────────────────────────────────────────────────────────

        public (VertexPositionNormalColor[] vertices, ushort[] indices, ChunkDebugInfo debugInfo) GenerateMesh()
        {
            var stopwatch = Stopwatch.StartNew();
            var debugInfo = new ChunkDebugInfo();

            _vertices = new List<VertexPositionNormalColor>();
            _indices = new List<ushort>();

            var blocks = _chunk.GetBlocks();

            var meshStopwatch = Stopwatch.StartNew();

            ProcessFaceDirection(blocks, Axis.X,  1);
            ProcessFaceDirection(blocks, Axis.X, -1);
            ProcessFaceDirection(blocks, Axis.Y,  1);
            ProcessFaceDirection(blocks, Axis.Y, -1);
            ProcessFaceDirection(blocks, Axis.Z,  1);
            ProcessFaceDirection(blocks, Axis.Z, -1);

            meshStopwatch.Stop();
            debugInfo.GreedyMeshingTimeMs = meshStopwatch.ElapsedMilliseconds;

            stopwatch.Stop();
            debugInfo.MeshGenerationTimeMs = stopwatch.ElapsedMilliseconds;

            if (_vertices.Count == 0)
                return (null, null, debugInfo);

            return (_vertices.ToArray(), _indices.ToArray(), debugInfo);
        }

        private void ProcessFaceDirection(byte[,,] blocks, Axis axis, int direction)
        {
            bool[,] processed = new bool[_size, _size];

            for (int main = 0; main < _size; main++)
            {
                for (int i = 0; i < _size; i++)
                    for (int j = 0; j < _size; j++)
                        processed[i, j] = false;

                for (int a = 0; a < _size; a++)
                {
                    for (int b = 0; b < _size; b++)
                    {
                        if (processed[a, b])
                            continue;

                        byte blockType = GetBlockAtIndices(blocks, axis, main, a, b);

                        if (blockType == BlockType.Air)
                            continue;

                        var (x, y, z) = GetCoordinatesFromIndices(axis, main, a, b);

                        if (!IsFaceVisible(blocks, axis, direction, x, y, z))
                            continue;

                        int width  = GreedyExpandWidth (blocks, processed, axis, direction, main, a, b, blockType);
                        int height = GreedyExpandHeight(blocks, processed, axis, direction, main, a, b, width, blockType);

                        for (int wa = 0; wa < width; wa++)
                            for (int hb = 0; hb < height; hb++)
                                processed[a + wa, b + hb] = true;

                        AddRectangleFace(axis, direction, main, a, b, width, height, blockType, x, y, z);
                    }
                }
            }
        }

        private int GreedyExpandWidth(byte[,,] blocks, bool[,] processed, Axis axis, int direction,
            int main, int startA, int startB, byte blockType)
        {
            int width = 1;
            while (startA + width < _size)
            {
                if (processed[startA + width, startB]) break;

                byte block = GetBlockAtIndices(blocks, axis, main, startA + width, startB);
                if (block != blockType) break;

                var (x, y, z) = GetCoordinatesFromIndices(axis, main, startA + width, startB);
                if (!IsFaceVisible(blocks, axis, direction, x, y, z)) break;

                width++;
            }
            return width;
        }

        private int GreedyExpandHeight(byte[,,] blocks, bool[,] processed, Axis axis, int direction,
            int main, int startA, int startB, int width, byte blockType)
        {
            int height = 1;
            while (startB + height < _size)
            {
                bool canExpand = true;
                for (int a = startA; a < startA + width; a++)
                {
                    if (processed[a, startB + height]) { canExpand = false; break; }

                    byte block = GetBlockAtIndices(blocks, axis, main, a, startB + height);
                    if (block != blockType) { canExpand = false; break; }

                    var (x, y, z) = GetCoordinatesFromIndices(axis, main, a, startB + height);
                    if (!IsFaceVisible(blocks, axis, direction, x, y, z)) { canExpand = false; break; }
                }

                if (!canExpand) break;
                height++;
            }
            return height;
        }

        private bool IsFaceVisible(byte[,,] blocks, Axis faceAxis, int direction, int x, int y, int z)
        {
            int nx = x + (faceAxis == Axis.X ? direction : 0);
            int ny = y + (faceAxis == Axis.Y ? direction : 0);
            int nz = z + (faceAxis == Axis.Z ? direction : 0);

            if (nx < 0 || nx >= _size || ny < 0 || ny >= _size || nz < 0 || nz >= _size)
                return true;

            return blocks[nx, ny, nz] == BlockType.Air;
        }

        private byte GetBlockAtIndices(byte[,,] blocks, Axis axis, int main, int a, int b)
        {
            return axis switch
            {
                Axis.X => blocks[main, a, b],
                Axis.Y => blocks[a, main, b],
                Axis.Z => blocks[a, b, main],
                _ => BlockType.Air
            };
        }

        private (int x, int y, int z) GetCoordinatesFromIndices(Axis axis, int main, int a, int b)
        {
            return axis switch
            {
                Axis.X => (main, a, b),
                Axis.Y => (a, main, b),
                Axis.Z => (a, b, main),
                _ => (0, 0, 0)
            };
        }

        private void AddRectangleFace(Axis axis, int direction, int main, int a, int b,
            int width, int height, byte blockType, int x, int y, int z)
        {
            int baseVertex = _vertices.Count;
            if (baseVertex + 4 > 65535)
                return;

            int faceOffset = direction > 0 ? main + 1 : main;

            Vector3[] corners = new Vector3[4];
            Vector3 normal;

            // Normal en coordenadas enteras para el calculador de AO
            int nx = axis == Axis.X ? direction : 0;
            int ny = axis == Axis.Y ? direction : 0;
            int nz = axis == Axis.Z ? direction : 0;

            switch (axis)
            {
                case Axis.X:
                    normal = new Vector3(direction, 0, 0);
                    if (direction > 0)
                    {
                        corners[0] = new Vector3(faceOffset, a,         b);
                        corners[1] = new Vector3(faceOffset, a + width,  b);
                        corners[2] = new Vector3(faceOffset, a + width,  b + height);
                        corners[3] = new Vector3(faceOffset, a,          b + height);
                    }
                    else
                    {
                        corners[0] = new Vector3(faceOffset, a,          b);
                        corners[1] = new Vector3(faceOffset, a,          b + height);
                        corners[2] = new Vector3(faceOffset, a + width,  b + height);
                        corners[3] = new Vector3(faceOffset, a + width,  b);
                    }
                    break;

                case Axis.Y:
                    normal = new Vector3(0, direction, 0);
                    if (direction > 0)
                    {
                        corners[0] = new Vector3(a,          faceOffset, b);
                        corners[1] = new Vector3(a,          faceOffset, b + height);
                        corners[2] = new Vector3(a + width,  faceOffset, b + height);
                        corners[3] = new Vector3(a + width,  faceOffset, b);
                    }
                    else
                    {
                        corners[0] = new Vector3(a,          faceOffset, b);
                        corners[1] = new Vector3(a + width,  faceOffset, b);
                        corners[2] = new Vector3(a + width,  faceOffset, b + height);
                        corners[3] = new Vector3(a,          faceOffset, b + height);
                    }
                    break;

                case Axis.Z:
                    normal = new Vector3(0, 0, direction);
                    if (direction > 0)
                    {
                        corners[0] = new Vector3(a,          b,          faceOffset);
                        corners[1] = new Vector3(a + width,  b,          faceOffset);
                        corners[2] = new Vector3(a + width,  b + height, faceOffset);
                        corners[3] = new Vector3(a,          b + height, faceOffset);
                    }
                    else
                    {
                        corners[0] = new Vector3(a,          b,          faceOffset);
                        corners[1] = new Vector3(a,          b + height, faceOffset);
                        corners[2] = new Vector3(a + width,  b + height, faceOffset);
                        corners[3] = new Vector3(a + width,  b,          faceOffset);
                    }
                    break;

                default:
                    return;
            }

            // Calcular AO para los 4 vértices de este quad
            // NOTA: el AO usa las coordenadas del bloque origen (x, y, z), no del quad greedy.
            // Esto es una aproximación válida — el AO del bloque de origen se aplica a todo el quad.
            // Para AO per-vértice perfecto en quads greedy se necesitaría calcular por cada celda.
            _ao.CalculateForFace(x, y, z, nx, ny, nz, _aoBuffer);

            Color baseColor = BlockType.GetBlockColor(blockType);

            for (int i = 0; i < 4; i++)
            {
                Color c = MultiplyColor(baseColor, _aoBuffer[i]);
                _vertices.Add(new VertexPositionNormalColor(corners[i], normal, c));
            }

            // Winding CCW: flip diagonal si AO lo requiere para evitar artefactos
            if (_aoBuffer[0] + _aoBuffer[2] < _aoBuffer[1] + _aoBuffer[3])
            {
                // Diagonal alternativa
                _indices.Add((ushort)(baseVertex + 1));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 3));
                _indices.Add((ushort)(baseVertex + 1));
                _indices.Add((ushort)(baseVertex + 3));
                _indices.Add((ushort)(baseVertex + 0));
            }
            else
            {
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 1));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 3));
            }
        }

        private static Color MultiplyColor(Color c, float factor)
        {
            return new Color(
                (int)(c.R * factor),
                (int)(c.G * factor),
                (int)(c.B * factor),
                c.A);
        }

        private enum Axis { X, Y, Z }
    }
}