using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Generador procedural de mundo con montañas, bosques y biomas.
    /// 
    /// CARACTERÍSTICAS:
    /// - Ruido Perlin multicapa (octavas) para terreno variado
    /// - Biomas: llanuras, colinas, montañas, nieve
    /// - Cuevas procedurales (ruido 3D)
    /// - Árboles generados proceduralmente
    /// - Capas geológicas realistas (piedra, tierra, pasto, nieve)
    /// - CACHE de bloques globales para evitar regeneración
    /// </summary>
    public class WorldGenerator
    {
        private readonly int _seed;

        // Configuración de altura
        private const int SeaLevel    = 10;
        private const int MaxHeight   = 120;
        private const int MinHeight   = 16;

        // Permutation table para Perlin noise (clásico de Ken Perlin)
        private readonly int[] _perm = new int[512];

        // CACHE de bloques generados (thread-safe)
        private readonly Dictionary<(int chunkX, int chunkY, int chunkZ), byte[,,]> _blockCache = new();
        private readonly object _cacheLock = new object();

        public WorldGenerator(int seed = 12345, float scale = 30f, int oceanLevel = 35)
        {
            _seed = seed;
            InitPermTable();
        }

        // ============================================================
        //  INICIALIZACIÓN
        // ============================================================

        private void InitPermTable()
        {
            // Tabla base de permutaciones (0..255)
            int[] p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;

            // Fisher-Yates shuffle con seed
            var rng = new Random(_seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }

            // Duplicar para evitar wrapping
            for (int i = 0; i < 512; i++)
                _perm[i] = p[i & 255];
        }

        // ============================================================
        //  GENERACIÓN DE CHUNK
        // ============================================================

        /// <summary>
        /// Obtiene bloques del cache, o genera y cachea si no existen.
        /// THREAD-SAFE: genera en paralelo sin duplicar trabajo.
        /// </summary>
        public byte[,,] GetOrGenerateChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            var key = (chunkX, chunkY, chunkZ);

            lock (_cacheLock)
            {
                if (_blockCache.TryGetValue(key, out var cached))
                    return cached;
            }

            // Generar (fuera del lock para evitar bloqueo)
            byte[,,] blocks = GenerateChunk(chunkX, chunkY, chunkZ, chunkSize);

            lock (_cacheLock)
            {
                // Doble-check: otro thread pudo haber generado mientras esperaba el lock
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

            // Pre-calcular la altura del terreno para cada columna (X,Z) del chunk
            int[,] heights = new int[chunkSize, chunkSize];
            BiomeType[,] biomes = new BiomeType[chunkSize, chunkSize];

            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    float wx = worldX + bx;
                    float wz = worldZ + bz;

                    biomes[bx, bz]  = GetBiome(wx, wz);
                    heights[bx, bz] = GetTerrainHeight(wx, wz, biomes[bx, bz]);
                }
            }

            // Llenar bloques
            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    int terrainHeight = heights[bx, bz];
                    BiomeType biome   = biomes[bx, bz];

                    for (int by = 0; by < chunkSize; by++)
                    {
                        int wy = worldY + by;

                        blocks[bx, by, bz] = GetBlockAt(
                            worldX + bx, wy, worldZ + bz,
                            terrainHeight, biome);
                    }
                }
            }

            // Generar árboles encima del terreno (solo si parte del chunk es cercana a la superficie)
            PlaceTrees(blocks, worldX, worldY, worldZ, chunkSize, heights, biomes);

            return blocks;
        }

        // ============================================================
        //  BIOMAS
        // ============================================================

        private enum BiomeType { Plains, Hills, Mountains, SnowPeaks, Forest }

        /// <summary>
        /// Determina el bioma en función de dos capas de ruido (temperatura y humedad).
        /// </summary>
        private BiomeType GetBiome(float wx, float wz)
        {
            // Ruido de "temperatura" (escala grande)
            float temp     = OctaveNoise2D(wx, wz, scale: 400f, octaves: 2, persistence: 0.5f, seed: _seed);
            // Ruido de "altitud / rugosidad"
            float roughness = OctaveNoise2D(wx, wz, scale: 300f, octaves: 2, persistence: 0.5f, seed: _seed + 1);

            // Normalizar a [0,1]
            temp      = (temp      + 1f) * 0.5f;
            roughness = (roughness + 1f) * 0.5f;

            if (roughness > 0.72f) return temp < 0.35f ? BiomeType.SnowPeaks : BiomeType.Mountains;
            if (roughness > 0.52f) return BiomeType.Hills;
            if (temp > 0.55f)      return BiomeType.Forest;
            return BiomeType.Plains;
        }

        // ============================================================
        //  ALTURA DEL TERRENO
        // ============================================================

        private int GetTerrainHeight(float wx, float wz, BiomeType biome)
        {
            float height;

            switch (biome)
            {
                case BiomeType.Plains:
                    // Llanuras suaves, poca variación
                    height = OctaveNoise2D(wx, wz, scale: 120f, octaves: 3, persistence: 0.4f, seed: _seed + 10);
                    height = SeaLevel + 4 + height * 8f;
                    break;

                case BiomeType.Hills:
                    // Colinas medianas
                    height = OctaveNoise2D(wx, wz, scale: 80f,  octaves: 4, persistence: 0.5f, seed: _seed + 20);
                    height = SeaLevel + 8 + height * 20f;
                    break;

                case BiomeType.Forest:
                    // Bosque: ligeramente más alto que llanuras
                    height = OctaveNoise2D(wx, wz, scale: 100f, octaves: 4, persistence: 0.45f, seed: _seed + 30);
                    height = SeaLevel + 6 + height * 12f;
                    break;

                case BiomeType.Mountains:
                    // Montañas: usar ruido de potencia para picos más pronunciados
                    float baseM = OctaveNoise2D(wx, wz, scale: 60f, octaves: 6, persistence: 0.55f, seed: _seed + 40);
                    float ridge = RidgeNoise2D(wx, wz, scale: 50f, octaves: 4, seed: _seed + 41);
                    height = SeaLevel + 20 + baseM * 35f + ridge * 25f;
                    break;

                case BiomeType.SnowPeaks:
                    // Picos nevados: muy altos
                    float baseS  = OctaveNoise2D(wx, wz, scale: 50f, octaves: 6, persistence: 0.6f, seed: _seed + 50);
                    float ridgeS = RidgeNoise2D(wx, wz, scale: 40f, octaves: 5, seed: _seed + 51);
                    height = SeaLevel + 40 + baseS * 40f + ridgeS * 30f;
                    break;

                default:
                    height = SeaLevel;
                    break;
            }

            return (int)Math.Clamp(height, MinHeight, MaxHeight);
        }

        // ============================================================
        //  TIPO DE BLOQUE
        // ============================================================

        private byte GetBlockAt(int wx, int wy, int wz, int terrainHeight, BiomeType biome)
        {
            // Por encima del terreno: aire
            if (wy > terrainHeight)
                return BlockType.Air;

            // Cuevas (ruido 3D)
            if (wy > 2 && wy < terrainHeight - 2)
            {
                float cave = OctaveNoise3D(wx, wy, wz, scale: 20f, octaves: 2, persistence: 0.5f, seed: _seed + 99);
                if (cave > 0.55f)
                    return BlockType.Air;
            }

            // Capa de superficie según bioma
            if (wy == terrainHeight)
            {
                return biome switch
                {
                    BiomeType.Mountains  => BlockType.Stone,
                    _                    => BlockType.Grass,
                };
            }

            // Capa de subsuperficie (tierra)
            int dirtDepth = biome == BiomeType.Mountains || biome == BiomeType.SnowPeaks ? 1 : 3;
            if (wy >= terrainHeight - dirtDepth)
            {
                return biome switch
                {
                    BiomeType.Mountains  => BlockType.Stone,
                    BiomeType.SnowPeaks  => BlockType.Stone,
                    _                    => BlockType.Dirt,
                };
            }

            // Capa de piedra
            if (wy < terrainHeight - dirtDepth)
            {
                // Venas de minerales ocasionales
                return BlockType.Stone;
            }

            return BlockType.Stone;
        }

        // ============================================================
        //  ÁRBOLES
        // ============================================================

        /// <summary>
        /// Siembra árboles en posiciones pseudoaleatorias dentro del chunk.
        /// Solo en biomas Plains y Forest, y solo si el bloque de superficie está en este chunk.
        /// </summary>
        private void PlaceTrees(
            byte[,,] blocks,
            int worldX, int worldY, int worldZ,
            int chunkSize,
            int[,] heights,
            BiomeType[,] biomes)
        {
            // Densidad según bioma
            // Usamos una cuadrícula de 5x5 con offset aleatorio para evitar patrones regulares
            const int gridSize = 5;

            for (int gx = 0; gx < chunkSize / gridSize + 1; gx++)
            {
                for (int gz = 0; gz < chunkSize / gridSize + 1; gz++)
                {
                    // Hash determinístico para esta celda
                    int cellWorldX = worldX + gx * gridSize;
                    int cellWorldZ = worldZ + gz * gridSize;

                    float chance = Hash2Df(cellWorldX, cellWorldZ, _seed + 200);

                    // Offset dentro de la celda
                    int offX = (int)(Hash2Df(cellWorldX, cellWorldZ, _seed + 201) * (gridSize - 1));
                    int offZ = (int)(Hash2Df(cellWorldX, cellWorldZ, _seed + 202) * (gridSize - 1));

                    int localX = gx * gridSize + offX;
                    int localZ = gz * gridSize + offZ;

                    if (localX >= chunkSize || localZ >= chunkSize) continue;

                    BiomeType biome = biomes[localX, localZ];

                    // Probabilidad de árbol según bioma
                    float treeProbability = biome switch
                    {
                        BiomeType.Forest => 0.55f,
                        BiomeType.Plains => 0.12f,
                        BiomeType.Hills  => 0.20f,
                        _                => 0f,
                    };

                    if (chance > treeProbability) continue;

                    // Colocar tronco + copa en coordenadas locales del chunk
                    int treeBase = heights[localX, localZ] + 1;  // encima del suelo
                    int treeHeight = 4 + (int)(Hash2Df(cellWorldX + 1, cellWorldZ + 1, _seed + 203) * 3);

                    PlaceTree(blocks, localX, treeBase - worldY, localZ, treeHeight, chunkSize);
                }
            }
        }

        private void PlaceTree(byte[,,] blocks, int lx, int ly, int lz, int height, int size)
        {
            // Tronco
            for (int i = 0; i < height; i++)
            {
                int by = ly + i;
                if (by >= 0 && by < size)
                    blocks[lx, by, lz] = BlockType.Wood;
            }

            // Copa esférica encima del tronco
            int crownCenter = ly + height;
            int crownRadius = 2;

            for (int dx = -crownRadius; dx <= crownRadius; dx++)
            {
                for (int dy = -1; dy <= crownRadius; dy++)
                {
                    for (int dz = -crownRadius; dz <= crownRadius; dz++)
                    {
                        if (dx * dx + dy * dy + dz * dz > crownRadius * crownRadius + 1)
                            continue;

                        int bx = lx + dx;
                        int by = crownCenter + dy;
                        int bz = lz + dz;

                        if (bx < 0 || bx >= size || by < 0 || by >= size || bz < 0 || bz >= size)
                            continue;

                        // No reemplazar tronco
                        if (blocks[bx, by, bz] == BlockType.Wood) continue;

                        blocks[bx, by, bz] = BlockType.Grass;
                    }
                }
            }
        }

        // ============================================================
        //  RUIDO PERLIN 2D / 3D
        // ============================================================

        /// <summary>
        /// Ruido Perlin clásico 2D (implementación de Ken Perlin).
        /// Retorna valores en aproximadamente [-1, 1].
        /// </summary>
        private float Perlin2D(float x, float y)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;

            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);

            float u = Fade(x);
            float v = Fade(y);

            int a  = _perm[X]     + Y;
            int aa = _perm[a];
            int ab = _perm[a + 1];
            int b  = _perm[X + 1] + Y;
            int ba = _perm[b];
            int bb = _perm[b + 1];

            return Lerp(
                Lerp(Grad2(aa, x,     y),     Grad2(ba, x - 1, y),     u),
                Lerp(Grad2(ab, x,     y - 1), Grad2(bb, x - 1, y - 1), u),
                v);
        }

        /// <summary>
        /// Ruido Perlin clásico 3D.
        /// </summary>
        private float Perlin3D(float x, float y, float z)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;
            int Z = (int)Math.Floor(z) & 255;

            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);
            z -= (float)Math.Floor(z);

            float u = Fade(x);
            float v = Fade(y);
            float w = Fade(z);

            int a  = _perm[X]     + Y; int aa = _perm[a] + Z; int ab = _perm[a + 1] + Z;
            int b  = _perm[X + 1] + Y; int ba = _perm[b] + Z; int bb = _perm[b + 1] + Z;

            return Lerp(
                Lerp(
                    Lerp(Grad3(aa,     x,     y,     z    ), Grad3(ba,     x - 1, y,     z    ), u),
                    Lerp(Grad3(ab,     x,     y - 1, z    ), Grad3(bb,     x - 1, y - 1, z    ), u),
                    v),
                Lerp(
                    Lerp(Grad3(aa + 1, x,     y,     z - 1), Grad3(ba + 1, x - 1, y,     z - 1), u),
                    Lerp(Grad3(ab + 1, x,     y - 1, z - 1), Grad3(bb + 1, x - 1, y - 1, z - 1), u),
                    v),
                w);
        }

        /// <summary>Octavas de ruido Perlin 2D para mayor detalle.</summary>
        private float OctaveNoise2D(float x, float z, float scale, int octaves, float persistence, int seed)
        {
            float total     = 0f;
            float frequency = 1f / scale;
            float amplitude = 1f;
            float maxValue  = 0f;

            // Desplazamiento por seed para independencia entre llamadas
            float ox = (seed * 0.13f) % 1000f;
            float oz = (seed * 0.17f) % 1000f;

            for (int i = 0; i < octaves; i++)
            {
                total    += Perlin2D((x + ox) * frequency, (z + oz) * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= 2f;
            }

            return total / maxValue;
        }

        /// <summary>Octavas de ruido Perlin 3D.</summary>
        private float OctaveNoise3D(float x, float y, float z, float scale, int octaves, float persistence, int seed)
        {
            float total     = 0f;
            float frequency = 1f / scale;
            float amplitude = 1f;
            float maxValue  = 0f;

            float ox = (seed * 0.13f) % 1000f;
            float oy = (seed * 0.19f) % 1000f;
            float oz = (seed * 0.17f) % 1000f;

            for (int i = 0; i < octaves; i++)
            {
                total    += Perlin3D((x + ox) * frequency, (y + oy) * frequency, (z + oz) * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= 2f;
            }

            return total / maxValue;
        }

        /// <summary>
        /// Ridge noise: invierte el ruido para crear crestas de montaña pronunciadas.
        /// </summary>
        private float RidgeNoise2D(float x, float z, float scale, int octaves, int seed)
        {
            float total     = 0f;
            float frequency = 1f / scale;
            float amplitude = 1f;
            float maxValue  = 0f;

            float ox = (seed * 0.11f) % 1000f;
            float oz = (seed * 0.23f) % 1000f;

            for (int i = 0; i < octaves; i++)
            {
                float n = Perlin2D((x + ox) * frequency, (z + oz) * frequency);
                n = 1f - Math.Abs(n);   // Invertir para crear crestas
                n *= n;                 // Suavizar las crestas
                total    += n * amplitude;
                maxValue += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return total / maxValue;
        }

        // ============================================================
        //  UTILIDADES DE RUIDO
        // ============================================================

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

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

        /// <summary>
        /// Genera un chunk LOD simplificado: SOLO la superficie (un bloque de altura).
        /// No genera bajo tierra en absoluto — es completamente aire debajo.
        /// Mucho más ligero para chunks lejanos.
        /// </summary>
        public byte[,,] GenerateLowPolyChunk(int chunkX, int chunkY, int chunkZ, int chunkSize, int depthLayers = 1)
        {
            byte[,,] blocks = new byte[chunkSize, chunkSize, chunkSize];
            // Todos los bloques comienzan como Air (default)

            int worldX = chunkX * chunkSize;
            int worldY = chunkY * chunkSize;
            int worldZ = chunkZ * chunkSize;

            // Pre-calcular la altura del terreno para cada columna
            int[,] heights = new int[chunkSize, chunkSize];
            BiomeType[,] biomes = new BiomeType[chunkSize, chunkSize];

            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    float wx = worldX + bx;
                    float wz = worldZ + bz;

                    biomes[bx, bz] = GetBiome(wx, wz);
                    heights[bx, bz] = GetTerrainHeight(wx, wz, biomes[bx, bz]);
                }
            }

            // SOLO generar la superficie (1 layer) —sin underground
            for (int bx = 0; bx < chunkSize; bx++)
            {
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    int terrainHeight = heights[bx, bz];
                    BiomeType biome = biomes[bx, bz];

                    for (int by = 0; by < chunkSize; by++)
                    {
                        int wy = worldY + by;

                        // Solo generar bloque EN la superficie
                        if (wy >= Math.Max(0, terrainHeight - 8) && wy <= terrainHeight)
                        {
                            blocks[bx, by, bz] = biome switch
                            {
                                BiomeType.Mountains => BlockType.Stone,
                                BiomeType.SnowPeaks => BlockType.Stone,
                                _ => BlockType.Grass,
                            };
                        }
                    }
                }
            }
            
            // Generar árboles encima del terreno (solo si parte del chunk es cercana a la superficie)
            PlaceTrees(blocks, worldX, worldY, worldZ, chunkSize, heights, biomes);

            return blocks;
        }

        /// <summary>
        /// Genera solo el heightmap (ULTRA simplificado) para VeryLowPolyChunk.
        /// Retorna un array 2D de alturas, sin datos de bloques 3D.
        /// Ultra eficiente para chunks muy lejanos.
        /// </summary>
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
                    int height = GetTerrainHeight(wx, wz, biome);

                    heightMap[bx, bz] = height;
                }
            }

            return heightMap;
        }

        /// <summary>Hash 2D a float [0,1] para posiciones de árboles.</summary>
        private static float Hash2Df(int x, int z, int seed)
        {
            int h = unchecked(seed ^ (x * 374761393) ^ (z * 668265263));
            h = unchecked((h ^ (h >> 13)) * 1274126177);
            h ^= h >> 16;
            return (float)((uint)h) / uint.MaxValue;
        }
    }
}