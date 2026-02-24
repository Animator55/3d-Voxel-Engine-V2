using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace game
{
    public class GreedyMesher
    {
        private readonly Chunk _chunk;
        private readonly Chunk[,,] _neighborChunks;
        private readonly int _size;

        private List<VertexPositionNormalColor> _vertices;
        private List<ushort> _indices;

        public GreedyMesher(Chunk chunk, Chunk[,,] neighborChunks, int size = 16)
        {
            _chunk = chunk;
            _neighborChunks = neighborChunks;
            _size = size;
        }

        public (VertexPositionNormalColor[] vertices, ushort[] indices) GenerateMesh()
        {
            _vertices = new List<VertexPositionNormalColor>();
            _indices = new List<ushort>();

            var blocks = _chunk.GetBlocks();

            ProcessFaceDirection(blocks, Axis.X, 1);
            ProcessFaceDirection(blocks, Axis.X, -1);
            ProcessFaceDirection(blocks, Axis.Y, 1);
            ProcessFaceDirection(blocks, Axis.Y, -1);
            ProcessFaceDirection(blocks, Axis.Z, 1);
            ProcessFaceDirection(blocks, Axis.Z, -1);

            if (_vertices.Count == 0)
                return (null, null);

            return (_vertices.ToArray(), _indices.ToArray());
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

                        // FIX 1: pasar direction correcto
                        if (!IsFaceVisible(blocks, axis, direction, x, y, z))
                            continue;

                        // FIX 4: pasar direction a los expand
                        int width = GreedyExpandWidth(blocks, processed, axis, direction, main, a, b, blockType);
                        int height = GreedyExpandHeight(blocks, processed, axis, direction, main, a, b, width, blockType);

                        for (int wa = 0; wa < width; wa++)
                            for (int hb = 0; hb < height; hb++)
                                processed[a + wa, b + hb] = true;

                        AddRectangleFace(axis, direction, main, a, b, width, height, blockType);
                    }
                }
            }
        }

        // FIX 4: ahora recibe direction y valida correctamente
        private int GreedyExpandWidth(byte[,,] blocks, bool[,] processed, Axis axis, int direction,
            int main, int startA, int startB, byte blockType)
        {
            int width = 1;
            while (startA + width < _size)
            {
                if (processed[startA + width, startB])
                    break;

                byte block = GetBlockAtIndices(blocks, axis, main, startA + width, startB);
                if (block != blockType)
                    break;

                var (x, y, z) = GetCoordinatesFromIndices(axis, main, startA + width, startB);
                if (!IsFaceVisible(blocks, axis, direction, x, y, z))  // FIX 1
                    break;

                width++;
            }
            return width;
        }

        // FIX 4: ahora recibe direction
        private int GreedyExpandHeight(byte[,,] blocks, bool[,] processed, Axis axis, int direction,
            int main, int startA, int startB, int width, byte blockType)
        {
            int height = 1;
            while (startB + height < _size)
            {
                bool canExpand = true;
                for (int a = startA; a < startA + width; a++)
                {
                    if (processed[a, startB + height])
                    {
                        canExpand = false;
                        break;
                    }

                    byte block = GetBlockAtIndices(blocks, axis, main, a, startB + height);
                    if (block != blockType)
                    {
                        canExpand = false;
                        break;
                    }

                    var (x, y, z) = GetCoordinatesFromIndices(axis, main, a, startB + height);
                    if (!IsFaceVisible(blocks, axis, direction, x, y, z))  // FIX 1
                    {
                        canExpand = false;
                        break;
                    }
                }

                if (!canExpand)
                    break;

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
            int width, int height, byte blockType)
        {
            int baseVertex = _vertices.Count;
            if (baseVertex + 4 > 65535)
                return;

            // FIX 2: offset correcto — cara positiva se coloca en main+1 (lado exterior)
            int faceOffset = direction > 0 ? main + 1 : main;

            Vector3[] corners = new Vector3[4];
            Vector3 normal;

            switch (axis)
            {
                case Axis.X:
                    normal = new Vector3(direction, 0, 0);
                    if (direction > 0)
                    {
                        // FIX 3: winding CCW visto desde +X (exterior)
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
                    if (direction > 0)
                    {
                        // Cara +Y (top): CCW visto desde arriba
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
                    if (direction > 0)
                    {
                        corners[0] = new Vector3(a, b, faceOffset);
                        corners[1] = new Vector3(a, b + height, faceOffset);
                        corners[2] = new Vector3(a + width, b + height, faceOffset);
                        corners[3] = new Vector3(a + width, b, faceOffset);
                    }
                    else
                    {
                        corners[0] = new Vector3(a + width, b, faceOffset);
                        corners[1] = new Vector3(a + width, b + height, faceOffset);
                        corners[2] = new Vector3(a, b + height, faceOffset);
                        corners[3] = new Vector3(a, b, faceOffset);
                    }
                    break;

                default:
                    return;
            }

            Color color = BlockType.GetBlockColor(blockType);

            for (int i = 0; i < 4; i++)
                _vertices.Add(new VertexPositionNormalColor(corners[i], normal, color));

            // FIX 3: índices consistentes 0,1,2 / 0,2,3 (CCW)
            if(axis != Axis.Z) {
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 1));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 3));
            }  

            if (axis == Axis.Z && direction < 0)
            {
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 1));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 3));
            }
            else
            {
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 2));
                _indices.Add((ushort)(baseVertex + 1));
                _indices.Add((ushort)(baseVertex + 0));
                _indices.Add((ushort)(baseVertex + 3));
                _indices.Add((ushort)(baseVertex + 2));
            }
        }

        private enum Axis { X, Y, Z }
    }
}