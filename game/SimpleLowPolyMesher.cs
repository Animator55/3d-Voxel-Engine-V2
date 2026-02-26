using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Generador simple de mallas para chunks LowPoly a distancia.
    /// Sin octree, sin greedy meshing - solo renderiza cada bloque como un cubo.
    /// Muy rápido para chunks lejanos.
    /// </summary>
    public class SimpleLowPolyMesher
    {
        private readonly LowPolyChunk _chunk;
        private readonly int _size;

        private List<VertexPositionNormalColor> _vertices;
        private List<ushort> _indices;

        public SimpleLowPolyMesher(LowPolyChunk chunk, int size = 16)
        {
            _chunk = chunk;
            _size = size;
        }

        public (VertexPositionNormalColor[] vertices, ushort[] indices) GenerateMesh()
        {
            _vertices = new List<VertexPositionNormalColor>(256);
            _indices = new List<ushort>(512);

            var blocks = _chunk.GetBlocks();

            // Iterar cada bloque
            for (int x = 0; x < _size; x++)
            {
                for (int y = 0; y < _size; y++)
                {
                    for (int z = 0; z < _size; z++)
                    {
                        byte blockType = blocks[x, y, z];

                        // Solo renderizar si no es aire
                        if (blockType != BlockType.Air)
                        {
                            AddBlockMesh(x, y, z, blockType, blocks);
                        }
                    }
                }
            }

            if (_vertices.Count == 0)
                return (null, null);

            return (_vertices.ToArray(), _indices.ToArray());
        }

        private void AddBlockMesh(int x, int y, int z, byte blockType, byte[,,] blocks)
        {
            Color color = BlockType.GetBlockColor(blockType);
            Vector3 pos = new Vector3(x, y, z);
            Vector3 posMax = pos + Vector3.One;

            // Solo renderizar caras visibles (exteriores del chunk o adyacentes a aire)
            // IMPORTANTE: CCW visto desde AFUERA del cubo
            
            // Cara +X - CORREGIDO
if (x == _size - 1 || blocks[x + 1, y, z] == BlockType.Air)
    AddQuad(
        new Vector3(posMax.X, pos.Y, pos.Z),
        new Vector3(posMax.X, posMax.Y, pos.Z),
        new Vector3(posMax.X, posMax.Y, posMax.Z),
        new Vector3(posMax.X, pos.Y, posMax.Z),
        Vector3.UnitX, color);

// Cara -X - CORREGIDO
if (x == 0 || blocks[x - 1, y, z] == BlockType.Air)
    AddQuad(
        new Vector3(pos.X, pos.Y, posMax.Z),
        new Vector3(pos.X, posMax.Y, posMax.Z),
        new Vector3(pos.X, posMax.Y, pos.Z),
        new Vector3(pos.X, pos.Y, pos.Z),
        -Vector3.UnitX, color);

            // Cara +Y (normal (0,1,0), miramos desde +Y hacia adentro)
            if (y == _size - 1 || blocks[x, y + 1, z] == BlockType.Air)
                AddQuad(
                    new Vector3(pos.X, posMax.Y, posMax.Z),
                    new Vector3(posMax.X, posMax.Y, posMax.Z),
                    new Vector3(posMax.X, posMax.Y, pos.Z),
                    new Vector3(pos.X, posMax.Y, pos.Z),
                    Vector3.UnitY, color);

            // Cara -Y (normal (0,-1,0), miramos desde -Y hacia adentro)
            if (y == 0 || blocks[x, y - 1, z] == BlockType.Air)
                AddQuad(
                    new Vector3(pos.X, pos.Y, pos.Z),
                    new Vector3(posMax.X, pos.Y, pos.Z),
                    new Vector3(posMax.X, pos.Y, posMax.Z),
                    new Vector3(pos.X, pos.Y, posMax.Z),
                    -Vector3.UnitY, color);

            // Cara +Z (normal (0,0,1), miramos desde +Z hacia adentro)
            if (z == _size - 1 || blocks[x, y, z + 1] == BlockType.Air)
                AddQuad(
                    new Vector3(posMax.X, pos.Y, posMax.Z),
                    new Vector3(posMax.X, posMax.Y, posMax.Z),
                    new Vector3(pos.X, posMax.Y, posMax.Z),
                    new Vector3(pos.X, pos.Y, posMax.Z),
                    Vector3.UnitZ, color);

            // Cara -Z (normal (0,0,-1), miramos desde -Z hacia adentro)
            if (z == 0 || blocks[x, y, z - 1] == BlockType.Air)
                AddQuad(
                    new Vector3(pos.X, pos.Y, pos.Z),
                    new Vector3(pos.X, posMax.Y, pos.Z),
                    new Vector3(posMax.X, posMax.Y, pos.Z),
                    new Vector3(posMax.X, pos.Y, pos.Z),
                    -Vector3.UnitZ, color);
        }

        private void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, Color color)
        {
            int baseIndex = _vertices.Count;
            _vertices.Add(new VertexPositionNormalColor(v0, normal, color));
            _vertices.Add(new VertexPositionNormalColor(v1, normal, color));
            _vertices.Add(new VertexPositionNormalColor(v2, normal, color));
            _vertices.Add(new VertexPositionNormalColor(v3, normal, color));

            _indices.Add((ushort)baseIndex);
            _indices.Add((ushort)(baseIndex + 1));
            _indices.Add((ushort)(baseIndex + 2));
            _indices.Add((ushort)baseIndex);
            _indices.Add((ushort)(baseIndex + 2));
            _indices.Add((ushort)(baseIndex + 3));
        }
    }
}
