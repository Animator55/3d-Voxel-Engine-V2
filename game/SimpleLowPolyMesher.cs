using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Generador simple de mallas para chunks LowPoly a distancia.
    ///
    /// SIMPLIFICATION LEVELS (3 niveles, 0–2):
    ///   0 → stride 1, árboles completos     (mayor detalle)
    ///   1 → stride 1, árboles cada 2 celdas
    ///   2 → stride 2, árboles cada 4 celdas (menor detalle)
    /// </summary>
    public class SimpleLowPolyMesher
    {
        private readonly LowPolyChunk _chunk;
        private readonly int _size;
        private readonly int _simplificationLevel;

        private List<VertexPositionNormalColor> _vertices;
        private List<ushort> _indices;

        public SimpleLowPolyMesher(LowPolyChunk chunk, int size = 16, int simplificationLevel = 0)
        {
            _chunk = chunk;
            _size  = size;
            _simplificationLevel = Math.Clamp(simplificationLevel, 0, LowPolyChunk.LOD_LEVELS - 1);
        }

        public (VertexPositionNormalColor[] vertices, ushort[] indices) GenerateMesh()
        {
            _vertices = new List<VertexPositionNormalColor>(256);
            _indices  = new List<ushort>(512);

            // stride en XZ: cuántos bloques cubre cada quad
            int stride = _simplificationLevel switch
            {
                0 => 1,
                1 => 1,
                _ => 2,   // nivel 2
            };

            if (stride == 1)
            {
                var blocks = _chunk.GetBlocks();
                for (int x = 0; x < _size; x++)
                for (int y = 0; y < _size; y++)
                for (int z = 0; z < _size; z++)
                {
                    byte blockType = blocks[x, y, z];
                    if (blockType != BlockType.Air)
                        AddBlockMesh(x, y, z, blockType, blocks);
                }
            }
            else
            {
                BuildStridedMesh(stride);
            }

            if (_vertices.Count == 0)
                return (null, null);

            return (_vertices.ToArray(), _indices.ToArray());
        }

        // ============================================================
        //  MESH CON STRIDE > 1
        // ============================================================

        private void BuildStridedMesh(int stride)
        {
            var blocks = _chunk.GetBlocks();

            for (int sx = 0; sx < _size; sx += stride)
            for (int sz = 0; sz < _size; sz += stride)
            {
                int qw = Math.Min(stride, _size - sx);
                int qd = Math.Min(stride, _size - sz);

                for (int y = _size - 1; y >= 0; y--)
                {
                    byte dominantBlock = BlockType.Air;
                    int solidCount = 0;
                    int waterCount = 0;

                    for (int dx = 0; dx < qw; dx++)
                    for (int dz = 0; dz < qd; dz++)
                    {
                        byte b = blocks[sx + dx, y, sz + dz];
                        if (b == BlockType.Water) waterCount++;
                        else if (b != BlockType.Air) { solidCount++; dominantBlock = b; }
                    }

                    if (solidCount == 0 && waterCount == 0) continue;

                    byte repr = solidCount > 0 ? dominantBlock
                              : waterCount > 0 ? BlockType.Water
                              : BlockType.Air;

                    if (repr == BlockType.Air) continue;

                    Color color = BlockType.GetBlockColor(repr);

                    // ── Cara superior (+Y) ──────────────────────────────
                    bool topVisible = (y == _size - 1);
                    if (!topVisible)
                    {
                        topVisible = true;
                        for (int dx = 0; dx < qw && topVisible; dx++)
                        for (int dz = 0; dz < qd && topVisible; dz++)
                            if (blocks[sx + dx, y + 1, sz + dz] != BlockType.Air)
                                topVisible = false;
                    }
                    if (topVisible) AddScaledTopFace(sx, y, sz, qw, qd, color);

                    // ── Cara inferior (-Y) ──────────────────────────────
                    bool botVisible = (y == 0);
                    if (!botVisible)
                    {
                        botVisible = true;
                        for (int dx = 0; dx < qw && botVisible; dx++)
                        for (int dz = 0; dz < qd && botVisible; dz++)
                            if (blocks[sx + dx, y - 1, sz + dz] != BlockType.Air)
                                botVisible = false;
                    }
                    if (botVisible) AddScaledBottomFace(sx, y, sz, qw, qd, color);

                    // ── +X ──────────────────────────────────────────────
                    int nx = sx + qw;
                    bool faceXPos = (nx >= _size);
                    if (!faceXPos)
                    {
                        faceXPos = true;
                        for (int dz = 0; dz < qd && faceXPos; dz++)
                            if (blocks[nx, y, sz + dz] != BlockType.Air) faceXPos = false;
                    }
                    if (faceXPos) AddScaledSideFace(sx, y, sz, qw, qd, FaceDir.PosX, color);

                    // ── -X ──────────────────────────────────────────────
                    bool faceXNeg = (sx == 0);
                    if (!faceXNeg)
                    {
                        faceXNeg = true;
                        for (int dz = 0; dz < qd && faceXNeg; dz++)
                            if (blocks[sx - 1, y, sz + dz] != BlockType.Air) faceXNeg = false;
                    }
                    if (faceXNeg) AddScaledSideFace(sx, y, sz, qw, qd, FaceDir.NegX, color);

                    // ── +Z ──────────────────────────────────────────────
                    int nz = sz + qd;
                    bool faceZPos = (nz >= _size);
                    if (!faceZPos)
                    {
                        faceZPos = true;
                        for (int dx = 0; dx < qw && faceZPos; dx++)
                            if (blocks[sx + dx, y, nz] != BlockType.Air) faceZPos = false;
                    }
                    if (faceZPos) AddScaledSideFace(sx, y, sz, qw, qd, FaceDir.PosZ, color);

                    // ── -Z ──────────────────────────────────────────────
                    bool faceZNeg = (sz == 0);
                    if (!faceZNeg)
                    {
                        faceZNeg = true;
                        for (int dx = 0; dx < qw && faceZNeg; dx++)
                            if (blocks[sx + dx, y, sz - 1] != BlockType.Air) faceZNeg = false;
                    }
                    if (faceZNeg) AddScaledSideFace(sx, y, sz, qw, qd, FaceDir.NegZ, color);
                }
            }
        }

        private enum FaceDir { PosX, NegX, PosZ, NegZ }

        // ── Quads escalados ─────────────────────────────────────────

        private void AddScaledTopFace(int x, int y, int z, int w, int d, Color color)
        {
            float py = y + 1;
            AddQuad(
                new Vector3(x,     py, z + d),
                new Vector3(x + w, py, z + d),
                new Vector3(x + w, py, z    ),
                new Vector3(x,     py, z    ),
                Vector3.UnitY, color);
        }

        private void AddScaledBottomFace(int x, int y, int z, int w, int d, Color color)
        {
            float py = y;
            AddQuad(
                new Vector3(x,     py, z    ),
                new Vector3(x + w, py, z    ),
                new Vector3(x + w, py, z + d),
                new Vector3(x,     py, z + d),
                -Vector3.UnitY, color);
        }

        private void AddScaledSideFace(int x, int y, int z, int w, int d, FaceDir dir, Color color)
        {
            float y0 = y, y1 = y + 1;
            switch (dir)
            {
                case FaceDir.PosX:
                    AddQuad(
                        new Vector3(x + w, y0, z    ),
                        new Vector3(x + w, y1, z    ),
                        new Vector3(x + w, y1, z + d),
                        new Vector3(x + w, y0, z + d),
                        Vector3.UnitX, color);
                    break;
                case FaceDir.NegX:
                    AddQuad(
                        new Vector3(x, y0, z + d),
                        new Vector3(x, y1, z + d),
                        new Vector3(x, y1, z    ),
                        new Vector3(x, y0, z    ),
                        -Vector3.UnitX, color);
                    break;
                case FaceDir.PosZ:
                    AddQuad(
                        new Vector3(x + w, y0, z + d),
                        new Vector3(x + w, y1, z + d),
                        new Vector3(x,     y1, z + d),
                        new Vector3(x,     y0, z + d),
                        Vector3.UnitZ, color);
                    break;
                case FaceDir.NegZ:
                    AddQuad(
                        new Vector3(x,     y0, z),
                        new Vector3(x,     y1, z),
                        new Vector3(x + w, y1, z),
                        new Vector3(x + w, y0, z),
                        -Vector3.UnitZ, color);
                    break;
            }
        }

        // ============================================================
        //  MESH BLOQUE A BLOQUE (stride = 1)
        // ============================================================

        private void AddBlockMesh(int x, int y, int z, byte blockType, byte[,,] blocks)
        {
            Color color = BlockType.GetBlockColor(blockType);
            Vector3 pos    = new Vector3(x, y, z);
            Vector3 posMax = pos + Vector3.One;

            if (x == _size - 1 || blocks[x + 1, y, z] == BlockType.Air)
                AddQuad(
                    new Vector3(posMax.X, pos.Y,    pos.Z),
                    new Vector3(posMax.X, posMax.Y, pos.Z),
                    new Vector3(posMax.X, posMax.Y, posMax.Z),
                    new Vector3(posMax.X, pos.Y,    posMax.Z),
                    Vector3.UnitX, color);

            if (x == 0 || blocks[x - 1, y, z] == BlockType.Air)
                AddQuad(
                    new Vector3(pos.X, pos.Y,    posMax.Z),
                    new Vector3(pos.X, posMax.Y, posMax.Z),
                    new Vector3(pos.X, posMax.Y, pos.Z),
                    new Vector3(pos.X, pos.Y,    pos.Z),
                    -Vector3.UnitX, color);

            if (y == _size - 1 || blocks[x, y + 1, z] == BlockType.Air)
                AddQuad(
                    new Vector3(pos.X,    posMax.Y, posMax.Z),
                    new Vector3(posMax.X, posMax.Y, posMax.Z),
                    new Vector3(posMax.X, posMax.Y, pos.Z),
                    new Vector3(pos.X,    posMax.Y, pos.Z),
                    Vector3.UnitY, color);

            if (y == 0 || blocks[x, y - 1, z] == BlockType.Air)
                AddQuad(
                    new Vector3(pos.X,    pos.Y, pos.Z),
                    new Vector3(posMax.X, pos.Y, pos.Z),
                    new Vector3(posMax.X, pos.Y, posMax.Z),
                    new Vector3(pos.X,    pos.Y, posMax.Z),
                    -Vector3.UnitY, color);

            if (z == _size - 1 || blocks[x, y, z + 1] == BlockType.Air)
                AddQuad(
                    new Vector3(posMax.X, pos.Y,    posMax.Z),
                    new Vector3(posMax.X, posMax.Y, posMax.Z),
                    new Vector3(pos.X,    posMax.Y, posMax.Z),
                    new Vector3(pos.X,    pos.Y,    posMax.Z),
                    Vector3.UnitZ, color);

            if (z == 0 || blocks[x, y, z - 1] == BlockType.Air)
                AddQuad(
                    new Vector3(pos.X,    pos.Y,    pos.Z),
                    new Vector3(pos.X,    posMax.Y, pos.Z),
                    new Vector3(posMax.X, posMax.Y, pos.Z),
                    new Vector3(posMax.X, pos.Y,    pos.Z),
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