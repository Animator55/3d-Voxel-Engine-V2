using System;
using System.Buffers;
using System.Collections.Generic;

namespace game
{
    public class WorldGenerator
    {
        private readonly int _seed;
        private const int SeaLevel = 22;
        private const int BaseHeight = 22;
        private const int MaxHeight = 160;
        private const int MinHeight = 2;
        private const float CaveThreshold = 0.62f;

        // Altura máxima hasta la que el río reemplaza bloques.
        // Por encima de este valor el cauce no existe (montaña alta).
        private const int RiverMaxHeight = 120;

        private readonly int[] _perm = new int[512];

        private const int BLOCK_CACHE_CAPACITY = 512;
        private readonly LruCache<(int, int, int), byte[,,]> _blockCache;
        private readonly object _cacheLock = new object();

        private readonly LruCache<(int, int), int> _heightCache;
        private readonly object _heightCacheLock = new object();
        private const int HEIGHT_CACHE_CAPACITY = 4096;

        public WorldGenerator(int seed = 12345)
        {
            _seed = seed;
            _blockCache = new LruCache<(int, int, int), byte[,,]>(BLOCK_CACHE_CAPACITY);
            _heightCache = new LruCache<(int, int), int>(HEIGHT_CACHE_CAPACITY);
            InitPermTable();
        }

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
            for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
        }

        // ════════════════════════════════════════════════════════════════════
        // Cache & entry points
        // ════════════════════════════════════════════════════════════════════

