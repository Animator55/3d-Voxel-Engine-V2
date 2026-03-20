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
        private List<VertexPositionNormalColor> _waterVertices;
        private List<ushort> _waterIndices;
        private List<VertexPositionNormalColor> _riverVertices;   // ← nuevo
        private List<ushort> _riverIndices;     // ← nuevo

        private readonly AmbientOcclusionCalculator _ao;
        private readonly float[] _aoBuffer = new float[4];
        public static float AoStrength = 0.4f;

        private const int SeaLevel = 20; // debe coincidir con WorldGenerator.SeaLevel
        private readonly bool _fancyWater;

        public GreedyMesher(Chunk chunk, Chunk[,,] neighborChunks, int size = 16, bool fancyWater = true)
        {
            _chunk = chunk;
            _neighborChunks = neighborChunks;
            _size = size;
            _fancyWater = fancyWater;
            _ao = new AmbientOcclusionCalculator(this, aoStrength: AoStrength);
        }
        // ─── IAOProvider ─────────────────────────────────────────────
        public bool IsSolid(int x, int y, int z)
        {
            if (x >= 0 && x < _size && y >= 0 && y < _size && z >= 0 && z < _size)
                return BlockType.IsSolid(_chunk.GetBlock(x, y, z));
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

        // ─── Main entry ───────────────────────────────────────────────
        public (VertexPositionNormalColor[] vertices, ushort[] indices,
        VertexPositionNormalColor[] waterVerts, ushort[] waterIdx,
        VertexPositionNormalColor[] riverVerts, ushort[] riverIdx,
        ChunkDebugInfo debugInfo) GenerateMesh()
        {
            var sw = Stopwatch.StartNew();
            var debugInfo = new ChunkDebugInfo();

            _vertices = new List<VertexPositionNormalColor>();
            _indices = new List<ushort>();
            _waterVertices = new List<VertexPositionNormalColor>();
            _waterIndices = new List<ushort>();
            _riverVertices = new List<VertexPositionNormalColor>();
            _riverIndices = new List<ushort>();

            var blocks = _chunk.GetBlocks();

            var meshSw = Stopwatch.StartNew();

            // ── Opaque pass ──────────────────────────────────────────
            ProcessFaceDirection(blocks, Axis.X, 1, water: false);
            ProcessFaceDirection(blocks, Axis.X, -1, water: false);
            ProcessFaceDirection(blocks, Axis.Y, 1, water: false);
            ProcessFaceDirection(blocks, Axis.Y, -1, water: false);
            ProcessFaceDirection(blocks, Axis.Z, 1, water: false);
            ProcessFaceDirection(blocks, Axis.Z, -1, water: false);

            // ── Water pass ───────────────────────────────────────────
            ProcessFaceDirection(blocks, Axis.X, 1, water: true);
            ProcessFaceDirection(blocks, Axis.X, -1, water: true);
            ProcessFaceDirection(blocks, Axis.Y, 1, water: true);
            ProcessFaceDirection(blocks, Axis.Y, -1, water: true);
            ProcessFaceDirection(blocks, Axis.Z, 1, water: true);
            ProcessFaceDirection(blocks, Axis.Z, -1, water: true);

            meshSw.Stop();
            sw.Stop();

            debugInfo.GreedyMeshingTimeMs = meshSw.ElapsedMilliseconds;
            debugInfo.MeshGenerationTimeMs = sw.ElapsedMilliseconds;

            return (
        _vertices.Count > 0 ? _vertices.ToArray() : null,
        _indices.Count > 0 ? _indices.ToArray() : null,
        _waterVertices.Count > 0 ? _waterVertices.ToArray() : null,
        _waterIndices.Count > 0 ? _waterIndices.ToArray() : null,
        _riverVertices.Count > 0 ? _riverVertices.ToArray() : null,
        _riverIndices.Count > 0 ? _riverIndices.ToArray() : null,
        debugInfo);
        }

        // ─── Per-direction face processing ───────────────────────────
        private void ProcessFaceDirection(byte[,,] blocks, Axis axis, int direction, bool water)
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
                        if (processed[a, b]) continue;

                        byte blockType = GetBlockAtIndices(blocks, axis, main, a, b);

                        if (water) { if (!BlockType.IsWater(blockType)) continue; }
                        else { if (!BlockType.IsSolid(blockType)) continue; }

                        var (x, y, z) = GetCoordinatesFromIndices(axis, main, a, b);

                        if (!IsFaceVisible(blocks, axis, direction, x, y, z, water))
                            continue;

                        int width = GreedyExpandWidth(blocks, processed, axis, direction, main, a, b, blockType, water);
                        int height = GreedyExpandHeight(blocks, processed, axis, direction, main, a, b, width, blockType, water);

                        for (int wa = 0; wa < width; wa++)
                            for (int hb = 0; hb < height; hb++)
                                processed[a + wa, b + hb] = true;

                        AddRectangleFace(axis, direction, main, a, b, width, height, blockType, x, y, z, water);
                    }
                }
            }
        }

        // ─── Greedy expand ────────────────────────────────────────────
        private int GreedyExpandWidth(byte[,,] blocks, bool[,] processed, Axis axis, int direction,
            int main, int startA, int startB, byte blockType, bool water)
        {
            int width = 1;
            while (startA + width < _size)
            {
                if (processed[startA + width, startB]) break;
                byte b = GetBlockAtIndices(blocks, axis, main, startA + width, startB);
                if (b != blockType) break;
                var (x, y, z) = GetCoordinatesFromIndices(axis, main, startA + width, startB);
                if (!IsFaceVisible(blocks, axis, direction, x, y, z, water)) break;
                width++;
            }
            return width;
        }

        private int GreedyExpandHeight(byte[,,] blocks, bool[,] processed, Axis axis, int direction,
            int main, int startA, int startB, int width, byte blockType, bool water)
        {
            int height = 1;
            while (startB + height < _size)
            {
                bool canExpand = true;
                for (int a = startA; a < startA + width; a++)
                {
                    if (processed[a, startB + height]) { canExpand = false; break; }
                    byte bl = GetBlockAtIndices(blocks, axis, main, a, startB + height);
                    if (bl != blockType) { canExpand = false; break; }
                    var (x, y, z) = GetCoordinatesFromIndices(axis, main, a, startB + height);
                    if (!IsFaceVisible(blocks, axis, direction, x, y, z, water)) { canExpand = false; break; }
                }
                if (!canExpand) break;
                height++;
            }
            return height;
        }

        // ─── Face visibility ──────────────────────────────────────────
        private bool IsFaceVisible(byte[,,] blocks, Axis faceAxis, int direction,
            int x, int y, int z, bool water)
        {
            int worldY = _chunk.Y * _size + y;

            // ── Agua por encima del nivel del mar ─────────────────────
            // Comportamiento IDÉNTICO a la primera versión entregada:
            // todas las caras, visible cuando el vecino es Air.
            // Borde de chunk tratado como Air (cara exterior visible).
            if (water && worldY > SeaLevel)
            {
                int nx = x + (faceAxis == Axis.X ? direction : 0);
                int ny = y + (faceAxis == Axis.Y ? direction : 0);
                int nz = z + (faceAxis == Axis.Z ? direction : 0);

                byte neighbor;
                if (nx < 0 || nx >= _size || ny < 0 || ny >= _size || nz < 0 || nz >= _size)
                    neighbor = BlockType.Air;
                else
                    neighbor = blocks[nx, ny, nz];

                return neighbor == BlockType.Air;
            }

            // ── Agua al nivel del mar o por debajo ────────────────────
            // Solo cara superior (Y+1). Elimina costuras de chunk en océanos.
            if (water && worldY <= SeaLevel)
            {
                if (faceAxis != Axis.Y || direction != 1)
                    return false;

                // Para la cara superior, mismo test: vecino tiene que ser Air.
                int ny = y + 1;
                if (ny >= _size) return true; // borde superior del chunk = Air
                return blocks[x, ny, z] == BlockType.Air;
            }

            // ── Bloques sólidos (sin cambios) ─────────────────────────
            int snx = x + (faceAxis == Axis.X ? direction : 0);
            int sny = y + (faceAxis == Axis.Y ? direction : 0);
            int snz = z + (faceAxis == Axis.Z ? direction : 0);

            byte sneighbor;
            if (snx < 0 || snx >= _size || sny < 0 || sny >= _size || snz < 0 || snz >= _size)
                sneighbor = BlockType.Air;
            else
                sneighbor = blocks[snx, sny, snz];

            return BlockType.IsTransparent(sneighbor);
        }

        // ─── Index helpers ────────────────────────────────────────────
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

        // ─── Quad emission ────────────────────────────────────────────
        private void AddRectangleFace(Axis axis, int direction, int main, int a, int b,
            int width, int height, byte blockType, int x, int y, int z, bool water)
        {
            int worldY = _chunk.Y * _size + y;
            bool isRiver = water && (worldY <= SeaLevel);

            var vList = (water && _fancyWater) ? (isRiver ? _riverVertices : _waterVertices) : _vertices;
            var iList = (water && _fancyWater) ? (isRiver ? _riverIndices : _waterIndices) : _indices;
            int baseVertex = vList.Count;
            if (baseVertex + 4 > 65535) return;

            int faceOffset = direction > 0 ? main + 1 : main;
            Vector3[] corners = new Vector3[4];
            Vector3 normal;

            int nx = axis == Axis.X ? direction : 0;
            int ny = axis == Axis.Y ? direction : 0;
            int nz = axis == Axis.Z ? direction : 0;

            int bx0, by0, bz0, bx1, by1, bz1, bx2, by2, bz2, bx3, by3, bz3;

            switch (axis)
            {
                case Axis.X:
                    normal = new Vector3(direction, 0, 0);
                    bx0 = x; by0 = a; bz0 = b;
                    bx1 = x; by1 = a + width - 1; bz1 = b;
                    bx2 = x; by2 = a + width - 1; bz2 = b + height - 1;
                    bx3 = x; by3 = a; bz3 = b + height - 1;
                    if (direction > 0)
                    {
                        corners[0] = new Vector3(faceOffset, a, b);
                        corners[1] = new Vector3(faceOffset, a + width, b);
                        corners[2] = new Vector3(faceOffset, a + width, b + height);
                        corners[3] = new Vector3(faceOffset, a, b + height);
                    }
                    else
                    {
                        corners[0] = new Vector3(faceOffset, a, b);
                        corners[1] = new Vector3(faceOffset, a, b + height);
                        corners[2] = new Vector3(faceOffset, a + width, b + height);
                        corners[3] = new Vector3(faceOffset, a + width, b);
                    }
                    break;
                case Axis.Y:
                    normal = new Vector3(0, direction, 0);
                    bx0 = a; by0 = y; bz0 = b;
                    bx1 = a; by1 = y; bz1 = b + height - 1;
                    bx2 = a + width - 1; by2 = y; bz2 = b + height - 1;
                    bx3 = a + width - 1; by3 = y; bz3 = b;
                    if (direction > 0)
                    {
                        corners[0] = new Vector3(a, faceOffset, b);
                        corners[1] = new Vector3(a, faceOffset, b + height);
                        corners[2] = new Vector3(a + width, faceOffset, b + height);
                        corners[3] = new Vector3(a + width, faceOffset, b);
                    }
                    else
                    {
                        corners[0] = new Vector3(a, faceOffset, b);
                        corners[1] = new Vector3(a + width, faceOffset, b);
                        corners[2] = new Vector3(a + width, faceOffset, b + height);
                        corners[3] = new Vector3(a, faceOffset, b + height);
                    }
                    break;
                case Axis.Z:
                    normal = new Vector3(0, 0, direction);
                    bx0 = a; by0 = b; bz0 = z;
                    bx1 = a + width - 1; by1 = b; bz1 = z;
                    bx2 = a + width - 1; by2 = b + height - 1; bz2 = z;
                    bx3 = a; by3 = b + height - 1; bz3 = z;
                    if (direction > 0)
                    {
                        corners[0] = new Vector3(a, b, faceOffset);
                        corners[1] = new Vector3(a + width, b, faceOffset);
                        corners[2] = new Vector3(a + width, b + height, faceOffset);
                        corners[3] = new Vector3(a, b + height, faceOffset);
                    }
                    else
                    {
                        corners[0] = new Vector3(a, b, faceOffset);
                        corners[1] = new Vector3(a, b + height, faceOffset);
                        corners[2] = new Vector3(a + width, b + height, faceOffset);
                        corners[3] = new Vector3(a + width, b, faceOffset);
                    }
                    break;
                default:
                    return;
            }

            _ao.CalculateForFace(
                bx0, by0, bz0,
                bx1, by1, bz1,
                bx2, by2, bz2,
                bx3, by3, bz3,
                nx, ny, nz, _aoBuffer);

            Color baseColor = BlockType.GetBlockColor(blockType);

            float aoScale = water ? 0f : 1.0f;
            for (int i = 0; i < 4; i++)
            {
                float aoFactor = water ? (1f - aoScale * (1f - _aoBuffer[i])) : _aoBuffer[i];
                Color c = MultiplyColor(baseColor, aoFactor);
                vList.Add(new VertexPositionNormalColor(corners[i], normal, c));
            }

            if (_aoBuffer[0] + _aoBuffer[2] < _aoBuffer[1] + _aoBuffer[3])
            {
                iList.Add((ushort)(baseVertex + 1));
                iList.Add((ushort)(baseVertex + 2));
                iList.Add((ushort)(baseVertex + 3));
                iList.Add((ushort)(baseVertex + 1));
                iList.Add((ushort)(baseVertex + 3));
                iList.Add((ushort)(baseVertex + 0));
            }
            else
            {
                iList.Add((ushort)(baseVertex + 0));
                iList.Add((ushort)(baseVertex + 1));
                iList.Add((ushort)(baseVertex + 2));
                iList.Add((ushort)(baseVertex + 0));
                iList.Add((ushort)(baseVertex + 2));
                iList.Add((ushort)(baseVertex + 3));
            }
        }

        private static Color MultiplyColor(Color c, float factor)
        {
            return new Color((int)(c.R * factor), (int)(c.G * factor),
                             (int)(c.B * factor), c.A);
        }

        private enum Axis { X, Y, Z }
    }
}