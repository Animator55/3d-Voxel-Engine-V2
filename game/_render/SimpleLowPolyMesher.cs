using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Mesher LP con greedy merging de quads coplanares.
    ///
    /// El algoritmo barre cada eje (X, Y, Z) en ambas direcciones (+/-).
    /// Para cada slice perpendicular al eje construye una máscara 2D de qué
    /// faces son visibles, luego las fusiona en rectángulos lo más grandes
    /// posible antes de emitir un quad.
    ///
    /// Nivel 0 y 1 → greedy completo sobre los 3 ejes.
    /// Nivel 2      → igual (el generador ya produce pocos bloques en nivel 2,
    ///                así que el greedy los funde en muy pocos quads).
    ///
    /// NO se toca el WorldGenerator: la fuente de bloques es correcta porque
    /// se revirtió a la lógica original probada.
    /// </summary>
    public class SimpleLowPolyMesher
    {
        private readonly LowPolyChunk _chunk;
        private readonly int _size;
        private readonly int _simplificationLevel;

        private List<VertexPositionNormalColor> _vertices;
        private List<ushort> _indices;

        // Buffer de máscara reutilizable (evita alloc por slice)
        private readonly byte[] _mask;
        // Buffer de marcado reutilizable para el greedy expand
        private readonly bool[] _merged;

        public SimpleLowPolyMesher(LowPolyChunk chunk, int size = 16, int simplificationLevel = 0)
        {
            _chunk = chunk;
            _size = size;
            _simplificationLevel = Math.Clamp(simplificationLevel, 0, LowPolyChunk.LOD_LEVELS - 1);
            _mask   = new byte[size * size];
            _merged = new bool[size * size];
        }

        // ─────────────────────────────────────────────────────────────────────
        public (VertexPositionNormalColor[] vertices, ushort[] indices) GenerateMesh()
        {
            int cap = _size * _size * 6;
            _vertices = new List<VertexPositionNormalColor>(cap);
            _indices  = new List<ushort>(cap * 2);

            var blocks = _chunk.GetBlocks();
            BuildGreedyMesh(blocks);

            if (_vertices.Count == 0) return (null, null);
            return (_vertices.ToArray(), _indices.ToArray());
        }

        // ═════════════════════════════════════════════════════════════════════
        // GREEDY MESH — barre los 3 ejes en ambas direcciones
        // ═════════════════════════════════════════════════════════════════════
        private void BuildGreedyMesh(byte[,,] blocks)
        {
            GreedyAxis(blocks, 0);  // eje X  (faces +X y -X)
            GreedyAxis(blocks, 1);  // eje Y  (faces +Y y -Y)
            GreedyAxis(blocks, 2);  // eje Z  (faces +Z y -Z)
        }

        /// <summary>
        /// Procesa todas las slices del eje dado en ambas direcciones (+1 y -1).
        ///
        /// Convención de ejes perpendiculares:
        ///   axis=0 (X) → u=Y, v=Z
        ///   axis=1 (Y) → u=X, v=Z
        ///   axis=2 (Z) → u=X, v=Y
        /// Esto mantiene una orientación consistente que produce winding CCW
        /// correcto para ambas direcciones con el flip de winding en dir=-1.
        /// </summary>
        private void GreedyAxis(byte[,,] blocks, int axis)
        {
            // Ejes perpendiculares — FIJOS, no rotados, para winding predecible
            int uAxis = axis == 0 ? 1 : 0;   // Y para X, X para Y y Z
            int vAxis = axis == 2 ? 1 : 2;   // Y para Z, Z para X e Y

            int size = _size;

            for (int dir = -1; dir <= 1; dir += 2)
            {
                for (int main = 0; main < size; main++)
                {
                    // ── Construir máscara 2D ──────────────────────────────
                    int maskIdx = 0;
                    for (int a = 0; a < size; a++)
                    {
                        for (int b = 0; b < size; b++, maskIdx++)
                        {
                            int[] pos  = IndexToXYZ(axis, main, a, b);
                            byte cur   = blocks[pos[0], pos[1], pos[2]];

                            // Vecino en la dirección de la normal
                            int nMain = main + dir;
                            byte neighbor;
                            if (nMain < 0 || nMain >= size)
                                neighbor = BlockType.Air;   // borde del chunk
                            else
                            {
                                int[] npos = IndexToXYZ(axis, nMain, a, b);
                                neighbor   = blocks[npos[0], npos[1], npos[2]];
                            }

                            // Cara visible = cur es sólido y neighbor es aire
                            bool visible = (cur != BlockType.Air) && (neighbor == BlockType.Air);
                            _mask[maskIdx] = visible ? cur : BlockType.Air;
                        }
                    }

                    // ── Greedy expand sobre la máscara ────────────────────
                    Array.Clear(_merged, 0, size * size);

                    for (int a = 0; a < size; a++)
                    {
                        for (int b = 0; b < size; b++)
                        {
                            int mi = a * size + b;
                            if (_merged[mi]) continue;
                            byte blockType = _mask[mi];
                            if (blockType == BlockType.Air) continue;

                            // Expandir en dirección 'a' (uAxis)
                            int w = 1;
                            while (a + w < size
                                   && !_merged[(a + w) * size + b]
                                   && _mask[(a + w) * size + b] == blockType)
                                w++;

                            // Expandir en dirección 'b' (vAxis)
                            int h = 1;
                            bool canExpand = true;
                            while (b + h < size && canExpand)
                            {
                                for (int k = 0; k < w; k++)
                                {
                                    int ki = (a + k) * size + (b + h);
                                    if (_merged[ki] || _mask[ki] != blockType)
                                    { canExpand = false; break; }
                                }
                                if (canExpand) h++;
                            }

                            // Marcar región como procesada
                            for (int wa = 0; wa < w; wa++)
                                for (int hb = 0; hb < h; hb++)
                                    _merged[(a + wa) * size + (b + hb)] = true;

                            // Emitir el quad fusionado
                            EmitQuad(axis, dir, main, a, b, w, h, blockType);
                        }
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Convierte (axis, main, a, b) → coordenadas [x, y, z]
        // De forma que 'a' siempre mapea a uAxis y 'b' a vAxis.
        // ─────────────────────────────────────────────────────────────────────
        private static int[] IndexToXYZ(int axis, int main, int a, int b)
        {
            int[] xyz = new int[3];
            xyz[axis] = main;
            // uAxis = (axis==0) ? 1 : 0
            // vAxis = (axis==2) ? 1 : 2
            int uAxis = axis == 0 ? 1 : 0;
            int vAxis = axis == 2 ? 1 : 2;
            xyz[uAxis] = a;
            xyz[vAxis] = b;
            return xyz;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Emite un quad greedy.
        //
        // La cara está en el plano perpendicular a 'axis'.
        // Si dir=+1, la cara exterior está en main+1.
        // Si dir=-1, la cara exterior está en main.
        //
        // Winding:
        //   dir=+1: CCW visto desde la dirección positiva del eje.
        //   dir=-1: se invierten dos vértices para mantener CCW desde afuera.
        // ─────────────────────────────────────────────────────────────────────
        private void EmitQuad(int axis, int dir, int main,
                               int a, int b, int w, int h,
                               byte blockType)
        {
            Color color = BlockType.GetBlockColor(blockType);

            float f = dir > 0 ? main + 1 : main;   // plano de la cara
            float a1 = a, a2 = a + w;               // rango en uAxis
            float b1 = b, b2 = b + h;               // rango en vAxis

            // Corners con winding derivado directamente del mesher original
            // (AddBlockMesh), extendido a quads greedy de tamaño w×h.
            //
            // Eje X:
            //   +X: (f, a1, b1)  (f, a2, b1)  (f, a2, b2)  (f, a1, b2)   orden orig
            //   -X: (f, a1, b2)  (f, a2, b2)  (f, a2, b1)  (f, a1, b1)   orden orig
            // Eje Y:
            //   +Y: (a1, f, b2)  (a2, f, b2)  (a2, f, b1)  (a1, f, b1)   orden orig
            //   -Y: (a1, f, b1)  (a2, f, b1)  (a2, f, b2)  (a1, f, b2)   orden orig
            // Eje Z:
            //   +Z: (a2, b1, f)  (a2, b2, f)  (a1, b2, f)  (a1, b1, f)   orden orig
            //   -Z: (a1, b1, f)  (a1, b2, f)  (a2, b2, f)  (a2, b1, f)   orden orig
            Vector3 p0, p1, p2, p3;
            switch (axis)
            {
                case 0: // X
                    if (dir > 0) {
                        p0 = new Vector3(f, a1, b1); p1 = new Vector3(f, a2, b1);
                        p2 = new Vector3(f, a2, b2); p3 = new Vector3(f, a1, b2);
                    } else {
                        p0 = new Vector3(f, a1, b2); p1 = new Vector3(f, a2, b2);
                        p2 = new Vector3(f, a2, b1); p3 = new Vector3(f, a1, b1);
                    }
                    break;
                case 1: // Y
                    if (dir > 0) {
                        p0 = new Vector3(a1, f, b2); p1 = new Vector3(a2, f, b2);
                        p2 = new Vector3(a2, f, b1); p3 = new Vector3(a1, f, b1);
                    } else {
                        p0 = new Vector3(a1, f, b1); p1 = new Vector3(a2, f, b1);
                        p2 = new Vector3(a2, f, b2); p3 = new Vector3(a1, f, b2);
                    }
                    break;
                default: // Z
                    if (dir > 0) {
                        p0 = new Vector3(a2, b1, f); p1 = new Vector3(a2, b2, f);
                        p2 = new Vector3(a1, b2, f); p3 = new Vector3(a1, b1, f);
                    } else {
                        p0 = new Vector3(a1, b1, f); p1 = new Vector3(a1, b2, f);
                        p2 = new Vector3(a2, b2, f); p3 = new Vector3(a2, b1, f);
                    }
                    break;
            }

            var normal = new Vector3(
                axis == 0 ? dir : 0,
                axis == 1 ? dir : 0,
                axis == 2 ? dir : 0);

            int bi = _vertices.Count;
            if (bi + 4 > 65535) return;

            _vertices.Add(new VertexPositionNormalColor(p0, normal, color));
            _vertices.Add(new VertexPositionNormalColor(p1, normal, color));
            _vertices.Add(new VertexPositionNormalColor(p2, normal, color));
            _vertices.Add(new VertexPositionNormalColor(p3, normal, color));

            _indices.Add((ushort) bi);
            _indices.Add((ushort)(bi + 1));
            _indices.Add((ushort)(bi + 2));
            _indices.Add((ushort) bi);
            _indices.Add((ushort)(bi + 2));
            _indices.Add((ushort)(bi + 3));
        }
    }
}