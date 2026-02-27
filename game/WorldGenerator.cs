using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Generador procedural de mundo mejorado.
    /// 
    /// MEJORAS RESPECTO A LA V1:
    /// - Domain warping en generación de biomas (bordes naturales)
    /// - Ríos procedurales que excavan el terreno
    /// - 5 variantes de árboles con ramas reales y clusters de hojas
    /// - Mezcla de alturas con smooth weights entre biomas (evita cortes)
    /// - Bioma SnowPeaks con nieve en superficie
    /// - CACHE de bloques global para evitar regeneración
    /// 
    /// SIMPLIFICATION LEVELS (para LOD):
    ///   0 → calidad completa (HQ)
    ///   1 → árboles cada 2 celdas de grid
    ///   2 → árboles cada 4 celdas + saltea 1 de cada 2 bloques en el mesher
    ///   3 → sin árboles + saltea 1 de cada 4 bloques + chunks enteros según stride
    /// </summary>
    public class WorldGenerator
    {
        private readonly int _seed;

        // Configuración de altura
        // SeaLevel:   hasta aquí llega el agua (océanos, ríos, orillas)
        // BaseHeight: piso del terreno sólido, SIEMPRE > SeaLevel para que haya costa visible
        private const int SeaLevel   = 30;
        private const int BaseHeight = 40;
        private const int MaxHeight  = 120;
        private const int MinHeight  = 4;

        // Permutation table para Perlin noise
        private readonly int[] _perm = new int[512];

        // CACHE de bloques generados
        private readonly Dictionary<(int, int, int), byte[,,]> _blockCache = new();
        private readonly object _cacheLock = new object();

        public WorldGenerator(int seed = 12345)
        {
            _seed = seed;
            InitPermTable();
        }

        // ============================================================
        //  INICIALIZACIÓN
        // ============================================================

        private void InitPermTable()
        {
            int[] p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;

            var rng = new Random(_seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }

            for (int i = 0; i < 512; i++)
                _perm[i] = p[i & 255];
        }

        // ============================================================
        //  CACHE Y GENERACIÓN DE CHUNK
        // ============================================================

        public byte[,,] GetOrGenerateChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            var key = (chunkX, chunkY, chunkZ);

            lock (_cacheLock)
            {
                if (_blockCache.TryGetValue(key, out var cached))
                    return cached;
            }

            byte[,,] blocks = GenerateChunk(chunkX, chunkY, chunkZ, chunkSize);

            lock (_cacheLock)
            {
                if (_blockCache.TryGetValue(key, out var cached2))
                    return cached2;
                _blockCache[key] = blocks;
            }

            return blocks;
        }

        public byte[,,] GenerateChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            byte[,,] blocks = new byte[chunkSize, chunkSize, chunkSize];

            int worldX = chunkX * chunkSize;
            int worldY = chunkY * chunkSize;
            int worldZ = chunkZ * chunkSize;

            int[,] heights       = new int[chunkSize, chunkSize];
            BiomeType[,] biomes  = new BiomeType[chunkSize, chunkSize];
            bool[,] riverMask    = new bool[chunkSize, chunkSize];

            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    float wx = worldX + bx;
                    float wz = worldZ + bz;

                    biomes[bx, bz]  = GetBiome(wx, wz);
                    heights[bx, bz] = GetTerrainHeight(wx, wz, biomes[bx, bz], out bool isRiver);
                    riverMask[bx, bz] = isRiver;
                }
            }

            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    int terrainHeight = heights[bx, bz];
                    BiomeType biome   = biomes[bx, bz];
                    bool isRiver      = riverMask[bx, bz];

                    for (int by = 0; by < chunkSize; by++)
                    {
                        int wy = worldY + by;
                        blocks[bx, by, bz] = GetBlockAt(worldX + bx, wy, worldZ + bz, terrainHeight, biome, isRiver);
                    }
                }
            }

            // HQ siempre con árboles completos (simplificationLevel = 0)
            PlaceTrees(blocks, worldX, worldY, worldZ, chunkSize, heights, biomes, riverMask, simplificationLevel: 0);

            return blocks;
        }

        // ============================================================
        //  BIOMAS (con domain warping)
        // ============================================================

        private enum BiomeType { Plains, Hills, Mountains, SnowPeaks, Forest }

        private BiomeType GetBiome(float wx, float wz)
        {
            // Domain warp: desplazar las coordenadas con otro ruido para bordes orgánicos
            float warpX = OctaveNoise2D(wx, wz, scale: 200f, octaves: 2, persistence: 0.5f, seed: _seed + 500) * 60f;
            float warpZ = OctaveNoise2D(wx, wz, scale: 200f, octaves: 2, persistence: 0.5f, seed: _seed + 501) * 60f;

            float temp      = OctaveNoise2D(wx + warpX, wz + warpZ, scale: 400f, octaves: 2, persistence: 0.5f, seed: _seed);
            float roughness = OctaveNoise2D(wx + warpX, wz + warpZ, scale: 300f, octaves: 2, persistence: 0.5f, seed: _seed + 1);

            temp      = (temp      + 1f) * 0.5f;
            roughness = (roughness + 1f) * 0.5f;

            if (roughness > 0.72f) return temp < 0.35f ? BiomeType.SnowPeaks : BiomeType.Mountains;
            if (roughness > 0.52f) return BiomeType.Hills;
            if (temp > 0.55f)      return BiomeType.Forest;
            return BiomeType.Plains;
        }

        // ============================================================
        //  ALTURA DEL TERRENO (con mezcla suave y ríos)
        // ============================================================

        private int GetTerrainHeight(float wx, float wz, BiomeType biome, out bool isRiver)
        {
            isRiver = false;

            float h = GetBlendedHeight(wx, wz, biome);

            if (biome == BiomeType.Plains || biome == BiomeType.Hills || biome == BiomeType.Forest)
            {
                float riverInfluence = GetRiverInfluence(wx, wz, out float riverDepth01);
                if (riverInfluence > 0f)
                {
                    float riverBottom = SeaLevel - 3f;
                    h = h * (1f - riverInfluence) + riverBottom * riverInfluence;

                    if (riverInfluence > 0.3f)
                        isRiver = true;
                }
            }

            return (int)Math.Clamp(h, MinHeight, MaxHeight);
        }

        private float GetBlendedHeight(float wx, float wz, BiomeType biome)
        {
            float roughness01 = (OctaveNoise2D(wx, wz, scale: 300f, octaves: 2, persistence: 0.5f, seed: _seed + 1) + 1f) * 0.5f;
            float temp01      = (OctaveNoise2D(wx, wz, scale: 400f, octaves: 2, persistence: 0.5f, seed: _seed) + 1f) * 0.5f;

            float plainsH = BaseHeight + 2f  + OctaveNoise2D(wx, wz, scale: 120f, octaves: 3, persistence: 0.4f,  seed: _seed + 10) * 8f;
            float hillsH  = BaseHeight + 6f  + OctaveNoise2D(wx, wz, scale: 80f,  octaves: 4, persistence: 0.5f,  seed: _seed + 20) * 20f;
            float forestH = BaseHeight + 4f  + OctaveNoise2D(wx, wz, scale: 100f, octaves: 4, persistence: 0.45f, seed: _seed + 30) * 12f;

            float baseM   = OctaveNoise2D(wx, wz, scale: 60f, octaves: 6, persistence: 0.55f, seed: _seed + 40);
            float ridgeM  = RidgeNoise2D(wx, wz, scale: 50f, octaves: 4, seed: _seed + 41);
            float mountainH = BaseHeight + 16f + baseM * 35f + ridgeM * 25f;

            float baseS   = OctaveNoise2D(wx, wz, scale: 50f, octaves: 6, persistence: 0.6f, seed: _seed + 50);
            float ridgeS  = RidgeNoise2D(wx, wz, scale: 40f, octaves: 5, seed: _seed + 51);
            float snowH   = BaseHeight + 36f + baseS * 40f + ridgeS * 30f;

            float wPlains   = SmoothStep(0.35f, 0.0f,  roughness01) * (temp01 < 0.55f ? 1f : 0f);
            float wForest   = SmoothStep(0.35f, 0.0f,  roughness01) * (temp01 >= 0.55f ? 1f : 0f);
            float wHills    = SmoothStep(0.35f, 0.52f, roughness01) * SmoothStep(0.72f, 0.52f, roughness01);
            float wMountain = SmoothStep(0.52f, 0.72f, roughness01) * (temp01 >= 0.35f ? 1f : 0f);
            float wSnow     = SmoothStep(0.52f, 0.72f, roughness01) * (temp01 < 0.35f ? 1f : 0f);

            float total = wPlains + wForest + wHills + wMountain + wSnow;
            if (total < 0.001f) return plainsH;

            return (plainsH * wPlains + forestH * wForest + hillsH * wHills +
                    mountainH * wMountain + snowH * wSnow) / total;
        }

        // ============================================================
        //  RÍOS
        // ============================================================

        private float GetRiverInfluence(float wx, float wz, out float depth01)
        {
            float rwarpX = OctaveNoise2D(wx, wz, scale: 150f, octaves: 2, persistence: 0.5f, seed: _seed + 600) * 30f;
            float rwarpZ = OctaveNoise2D(wx, wz, scale: 150f, octaves: 2, persistence: 0.5f, seed: _seed + 601) * 30f;

            float n = OctaveNoise2D(wx + rwarpX, wz + rwarpZ, scale: 300f, octaves: 4, persistence: 0.5f, seed: _seed + 700);
            n = (n + 1f) * 0.5f;

            float riverRaw = 1f - Math.Abs(n * 2f - 1f);
            riverRaw = (float)Math.Pow(riverRaw, 3f);

            depth01 = riverRaw;

            float threshold = 0.70f;
            if (riverRaw < threshold) return 0f;

            return Math.Clamp((riverRaw - threshold) / (1f - threshold), 0f, 1f);
        }

        // ============================================================
        //  TIPO DE BLOQUE
        // ============================================================

        private byte GetBlockAt(int wx, int wy, int wz, int terrainHeight, BiomeType biome, bool isRiver)
        {
            if (wy > terrainHeight)
            {
                if (wy <= SeaLevel)
                    return BlockType.Water;
                return BlockType.Air;
            }

            if (wy > 2 && wy < terrainHeight - 2)
            {
                float cave = OctaveNoise3D(wx, wy, wz, scale: 20f, octaves: 2, persistence: 0.5f, seed: _seed + 99);
                if (cave > 0.55f)
                    return BlockType.Air;
            }

            if (wy == terrainHeight)
            {
                if (isRiver) return BlockType.Sand;

                return biome switch
                {
                    BiomeType.SnowPeaks  => BlockType.Snow,
                    BiomeType.Mountains  => BlockType.Stone,
                    _                    => BlockType.Grass,
                };
            }

            int dirtDepth = (biome == BiomeType.Mountains || biome == BiomeType.SnowPeaks) ? 1 : 3;
            if (wy >= terrainHeight - dirtDepth)
            {
                return biome switch
                {
                    BiomeType.Mountains  => BlockType.Stone,
                    BiomeType.SnowPeaks  => BlockType.Stone,
                    _                    => BlockType.Dirt,
                };
            }

            return BlockType.Stone;
        }

        // ============================================================
        //  ÁRBOLES (5 variantes con ramas y clusters de hojas)
        // ============================================================

        private static readonly (int, int, int)[] _trunk0 =
        {
            (0,0,0),(0,1,0),(0,2,0),(0,3,0),(0,4,0),(0,5,0),
        };
        private static readonly (int, int, int)[] _branches0 = { };
        private static readonly (int, int, int)[] _tips0 =
        {
            (0,5,0),(1,5,0),(-1,5,0),(0,5,1),(0,5,-1),
            (0,6,0),(1,6,0),(-1,6,0),(0,6,1),(0,6,-1),
        };

        private static readonly (int, int, int)[] _trunk1 =
        {
            (0,0,0),(0,1,0),(0,2,0),(0,3,0),(0,4,0),(0,5,0),(0,6,0),
        };
        private static readonly (int, int, int)[] _branches1 =
        {
            (1,4,0),(2,4,0),(3,4,0),(-1,4,0),(-2,4,0),(-3,4,0),
            (0,4,1),(0,4,2),(0,4,3),(0,4,-1),(0,4,-2),(0,4,-3),
            (2,5,2),(-2,5,2),(2,5,-2),(-2,5,-2),
        };
        private static readonly (int, int, int)[] _tips1 =
        {
            (3,4,0),(-3,4,0),(0,4,3),(0,4,-3),
            (2,5,2),(-2,5,2),(2,5,-2),(-2,5,-2),
            (1,6,0),(-1,6,0),(0,6,1),(0,6,-1),(0,6,0),
        };

        private static readonly (int, int, int)[] _trunk2 =
        {
            (0,0,0),(0,1,0),(0,2,0),
            (1,3,0),(1,4,0),(2,5,0),(2,6,0),(2,7,1),(2,8,1),
        };
        private static readonly (int, int, int)[] _branches2 =
        {
            (-1,3,0),(-2,4,0),(-3,4,-1),
            (3,5,0),(4,6,0),(5,6,1),
            (3,7,1),(4,7,1),(3,8,2),
        };
        private static readonly (int, int, int)[] _tips2 =
        {
            (-3,4,-1),(5,6,1),(3,8,2),
            (2,8,2),(1,8,1),(3,8,0),(2,9,1),
        };

        private static readonly (int, int, int)[] _trunk3 =
        {
            (0,0,0),(0,1,0),(0,2,0),(0,3,0),(0,4,0),(0,5,0),
            (0,6,0),(0,7,0),(0,8,0),(0,9,0),(0,10,0),
        };
        private static readonly (int, int, int)[] _branches3 =
        {
            (1,6,0),(2,6,0),(3,6,1),(-1,6,0),(-2,6,0),(-3,6,-1),
            (0,6,1),(0,6,2),(1,6,3),(0,6,-1),(0,6,-2),
            (1,8,0),(2,8,0),(-1,8,0),(-2,8,0),(0,8,1),(0,8,-1),
            (1,10,0),(-1,10,0),(0,10,1),(0,10,-1),
        };
        private static readonly (int, int, int)[] _tips3 =
        {
            (3,6,1),(-3,6,-1),(1,6,3),
            (2,8,0),(-2,8,0),(0,8,1),(0,8,-1),(2,8,2),(-2,8,2),
            (1,10,0),(-1,10,0),(0,10,1),(0,10,-1),(0,10,0),
        };

        private static readonly (int, int, int)[] _trunk4 =
        {
            (0,0,0),(0,1,0),(0,2,0),(1,3,1),(1,4,1),
        };
        private static readonly (int, int, int)[] _branches4 =
        {
            (2,3,0),(3,3,0),(4,3,1),(-1,3,0),(-2,3,0),(-3,3,-1),
            (0,3,2),(0,3,3),(1,3,4),
            (2,4,0),(3,4,0),(-1,4,0),(-2,4,0),(0,4,2),(0,4,3),
        };
        private static readonly (int, int, int)[] _tips4 =
        {
            (4,3,1),(-3,3,-1),(1,3,4),
            (3,4,0),(-2,4,0),(0,4,3),
            (2,5,1),(-1,5,1),(1,5,2),(0,5,0),
        };

        private static readonly (int, int, int)[] _leafCluster =
        {
            (0,0,0),
            (1,0,0),(-1,0,0),(0,0,1),(0,0,-1),(0,1,0),(0,-1,0),
            (1,1,0),(-1,1,0),(0,1,1),(0,1,-1),
            (2,0,0),(-2,0,0),(0,0,2),(0,0,-2),
            (1,0,1),(-1,0,1),(1,0,-1),(-1,0,-1),
            (1,2,0),(-1,2,0),(0,2,1),(0,2,-1),
        };

        private struct TreeVariant
        {
            public (int, int, int)[] Trunk;
            public (int, int, int)[] Branches;
            public (int, int, int)[] Tips;
        }

        private static readonly TreeVariant[] _treeVariants =
        {
            new TreeVariant { Trunk = _trunk0, Branches = _branches0, Tips = _tips0 },
            new TreeVariant { Trunk = _trunk1, Branches = _branches1, Tips = _tips1 },
            new TreeVariant { Trunk = _trunk2, Branches = _branches2, Tips = _tips2 },
            new TreeVariant { Trunk = _trunk3, Branches = _branches3, Tips = _tips3 },
            new TreeVariant { Trunk = _trunk4, Branches = _branches4, Tips = _tips4 },
        };

        /// <summary>
        /// Coloca árboles en el chunk según el nivel de simplificación:
        ///   0 → grid de 5, todos los árboles (HQ)
        ///   1 → grid de 10 (skipea 1 de cada 2 celdas)
        ///   2 → grid de 20 (skipea 3 de cada 4 celdas)
        ///   3+ → sin árboles
        /// La máscara de river/bioma es la misma; no se generan máscaras extra.
        /// </summary>
        private void PlaceTrees(
            byte[,,] blocks,
            int worldX, int worldY, int worldZ,
            int chunkSize,
            int[,] heights,
            BiomeType[,] biomes,
            bool[,] riverMask,
            int simplificationLevel = 0)
        {
            // Con 3 niveles (0,1,2) todos generan árboles.
            // gridSize controla el espaciado de la grilla de árboles.
            int gridSize = simplificationLevel switch
            {
                0 => 5,
                1 => 10,
                _ => 20,  // nivel 2
            };

            for (int gx = 0; gx < chunkSize / gridSize + 1; gx++)
            {
                for (int gz = 0; gz < chunkSize / gridSize + 1; gz++)
                {
                    int cellWX = worldX + gx * gridSize;
                    int cellWZ = worldZ + gz * gridSize;

                    float chance = Hash2Df(cellWX, cellWZ, _seed + 200);

                    int offX = (int)(Hash2Df(cellWX, cellWZ, _seed + 201) * (gridSize - 1));
                    int offZ = (int)(Hash2Df(cellWX, cellWZ, _seed + 202) * (gridSize - 1));

                    int localX = gx * gridSize + offX;
                    int localZ = gz * gridSize + offZ;

                    if (localX >= chunkSize || localZ >= chunkSize) continue;
                    if (riverMask[localX, localZ]) continue;

                    BiomeType biome = biomes[localX, localZ];

                    float treeProbability = biome switch
                    {
                        BiomeType.Forest => 0.55f,
                        BiomeType.Plains => 0.10f,
                        BiomeType.Hills  => 0.18f,
                        _                => 0f,
                    };

                    if (chance > treeProbability) continue;

                    int treeBase   = heights[localX, localZ] + 1;
                    int localBaseY = treeBase - worldY;

                    int variantIdx = (int)(Hash2Df(cellWX + 5, cellWZ + 5, _seed + 210) * _treeVariants.Length);
                    variantIdx = Math.Clamp(variantIdx, 0, _treeVariants.Length - 1);

                    PlaceTreeVariant(blocks, localX, localBaseY, localZ, variantIdx, chunkSize);
                }
            }
        }

        private void PlaceTreeVariant(byte[,,] blocks, int lx, int ly, int lz, int variantIdx, int size)
        {
            ref readonly var variant = ref _treeVariants[variantIdx];

            foreach (var (dx, dy, dz) in variant.Trunk)
            {
                int bx = lx + dx, by = ly + dy, bz = lz + dz;
                if (InBounds(bx, by, bz, size))
                    blocks[bx, by, bz] = BlockType.Wood;
            }

            foreach (var (dx, dy, dz) in variant.Branches)
            {
                int bx = lx + dx, by = ly + dy, bz = lz + dz;
                if (InBounds(bx, by, bz, size))
                    blocks[bx, by, bz] = BlockType.Wood;
            }

            foreach (var (tx, ty, tz) in variant.Tips)
            {
                foreach (var (cx, cy, cz) in _leafCluster)
                {
                    int bx = lx + tx + cx;
                    int by = ly + ty + cy;
                    int bz = lz + tz + cz;

                    if (!InBounds(bx, by, bz, size)) continue;
                    if (blocks[bx, by, bz] == BlockType.Wood) continue;
                    blocks[bx, by, bz] = BlockType.Leaves;
                }
            }
        }

        private static bool InBounds(int x, int y, int z, int size) =>
            x >= 0 && x < size && y >= 0 && y < size && z >= 0 && z < size;

        // ============================================================
        //  LOD SIMPLIFICADO
        // ============================================================

        /// <summary>
        /// Genera un chunk LOD simplificado.
        /// 
        /// <paramref name="simplificationLevel"/> controla:
        ///   0 → surface completa + todos los árboles (igual que nivel 1 original)
        ///   1 → surface + árboles cada 2 celdas
        ///   2 → surface + árboles cada 4 celdas
        ///      (el bloque-stride del mesher lo aplica SimpleLowPolyMesher)
        ///   3 → surface sin árboles
        /// </summary>
        public byte[,,] GenerateLowPolyChunk(
            int chunkX, int chunkY, int chunkZ,
            int chunkSize,
            int simplificationLevel = 0)
        {
            byte[,,] blocks = new byte[chunkSize, chunkSize, chunkSize];

            int worldX = chunkX * chunkSize;
            int worldY = chunkY * chunkSize;
            int worldZ = chunkZ * chunkSize;

            int[,] heights      = new int[chunkSize, chunkSize];
            BiomeType[,] biomes = new BiomeType[chunkSize, chunkSize];
            bool[,] riverMask   = new bool[chunkSize, chunkSize];

            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    float wx = worldX + bx;
                    float wz = worldZ + bz;
                    biomes[bx, bz]    = GetBiome(wx, wz);
                    heights[bx, bz]   = GetTerrainHeight(wx, wz, biomes[bx, bz], out bool isRiver);
                    riverMask[bx, bz] = isRiver;
                }
            }

            // Número de capas de suelo visibles debajo de la superficie.
            // A mayor simplificación generamos menos capas (el mesher ya va a saltar bloques).
            int simplifiedCheck = simplificationLevel switch
            {
                0 => 20,
                1 => 10,
                _ => 5,
            };

            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    int terrainHeight = heights[bx, bz];
                    BiomeType biome   = biomes[bx, bz];
                    bool isRiver      = riverMask[bx, bz];

                    for (int by = 0; by < chunkSize; by++)
                    {
                        int wy = worldY + by;
                        if (wy >= terrainHeight - simplifiedCheck && wy <= terrainHeight)
                        {
                            blocks[bx, by, bz] = wy == terrainHeight
                                ? GetSurfaceBlock(biome, isRiver)
                                : BlockType.Dirt;
                        }
                        else if (wy <= SeaLevel && wy > terrainHeight)
                        {
                            blocks[bx, by, bz] = BlockType.Water;
                        }
                    }
                }
            }

            // Árboles: siempre pasamos el mismo simplificationLevel
            PlaceTrees(blocks, worldX, worldY, worldZ, chunkSize,
                       heights, biomes, riverMask,
                       simplificationLevel: simplificationLevel);

            return blocks;
        }

        private byte GetSurfaceBlock(BiomeType biome, bool isRiver)
        {
            if (isRiver) return BlockType.Sand;
            return biome switch
            {
                BiomeType.SnowPeaks => BlockType.Snow,
                BiomeType.Mountains => BlockType.Stone,
                _                   => BlockType.Grass,
            };
        }

        public int[,] GenerateVeryLowPolyChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            int[,] heightMap = new int[chunkSize, chunkSize];
            int worldX = chunkX * chunkSize;
            int worldZ = chunkZ * chunkSize;

            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    float wx = worldX + bx;
                    float wz = worldZ + bz;
                    BiomeType biome = GetBiome(wx, wz);
                    heightMap[bx, bz] = GetTerrainHeight(wx, wz, biome, out _);
                }
            }

            return heightMap;
        }

        // ============================================================
        //  RUIDO PERLIN 2D / 3D
        // ============================================================

        private float Perlin2D(float x, float y)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;
            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);
            float u = Fade(x), v = Fade(y);
            int a  = _perm[X] + Y,     aa = _perm[a],     ab = _perm[a + 1];
            int b  = _perm[X + 1] + Y, ba = _perm[b],     bb = _perm[b + 1];
            return Lerp(Lerp(Grad2(aa, x, y),     Grad2(ba, x-1, y),     u),
                        Lerp(Grad2(ab, x, y-1),   Grad2(bb, x-1, y-1),   u), v);
        }

        private float Perlin3D(float x, float y, float z)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;
            int Z = (int)Math.Floor(z) & 255;
            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);
            z -= (float)Math.Floor(z);
            float u = Fade(x), v = Fade(y), w = Fade(z);
            int a  = _perm[X]+Y;   int aa = _perm[a]+Z;   int ab = _perm[a+1]+Z;
            int b  = _perm[X+1]+Y; int ba = _perm[b]+Z;   int bb = _perm[b+1]+Z;
            return Lerp(
                Lerp(Lerp(Grad3(aa,   x,   y,   z),   Grad3(ba,   x-1,y,  z),   u),
                     Lerp(Grad3(ab,   x,   y-1, z),   Grad3(bb,   x-1,y-1,z),   u), v),
                Lerp(Lerp(Grad3(aa+1, x,   y,   z-1), Grad3(ba+1, x-1,y,  z-1), u),
                     Lerp(Grad3(ab+1, x,   y-1, z-1), Grad3(bb+1, x-1,y-1,z-1), u), v), w);
        }

        private float OctaveNoise2D(float x, float z, float scale, int octaves, float persistence, int seed)
        {
            float total = 0f, frequency = 1f / scale, amplitude = 1f, maxVal = 0f;
            float ox = (seed * 0.13f) % 1000f;
            float oz = (seed * 0.17f) % 1000f;
            for (int i = 0; i < octaves; i++)
            {
                total    += Perlin2D((x + ox) * frequency, (z + oz) * frequency) * amplitude;
                maxVal   += amplitude;
                amplitude *= persistence;
                frequency *= 2f;
            }
            return total / maxVal;
        }

        private float OctaveNoise3D(float x, float y, float z, float scale, int octaves, float persistence, int seed)
        {
            float total = 0f, frequency = 1f / scale, amplitude = 1f, maxVal = 0f;
            float ox = (seed * 0.13f) % 1000f;
            float oy = (seed * 0.19f) % 1000f;
            float oz = (seed * 0.17f) % 1000f;
            for (int i = 0; i < octaves; i++)
            {
                total    += Perlin3D((x+ox)*frequency, (y+oy)*frequency, (z+oz)*frequency) * amplitude;
                maxVal   += amplitude;
                amplitude *= persistence;
                frequency *= 2f;
            }
            return total / maxVal;
        }

        private float RidgeNoise2D(float x, float z, float scale, int octaves, int seed)
        {
            float total = 0f, frequency = 1f / scale, amplitude = 1f, maxVal = 0f;
            float ox = (seed * 0.11f) % 1000f;
            float oz = (seed * 0.23f) % 1000f;
            for (int i = 0; i < octaves; i++)
            {
                float n = Perlin2D((x + ox) * frequency, (z + oz) * frequency);
                n = 1f - Math.Abs(n);
                n *= n;
                total    += n * amplitude;
                maxVal   += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return total / maxVal;
        }

        // ============================================================
        //  UTILIDADES
        // ============================================================

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0 + 0.0001f), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private float Grad2(int hash, float x, float y)
        {
            int h = _perm[hash & 255] & 3;
            float u = h < 2 ? x : y;
            float v = h < 2 ? y : x;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        private float Grad3(int hash, float x, float y, float z)
        {
            int h = _perm[hash & 255] & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        private static float Hash2Df(int x, int z, int seed)
        {
            int h = unchecked(seed ^ (x * 374761393) ^ (z * 668265263));
            h = unchecked((h ^ (h >> 13)) * 1274126177);
            h ^= h >> 16;
            return (float)((uint)h) / uint.MaxValue;
        }
    }
}