        public byte[,,] GetOrGenerateChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            var key = (chunkX, chunkY, chunkZ);
            lock (_cacheLock) { if (_blockCache.TryGet(key, out var c)) return c; }
            byte[,,] blocks = GenerateChunk(chunkX, chunkY, chunkZ, chunkSize);
            lock (_cacheLock) { if (!_blockCache.TryGet(key, out _)) _blockCache.Put(key, blocks); }
            return blocks;
        }

        public byte[,,] GenerateChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            byte[,,] blocks = new byte[chunkSize, chunkSize, chunkSize];
            int worldX = chunkX * chunkSize, worldY = chunkY * chunkSize, worldZ = chunkZ * chunkSize;
            int area = chunkSize * chunkSize;

            int[] heightsFlat = ArrayPool<int>.Shared.Rent(area);
            float[] weightsFlat = ArrayPool<float>.Shared.Rent(area * BIOME_COUNT);
            bool[] riverFlat = ArrayPool<bool>.Shared.Rent(area);

            try
            {
                for (int bx = 0; bx < chunkSize; bx++)
                    for (int bz = 0; bz < chunkSize; bz++)
                    {
                        int idx = bx * chunkSize + bz;
                        float wx = worldX + bx, wz = worldZ + bz;
                        SoftBiomeWeights(wx, wz,
                            out float wPl, out float wFo, out float wTa,
                            out float wDe, out float wMo, out float wOc);
                        weightsFlat[idx * BIOME_COUNT + 0] = wPl; weightsFlat[idx * BIOME_COUNT + 1] = wFo;
                        weightsFlat[idx * BIOME_COUNT + 2] = wTa; weightsFlat[idx * BIOME_COUNT + 3] = wDe;
                        weightsFlat[idx * BIOME_COUNT + 4] = wMo; weightsFlat[idx * BIOME_COUNT + 5] = wOc;
                        heightsFlat[idx] = GetTerrainHeight(wx, wz, wPl, wFo, wTa, wDe, wMo, wOc, out bool ir);
                        riverFlat[idx] = ir;
                    }

                for (int bx = 0; bx < chunkSize; bx++)
                    for (int bz = 0; bz < chunkSize; bz++)
                    {
                        int idx = bx * chunkSize + bz;
                        int h = heightsFlat[idx];
                        bool ir = riverFlat[idx];
                        int base_ = idx * BIOME_COUNT;
                        BiomeType biome = DominantBiome(
                            weightsFlat[base_ + 0], weightsFlat[base_ + 1], weightsFlat[base_ + 2],
                            weightsFlat[base_ + 3], weightsFlat[base_ + 4], weightsFlat[base_ + 5]);
                        for (int by = 0; by < chunkSize; by++)
                            blocks[bx, by, bz] = GetBlockAt(worldX + bx, worldY + by, worldZ + bz, h, biome, ir);
                    }

                // Pintar el cauce del río: reemplaza cualquier bloque sólido
                // por Water en las columnas donde el noise de río es activo,
                // desde la base del terreno hasta min(terrainH, RiverMaxHeight).
                PaintRiverBeds(blocks, worldX, worldY, worldZ, chunkSize, heightsFlat, riverFlat);

                PlaceTrees(blocks, worldX, worldY, worldZ, chunkSize, heightsFlat, weightsFlat, riverFlat, 0);

                var structures = StructurePlacer.GetStructuresForChunk(
                    chunkX, chunkY, chunkZ, chunkSize, _seed, GetSurfaceHeight);
                foreach (var s in structures)
                    StructurePlacer.Apply(blocks, s, chunkX, chunkY, chunkZ, chunkSize, null);
            }
            finally
            {
                ArrayPool<int>.Shared.Return(heightsFlat);
                ArrayPool<float>.Shared.Return(weightsFlat);
                ArrayPool<bool>.Shared.Return(riverFlat);
            }
            return blocks;
        }
        // ════════════════════════════════════════════════════════════════════
        // River bed painter  (refactorizado)
        // ════════════════════════════════════════════════════════════════════
        //
        // Reglas nuevas:
        //   1. Solo pinta el bloque de SUPERFICIE (el primero sólido visto
        //      desde arriba), no rellena la columna entera.
        //      → Las cuevas quedan intactas; nunca hay agua enterrada.
        //
        //   2. La superficie real se busca bajando desde terrainH hasta
        //      encontrar el primer bloque sólido que esté expuesto al aire.
        //      Esto soporta terrenos con voladizos o capas de grava.
        //
        //   3. La generación se corta cuando surfaceY > RiverMaxHeight,
        //      eliminando agua en cimas de montañas.
        //      En laderas el río sí aparece (y crea cascadas naturales al
        //      bajar de columna en columna).
        //
        //   4. RIVER_THRESHOLD se baja a 0.92f para que el cauce sea visible.
        //      El valor anterior de 0.99f generaba apenas unos pocos píxeles.
        private const float RIVER_THRESHOLD = 0.98f;
        private const float WaterfallEdgeMin = 0.08f;
        private const float WaterfallMountainCenter = 0.62f;
        private const float RiverOnMountainCenter = 0.70f;

        private void PaintRiverBeds(byte[,,] blocks, int worldX, int worldY, int worldZ,
            int chunkSize, int[] heightsFlat, bool[] riverFlat)
        {
            for (int bx = 0; bx < chunkSize; bx++)
                for (int bz = 0; bz < chunkSize; bz++)
                {
                    float wx = worldX + bx, wz = worldZ + bz;
                    if (GetRiverRaw(wx, wz) <= RIVER_THRESHOLD) continue;

                    int idx = bx * chunkSize + bz;
                    int h = heightsFlat[idx];

                    if (h < SeaLevel) continue;

                    float mCenter = GetMountainWeight(wx, wz);
                    float mN = GetMountainWeight(wx, wz - 1);
                    float mS = GetMountainWeight(wx, wz + 1);
                    float mE = GetMountainWeight(wx + 1, wz);
                    float mW = GetMountainWeight(wx - 1, wz);
                    float maxNeighborM = Math.Max(Math.Max(mN, mS), Math.Max(mE, mW));

                    bool isWaterfallEdge = mCenter <= WaterfallMountainCenter && maxNeighborM >= WaterfallEdgeMin;
                    bool isRiverOnMountain = mCenter <= RiverOnMountainCenter && maxNeighborM >= WaterfallEdgeMin;

                    if (isWaterfallEdge)
                    {
                        int neighborH = Math.Max(
                            Math.Max(
                                GetTerrainHeightForColumn(wx, wz - 1),
                                GetTerrainHeightForColumn(wx, wz + 1)),
                            Math.Max(
                                GetTerrainHeightForColumn(wx + 1, wz),
                                GetTerrainHeightForColumn(wx - 1, wz)));

                        int waterfallTop = Math.Min(neighborH, RiverMaxHeight);

                        int yMin = Math.Max(SeaLevel, worldY);
                        int yMax = Math.Min(waterfallTop, worldY + chunkSize - 1);
                        if (yMin > yMax) continue;

                        for (int wy = yMin; wy <= yMax; wy++)
                        {
                            int by = wy - worldY;
                            if (blocks[bx, by, bz] == BlockType.Air
                                || blocks[bx, by, bz] == BlockType.Stone
                                || blocks[bx, by, bz] == BlockType.Dirt)
                                blocks[bx, by, bz] = BlockType.Water;
                        }
                    }
                    else if (isRiverOnMountain)
                    {
                        if (h > RiverMaxHeight) continue;

                        int surfaceBy = h - worldY;
                        if (surfaceBy < 0 || surfaceBy >= chunkSize) continue;

                        byte b = blocks[bx, surfaceBy, bz];
                        if (b == BlockType.Stone || b == BlockType.Dirt)
                            blocks[bx, surfaceBy, bz] = BlockType.Water;
                    }
                }
        }

        // Altura del terreno para una columna adyacente — usa el cache existente.
        private int GetTerrainHeightForColumn(float wx, float wz)
        {
            SoftBiomeWeights(wx, wz, out float wPl, out float wFo, out float wTa,
                                     out float wDe, out float wMo, out float wOc);
            return GetTerrainHeight(wx, wz, wPl, wFo, wTa, wDe, wMo, wOc, out _);
        }

        // Extrae solo wMountains del sistema de biomas — mismo cálculo que
        // SoftBiomeWeights pero descartando los otros pesos. Sin cache porque
        // solo se llama 4 veces por columna y el noise es barato.
        private float GetMountainWeight(float wx, float wz)
        {
            float wpX = OctaveNoise2D(wx, wz, 250f, 2, 0.5f, _seed + 500) * 30f;
            float wpZ = OctaveNoise2D(wx, wz, 250f, 2, 0.5f, _seed + 501) * 30f;
            float wx2 = wx + wpX, wz2 = wz + wpZ;

            float rug = (OctaveNoise2D(wx2, wz2, 300f, 3, 0.5f, _seed + 2) + 1f) * 0.5f;
            float cont = (OctaveNoise2D(wx2, wz2, 900f, 2, 0.5f, _seed + 3) + 1f) * 0.7f;

            float wOcean = SmoothRange(1f - cont, 0.45f, 0.65f);
            float land = 1f - wOcean;

            return SmoothRange(rug, 0.50f, 0.70f) * land;
        }
        // ════════════════════════════════════════════════════════════════════
        // Biomes
        // ════════════════════════════════════════════════════════════════════

        private const int BIOME_COUNT = 6;
        private enum BiomeType { Plains, Forest, Taiga, Desert, Mountains, Ocean }

        private void SoftBiomeWeights(float wx, float wz,
            out float wPlains, out float wForest, out float wTaiga,
            out float wDesert, out float wMountains, out float wOcean)
        {
            float wpX = OctaveNoise2D(wx, wz, 250f, 2, 0.5f, _seed + 500) * 30f;
            float wpZ = OctaveNoise2D(wx, wz, 250f, 2, 0.5f, _seed + 501) * 30f;
            float wx2 = wx + wpX, wz2 = wz + wpZ;

            float temp = (OctaveNoise2D(wx2, wz2, 600f, 3, 0.5f, _seed) + 1f) * 0.5f;
            float hum = (OctaveNoise2D(wx2, wz2, 400f, 3, 0.5f, _seed + 1) + 1f) * 0.5f;
            float rug = (OctaveNoise2D(wx2, wz2, 300f, 3, 0.5f, _seed + 2) + 1f) * 0.5f;
            float cont = (OctaveNoise2D(wx2, wz2, 900f, 2, 0.5f, _seed + 3) + 1f) * 0.7f;

            wOcean = SmoothRange(1f - cont, 0.45f, 0.65f);
            float land = 1f - wOcean;

            wMountains = SmoothRange(rug, 0.50f, 0.70f) * land;
            float noMtn = 1f - SmoothRange(rug, 0.44f, 0.62f);

            wDesert = SmoothRange(temp, 0.62f, 0.80f) * SmoothRange(1f - hum, 0.55f, 0.75f) * noMtn * land;
            wTaiga = SmoothRange(1f - temp, 0.50f, 0.70f) * SmoothRange(hum, 0.40f, 0.60f) * noMtn * land;
            wForest = SmoothRange(temp, 0.38f, 0.58f) * SmoothRange(hum, 0.50f, 0.70f) * noMtn * land;

            float usedLand = wMountains + wDesert + wTaiga + wForest;
            wPlains = Math.Max(0f, land - usedLand);

            float total = wPlains + wForest + wTaiga + wDesert + wMountains + wOcean;
            if (total < 0.001f) { wPlains = 1f; wForest = wTaiga = wDesert = wMountains = wOcean = 0f; return; }
            float inv = 1f / total;
            wPlains *= inv; wForest *= inv; wTaiga *= inv; wDesert *= inv; wMountains *= inv; wOcean *= inv;
        }

        private static float SmoothRange(float x, float edge0, float edge1)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0 + 0.0001f), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static BiomeType DominantBiome(float wPl, float wFo, float wTa, float wDe, float wMo, float wOc)
        {
            BiomeType best = BiomeType.Plains; float bestW = wPl;
            if (wFo > bestW) { best = BiomeType.Forest; bestW = wFo; }
            if (wTa > bestW) { best = BiomeType.Taiga; bestW = wTa; }
            if (wDe > bestW) { best = BiomeType.Desert; bestW = wDe; }
            if (wMo > bestW) { best = BiomeType.Mountains; bestW = wMo; }
            if (wOc > bestW) best = BiomeType.Ocean;
            return best;
        }

        // ════════════════════════════════════════════════════════════════════
        // Terrain height
        // ════════════════════════════════════════════════════════════════════

        private int GetTerrainHeight(float wx, float wz,
            float wPlains, float wForest, float wTaiga, float wDesert, float wMountains, float wOcean,
            out bool isRiver)
        {
            isRiver = false;

            float plainsH = BaseHeight + 2f + OctaveNoise2D(wx, wz, 120f, 3, 0.40f, _seed + 10) * 8f;
            float forestH = BaseHeight + 4f + OctaveNoise2D(wx, wz, 100f, 4, 0.45f, _seed + 30) * 12f;
            float taigaH = BaseHeight + 6f + OctaveNoise2D(wx, wz, 90f, 4, 0.45f, _seed + 60) * 16f;
            float desertH = BaseHeight + 3f + OctaveNoise2D(wx, wz, 150f, 3, 0.35f, _seed + 70) * 10f
                                          + RidgeNoise2D(wx, wz, 120f, 2, _seed + 71) * 6f;
            float oceanH = SeaLevel - 8f + OctaveNoise2D(wx, wz, 200f, 2, 0.40f, _seed + 80) * 5f;

            float mBase = OctaveNoise2D(wx, wz, 80f, 6, 0.52f, _seed + 40);
            float mRidge = RidgeNoise2D(wx, wz, 55f, 5, _seed + 41);
            float mDetail = OctaveNoise2D(wx, wz, 22f, 4, 0.62f, _seed + 42) * 0.15f;
            float mNorm = (float)Math.Pow(Math.Clamp((mBase + 1f) * 0.5f, 0f, 1f), 1.6f);
            float mComb = mNorm * 0.52f + mRidge * (0.30f + mNorm * 0.28f) + mDetail;
            mComb = Math.Clamp(mComb, 0f, 1f);
            float mountainH = BaseHeight + 20f + mComb * 100f;

            float h = plainsH * wPlains + forestH * wForest + taigaH * wTaiga
                    + desertH * wDesert + mountainH * wMountains + oceanH * wOcean;

            // Ríos solo en tierra llana (depresión original del heightmap)
            float landWeight = wPlains + wForest + wTaiga;
            if (landWeight > 0.4f)
            {
                float ri = GetRiverInfluence(wx, wz, out _);
                if (ri > 0f)
                {
                    h = h * (1f - ri) + (SeaLevel - 3f) * ri;
                    if (ri > 0.2f) isRiver = true;
                }
            }

            return (int)Math.Clamp(h, MinHeight, MaxHeight);
        }

        // Versión simplificada para VLP
        private int GetTerrainHeightVLP(float wx, float wz)
        {
            SoftBiomeWeights(wx, wz, out float wPl, out float wFo, out float wTa,
                                     out float wDe, out float wMo, out float wOc);
            float plainsH = BaseHeight + 2f + OctaveNoise2D(wx, wz, 120f, 2, 0.40f, _seed + 10) * 8f;
            float forestH = BaseHeight + 4f + OctaveNoise2D(wx, wz, 100f, 2, 0.45f, _seed + 30) * 12f;
            float taigaH = BaseHeight + 6f + OctaveNoise2D(wx, wz, 90f, 2, 0.45f, _seed + 60) * 16f;
            float desertH = BaseHeight + 3f + OctaveNoise2D(wx, wz, 150f, 2, 0.35f, _seed + 70) * 10f
                                          + RidgeNoise2D(wx, wz, 120f, 2, _seed + 71) * 6f;
            float mBase = OctaveNoise2D(wx, wz, 80f, 3, 0.52f, _seed + 40);
            float mRidge = RidgeNoise2D(wx, wz, 55f, 3, _seed + 41);
            float mNorm = (float)Math.Pow(Math.Clamp((mBase + 1f) * 0.5f, 0f, 1f), 1.6f);
            float mComb = mNorm * 0.52f + mRidge * (0.30f + mNorm * 0.28f);
            float mountainH = BaseHeight + 20f + Math.Clamp(mComb, 0f, 1f) * 100f;
            float oceanH = SeaLevel - 8f + OctaveNoise2D(wx, wz, 200f, 2, 0.40f, _seed + 80) * 5f;
            float h = plainsH * wPl + forestH * wFo + taigaH * wTa + desertH * wDe + mountainH * wMo + oceanH * wOc;
            return (int)Math.Clamp(h, MinHeight, MaxHeight);
        }

        // ════════════════════════════════════════════════════════════════════
        // Rivers
        // ════════════════════════════════════════════════════════════════════

        private float GetRiverInfluence(float wx, float wz, out float depth01)
        {
            float rwX = OctaveNoise2D(wx, wz, 150f, 2, 0.5f, _seed + 600) * 30f;
            float rwZ = OctaveNoise2D(wx, wz, 150f, 2, 0.5f, _seed + 601) * 30f;
            float n = OctaveNoise2D(wx + rwX, wz + rwZ, 300f, 4, 0.5f, _seed + 700);
            n = (n + 1f) * 0.5f;
            float raw = 1f - Math.Abs(n * 2f - 1f);
            raw = (float)Math.Pow(raw, 3f);
            depth01 = raw;
            const float threshold = 0.55f;
            if (raw < threshold) return 0f;
            return Math.Clamp((raw - threshold) / (1f - threshold), 0f, 1f);
        }

        private float GetRiverRaw(float wx, float wz)
        {
            float rwX = OctaveNoise2D(wx, wz, 150f, 2, 0.5f, _seed + 600) * 30f;
            float rwZ = OctaveNoise2D(wx, wz, 150f, 2, 0.5f, _seed + 601) * 30f;
            float n = OctaveNoise2D(wx + rwX, wz + rwZ, 300f, 4, 0.5f, _seed + 700);
            n = (n + 1f) * 0.5f;
            float raw = 1f - Math.Abs(n * 2f - 1f);
            return (float)Math.Pow(raw, 3f);
        }

        // ════════════════════════════════════════════════════════════════════
        // Block selection
        // ════════════════════════════════════════════════════════════════════

        private byte GetBlockAt(int wx, int wy, int wz, int terrainH, BiomeType biome, bool isRiver)
        {
            if (wy > terrainH)
                return wy <= SeaLevel ? BlockType.Water : BlockType.Air;
            if (wy > 4 && wy < terrainH - 1)
            {
                float cave = (OctaveNoise3D(wx, wy, wz, 40f, 2, 0.5f, _seed + 99) + 1f) * 0.5f;
                if (cave > CaveThreshold) return BlockType.Air;
            }
            if (wy == terrainH) return GetSurfaceBlock(biome, isRiver, terrainH);
            if (wy >= terrainH - 3)
            {
                return biome switch
                {
                    BiomeType.Desert => BlockType.Sand,
                    BiomeType.Ocean => BlockType.Stone,
                    _ => terrainH <= SeaLevel ? BlockType.Sand : BlockType.Dirt
                };
            }
            return BlockType.Stone;
        }

        private byte GetSurfaceBlock(BiomeType biome, bool isRiver, int h)
        {
            if (isRiver) return BlockType.Sand;
            return biome switch
            {
                BiomeType.Ocean => h <= SeaLevel - 3 ? BlockType.Stone : BlockType.Sand,
                BiomeType.Desert => BlockType.Sand,
                BiomeType.Mountains => h > SeaLevel + 45 ? BlockType.Snow
                                     : h > SeaLevel + 10 ? BlockType.Stone
                                     : BlockType.Grass,
                BiomeType.Taiga => h > SeaLevel + 30 ? BlockType.Snow : BlockType.Grass,
                BiomeType.Plains => h <= SeaLevel ? BlockType.Sand : BlockType.Grass,
                BiomeType.Forest => h <= SeaLevel ? BlockType.Sand : BlockType.Grass,
                _ => BlockType.Grass
            };
        }

        public int GetSurfaceHeight(float wx, float wz)
        {
            var key = ((int)wx, (int)wz);
            lock (_heightCacheLock) { if (_heightCache.TryGet(key, out int ch)) return ch; }
            SoftBiomeWeights(wx, wz, out float wPl, out float wFo, out float wTa,
                                     out float wDe, out float wMo, out float wOc);
            int h = GetTerrainHeight(wx, wz, wPl, wFo, wTa, wDe, wMo, wOc, out _);
            lock (_heightCacheLock) { _heightCache.Put(key, h); }
            return h;
        }

        // ════════════════════════════════════════════════════════════════════
        // Trees
        // ════════════════════════════════════════════════════════════════════

        private static readonly (int, int, int)[] _trunk0 = { (0, 0, 0), (0, 1, 0), (0, 2, 0), (0, 3, 0), (0, 4, 0), (0, 5, 0) };
        private static readonly (int, int, int)[] _branches0 = { };
        private static readonly (int, int, int)[] _tips0 = { (0, 5, 0), (1, 5, 0), (-1, 5, 0), (0, 5, 1), (0, 5, -1), (0, 6, 0), (1, 6, 0), (-1, 6, 0), (0, 6, 1), (0, 6, -1) };
        private static readonly (int, int, int)[] _trunk1 = { (0, 0, 0), (0, 1, 0), (0, 2, 0), (0, 3, 0), (0, 4, 0), (0, 5, 0), (0, 6, 0) };
        private static readonly (int, int, int)[] _branches1 = { (1, 4, 0), (2, 4, 0), (3, 4, 0), (-1, 4, 0), (-2, 4, 0), (-3, 4, 0), (0, 4, 1), (0, 4, 2), (0, 4, 3), (0, 4, -1), (0, 4, -2), (0, 4, -3), (2, 5, 2), (-2, 5, 2), (2, 5, -2), (-2, 5, -2) };
        private static readonly (int, int, int)[] _tips1 = { (3, 4, 0), (-3, 4, 0), (0, 4, 3), (0, 4, -3), (2, 5, 2), (-2, 5, 2), (2, 5, -2), (-2, 5, -2), (1, 6, 0), (-1, 6, 0), (0, 6, 1), (0, 6, -1), (0, 6, 0) };
        private static readonly (int, int, int)[] _trunk2 = { (0, 0, 0), (0, 1, 0), (0, 2, 0), (1, 3, 0), (1, 4, 0), (2, 5, 0), (2, 6, 0), (2, 7, 1), (2, 8, 1) };
        private static readonly (int, int, int)[] _branches2 = { (-1, 3, 0), (-2, 4, 0), (-3, 4, -1), (3, 5, 0), (4, 6, 0), (5, 6, 1), (3, 7, 1), (4, 7, 1), (3, 8, 2) };
        private static readonly (int, int, int)[] _tips2 = { (-3, 4, -1), (5, 6, 1), (3, 8, 2), (2, 8, 2), (1, 8, 1), (3, 8, 0), (2, 9, 1) };
        private static readonly (int, int, int)[] _trunk3 = { (0, 0, 0), (0, 1, 0), (0, 2, 0), (0, 3, 0), (0, 4, 0), (0, 5, 0), (0, 6, 0), (0, 7, 0), (0, 8, 0), (0, 9, 0), (0, 10, 0) };
        private static readonly (int, int, int)[] _branches3 = { (1, 6, 0), (2, 6, 0), (3, 6, 1), (-1, 6, 0), (-2, 6, 0), (-3, 6, -1), (0, 6, 1), (0, 6, 2), (1, 6, 3), (0, 6, -1), (0, 6, -2), (1, 8, 0), (2, 8, 0), (-1, 8, 0), (-2, 8, 0), (0, 8, 1), (0, 8, -1), (1, 10, 0), (-1, 10, 0), (0, 10, 1), (0, 10, -1) };
        private static readonly (int, int, int)[] _tips3 = { (3, 6, 1), (-3, 6, -1), (1, 6, 3), (2, 8, 0), (-2, 8, 0), (0, 8, 1), (0, 8, -1), (2, 8, 2), (-2, 8, 2), (1, 10, 0), (-1, 10, 0), (0, 10, 1), (0, 10, -1), (0, 10, 0) };
        private static readonly (int, int, int)[] _trunk4 = { (0, 0, 0), (0, 1, 0), (0, 2, 0), (1, 3, 1), (1, 4, 1) };
        private static readonly (int, int, int)[] _branches4 = { (2, 3, 0), (3, 3, 0), (4, 3, 1), (-1, 3, 0), (-2, 3, 0), (-3, 3, -1), (0, 3, 2), (0, 3, 3), (1, 3, 4), (2, 4, 0), (3, 4, 0), (-1, 4, 0), (-2, 4, 0), (0, 4, 2), (0, 4, 3) };
        private static readonly (int, int, int)[] _tips4 = { (4, 3, 1), (-3, 3, -1), (1, 3, 4), (3, 4, 0), (-2, 4, 0), (0, 4, 3), (2, 5, 1), (-1, 5, 1), (1, 5, 2), (0, 5, 0) };
        private static readonly (int, int, int)[] _leafCluster =
        {
            (0,0,0),(1,0,0),(-1,0,0),(0,0,1),(0,0,-1),(0,1,0),(0,-1,0),
            (1,1,0),(-1,1,0),(0,1,1),(0,1,-1),(2,0,0),(-2,0,0),(0,0,2),(0,0,-2),
            (1,0,1),(-1,0,1),(1,0,-1),(-1,0,-1),(1,2,0),(-1,2,0),(0,2,1),(0,2,-1),
        };
        private struct TreeVariant { public (int, int, int)[] Trunk, Branches, Tips; }
        private static readonly TreeVariant[] _treeVariants =
        {
            new TreeVariant{Trunk=_trunk0,Branches=_branches0,Tips=_tips0},
            new TreeVariant{Trunk=_trunk1,Branches=_branches1,Tips=_tips1},
            new TreeVariant{Trunk=_trunk2,Branches=_branches2,Tips=_tips2},
            new TreeVariant{Trunk=_trunk3,Branches=_branches3,Tips=_tips3},
            new TreeVariant{Trunk=_trunk4,Branches=_branches4,Tips=_tips4},
        };

        private void PlaceTrees(byte[,,] blocks, int worldX, int worldY, int worldZ, int chunkSize,
            int[] heightsFlat, float[] weightsFlat, bool[] riverFlat, int simplificationLevel = 0)
        {
            int gridSize = simplificationLevel switch { 0 => 5, 1 => 10, _ => 20 };
            for (int gx = 0; gx < chunkSize / gridSize + 1; gx++)
                for (int gz = 0; gz < chunkSize / gridSize + 1; gz++)
                {
                    int cwx = worldX + gx * gridSize, cwz = worldZ + gz * gridSize;
                    float chance = Hash2Df(cwx, cwz, _seed + 200);
                    int offX = (int)(Hash2Df(cwx, cwz, _seed + 201) * (gridSize - 1));
                    int offZ = (int)(Hash2Df(cwx, cwz, _seed + 202) * (gridSize - 1));
                    int lx = gx * gridSize + offX, lz = gz * gridSize + offZ;
                    if (lx >= chunkSize || lz >= chunkSize) continue;
                    int idx = lx * chunkSize + lz;
                    if (riverFlat[idx]) continue;
                    if (heightsFlat[idx] <= SeaLevel) continue;
                    int base_ = idx * BIOME_COUNT;
                    float prob = weightsFlat[base_ + 0] * 0.08f + weightsFlat[base_ + 1] * 0.60f
                               + weightsFlat[base_ + 2] * 0.35f + weightsFlat[base_ + 3] * 0.00f
                               + weightsFlat[base_ + 4] * 0.05f + weightsFlat[base_ + 5] * 0.00f;
                    if (chance > prob) continue;
                    int ly = heightsFlat[idx] + 1 - worldY;
                    int vi = Math.Clamp((int)(Hash2Df(cwx + 5, cwz + 5, _seed + 210) * _treeVariants.Length), 0, _treeVariants.Length - 1);
                    PlaceTreeVariant(blocks, lx, ly, lz, vi, chunkSize);
                }
        }

        private void PlaceTreeVariant(byte[,,] blocks, int lx, int ly, int lz, int vi, int size)
        {
            ref readonly var v = ref _treeVariants[vi];
            foreach (var (dx, dy, dz) in v.Trunk)
            { int bx = lx + dx, by = ly + dy, bz = lz + dz; if (InBounds(bx, by, bz, size)) blocks[bx, by, bz] = BlockType.Wood; }
            foreach (var (dx, dy, dz) in v.Branches)
            { int bx = lx + dx, by = ly + dy, bz = lz + dz; if (InBounds(bx, by, bz, size)) blocks[bx, by, bz] = BlockType.Wood; }
            foreach (var (tx, ty, tz) in v.Tips)
                foreach (var (cx, cy, cz) in _leafCluster)
                {
                    int bx = lx + tx + cx, by = ly + ty + cy, bz = lz + tz + cz;
                    if (!InBounds(bx, by, bz, size)) continue;
                    if (blocks[bx, by, bz] == BlockType.Wood) continue;
                    blocks[bx, by, bz] = BlockType.Leaves;
                }
        }

        private static bool InBounds(int x, int y, int z, int size) =>
            x >= 0 && x < size && y >= 0 && y < size && z >= 0 && z < size;

        // ════════════════════════════════════════════════════════════════════
        // Low-poly & Very-low-poly
        // ════════════════════════════════════════════════════════════════════

        public byte[,,] GenerateLowPolyChunk(int chunkX, int chunkY, int chunkZ, int chunkSize, int simplificationLevel = 0)
        {
            byte[,,] blocks = new byte[chunkSize, chunkSize, chunkSize];
            int worldX = chunkX * chunkSize, worldY = chunkY * chunkSize, worldZ = chunkZ * chunkSize;
            int area = chunkSize * chunkSize;

            int[] heightsFlat = ArrayPool<int>.Shared.Rent(area);
            float[] weightsFlat = ArrayPool<float>.Shared.Rent(area * BIOME_COUNT);
            bool[] riverFlat = ArrayPool<bool>.Shared.Rent(area);

            try
            {
                for (int bx = 0; bx < chunkSize; bx++)
                    for (int bz = 0; bz < chunkSize; bz++)
                    {
                        int idx = bx * chunkSize + bz;
                        float wx = worldX + bx, wz = worldZ + bz;
                        SoftBiomeWeights(wx, wz, out float wPl, out float wFo, out float wTa,
                                               out float wDe, out float wMo, out float wOc);
                        weightsFlat[idx * BIOME_COUNT + 0] = wPl; weightsFlat[idx * BIOME_COUNT + 1] = wFo;
                        weightsFlat[idx * BIOME_COUNT + 2] = wTa; weightsFlat[idx * BIOME_COUNT + 3] = wDe;
                        weightsFlat[idx * BIOME_COUNT + 4] = wMo; weightsFlat[idx * BIOME_COUNT + 5] = wOc;
                        heightsFlat[idx] = GetTerrainHeight(wx, wz, wPl, wFo, wTa, wDe, wMo, wOc, out bool ir);
                        riverFlat[idx] = ir;
                    }

                int sc = simplificationLevel switch { 0 => 20, 1 => 10, _ => 5 };
                for (int bx = 0; bx < chunkSize; bx++)
                    for (int bz = 0; bz < chunkSize; bz++)
                    {
                        int idx = bx * chunkSize + bz;
                        int h = heightsFlat[idx]; bool ir = riverFlat[idx];
                        int base_ = idx * BIOME_COUNT;
                        BiomeType biome = DominantBiome(
                            weightsFlat[base_ + 0], weightsFlat[base_ + 1], weightsFlat[base_ + 2],
                            weightsFlat[base_ + 3], weightsFlat[base_ + 4], weightsFlat[base_ + 5]);
                        for (int by = 0; by < chunkSize; by++)
                        {
                            int wy = worldY + by;
                            if (wy < SeaLevel) continue;
                            if (wy >= h - sc && wy <= h) blocks[bx, by, bz] = wy == h ? GetSurfaceBlock(biome, ir, h) : BlockType.Dirt;
                            else if (wy >= SeaLevel && wy < h - sc) blocks[bx, by, bz] = BlockType.Stone;
                            else if (wy <= SeaLevel && wy > h) blocks[bx, by, bz] = BlockType.Water;
                        }
                    }

                if (simplificationLevel == 0)
                    PaintRiverBeds(blocks, worldX, worldY, worldZ, chunkSize, heightsFlat, riverFlat);

                PlaceTrees(blocks, worldX, worldY, worldZ, chunkSize, heightsFlat, weightsFlat, riverFlat, simplificationLevel);

                if (simplificationLevel == 0)
                {
                    var structures = StructurePlacer.GetStructuresForChunk(
                        chunkX, chunkY, chunkZ, chunkSize, _seed, GetSurfaceHeight);
                    foreach (var s in structures)
                        StructurePlacer.Apply(blocks, s, chunkX, chunkY, chunkZ, chunkSize, null);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(heightsFlat);
                ArrayPool<float>.Shared.Return(weightsFlat);
                ArrayPool<bool>.Shared.Return(riverFlat);
            }
            return blocks;
        }

        public int[,] GenerateVeryLowPolyChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            int gridSize = chunkSize + 2;
            int[,] heightMap = new int[gridSize, gridSize];
            int worldX = chunkX * chunkSize, worldZ = chunkZ * chunkSize;
            for (int bx = 0; bx < gridSize; bx++)
                for (int bz = 0; bz < gridSize; bz++)
                    heightMap[bx, bz] = GetTerrainHeightVLP(worldX + bx - 1, worldZ + bz - 1);
            return heightMap;
        }

        // ════════════════════════════════════════════════════════════════════
        // Noise primitives
        // ════════════════════════════════════════════════════════════════════

        private float Perlin2D(float x, float y)
        {
            int X = (int)Math.Floor(x) & 255, Y = (int)Math.Floor(y) & 255;
            x -= (float)Math.Floor(x); y -= (float)Math.Floor(y);
            float u = Fade(x), v = Fade(y);
            int a = _perm[X] + Y, aa = _perm[a], ab = _perm[a + 1], b = _perm[X + 1] + Y, ba = _perm[b], bb = _perm[b + 1];
            return Lerp(Lerp(Grad2(aa, x, y), Grad2(ba, x - 1, y), u), Lerp(Grad2(ab, x, y - 1), Grad2(bb, x - 1, y - 1), u), v);
        }

        private float Perlin3D(float x, float y, float z)
        {
            int X = (int)Math.Floor(x) & 255, Y = (int)Math.Floor(y) & 255, Z = (int)Math.Floor(z) & 255;
            x -= (float)Math.Floor(x); y -= (float)Math.Floor(y); z -= (float)Math.Floor(z);
            float u = Fade(x), v = Fade(y), w = Fade(z);
            int a = _perm[X] + Y, aa = _perm[a] + Z, ab = _perm[a + 1] + Z, b = _perm[X + 1] + Y, ba = _perm[b] + Z, bb = _perm[b + 1] + Z;
            return Lerp(
                Lerp(Lerp(Grad3(aa, x, y, z), Grad3(ba, x - 1, y, z), u), Lerp(Grad3(ab, x, y - 1, z), Grad3(bb, x - 1, y - 1, z), u), v),
                Lerp(Lerp(Grad3(aa + 1, x, y, z - 1), Grad3(ba + 1, x - 1, y, z - 1), u), Lerp(Grad3(ab + 1, x, y - 1, z - 1), Grad3(bb + 1, x - 1, y - 1, z - 1), u), v), w);
        }

        private float OctaveNoise2D(float x, float z, float scale, int octaves, float persistence, int seed)
        {
            float total = 0f, freq = 1f / scale, amp = 1f, maxV = 0f;
            float ox = (seed * 0.13f) % 1000f, oz = (seed * 0.17f) % 1000f;
            for (int i = 0; i < octaves; i++) { total += Perlin2D((x + ox) * freq, (z + oz) * freq) * amp; maxV += amp; amp *= persistence; freq *= 2f; }
            return total / maxV;
        }

        private float OctaveNoise3D(float x, float y, float z, float scale, int octaves, float persistence, int seed)
        {
            float total = 0f, freq = 1f / scale, amp = 1f, maxV = 0f;
            float ox = (seed * 0.13f) % 1000f, oy = (seed * 0.19f) % 1000f, oz = (seed * 0.17f) % 1000f;
            for (int i = 0; i < octaves; i++) { total += Perlin3D((x + ox) * freq, (y + oy) * freq, (z + oz) * freq) * amp; maxV += amp; amp *= persistence; freq *= 2f; }
            return total / maxV;
        }

        private float RidgeNoise2D(float x, float z, float scale, int octaves, int seed)
        {
            float total = 0f, freq = 1f / scale, amp = 1f, maxV = 0f;
            float ox = (seed * 0.11f) % 1000f, oz = (seed * 0.23f) % 1000f;
            for (int i = 0; i < octaves; i++) { float n = Perlin2D((x + ox) * freq, (z + oz) * freq); n = 1f - Math.Abs(n); n *= n; total += n * amp; maxV += amp; amp *= 0.5f; freq *= 2f; }
            return total / maxV;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private float Grad2(int hash, float x, float y)
        { int h = _perm[hash & 255] & 3; float u = h < 2 ? x : y, v = h < 2 ? y : x; return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v); }

        private float Grad3(int hash, float x, float y, float z)
        { int h = _perm[hash & 255] & 15; float u = h < 8 ? x : y, v = h < 4 ? y : (h == 12 || h == 14 ? x : z); return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v); }

        private static float Hash2Df(int x, int z, int seed)
        { int h = unchecked(seed ^ (x * 374761393) ^ (z * 668265263)); h = unchecked((h ^ (h >> 13)) * 1274126177); h ^= h >> 16; return (float)((uint)h) / uint.MaxValue; }
    }
}