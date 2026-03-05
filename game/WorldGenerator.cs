using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// GenerateVeryLowPolyChunk simplificado:
    /// Reutiliza SoftBiomeWeights (ya barato) y blendea alturas reales con menos octavas.
    /// Así las montañas aparecen correctamente en el horizonte sin pop-in brusco.
    /// </summary>
    public class WorldGenerator
    {
        private readonly int _seed;

        private const int SeaLevel   = 22;
        private const int BaseHeight = 22;
        private const int MaxHeight  = 120;
        private const int MinHeight  = 4;

        private const float CaveThreshold = 0.72f;

        private readonly int[] _perm = new int[512];

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
        //  CACHE Y GENERACIÓN HQ
        // ============================================================

        public byte[,,] GetOrGenerateChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            var key = (chunkX, chunkY, chunkZ);
            lock (_cacheLock) { if (_blockCache.TryGetValue(key, out var cached)) return cached; }
            byte[,,] blocks = GenerateChunk(chunkX, chunkY, chunkZ, chunkSize);
            lock (_cacheLock)
            {
                if (_blockCache.TryGetValue(key, out var cached2)) return cached2;
                _blockCache[key] = blocks;
            }
            return blocks;
        }

        public byte[,,] GenerateChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            byte[,,] blocks = new byte[chunkSize, chunkSize, chunkSize];
            int worldX = chunkX*chunkSize, worldY = chunkY*chunkSize, worldZ = chunkZ*chunkSize;

            int[,]    heights   = new int[chunkSize, chunkSize];
            float[,,] weights   = new float[chunkSize, chunkSize, BIOME_COUNT];
            bool[,]   riverMask = new bool[chunkSize, chunkSize];

            for (int bx = 0; bx < chunkSize; bx++)
            for (int bz = 0; bz < chunkSize; bz++)
            {
                float wx = worldX+bx, wz = worldZ+bz;
                SoftBiomeWeights(wx, wz, out float wP, out float wF, out float wH, out float wM, out float wS);
                weights[bx,bz,0]=wP; weights[bx,bz,1]=wF; weights[bx,bz,2]=wH; weights[bx,bz,3]=wM; weights[bx,bz,4]=wS;
                heights[bx,bz]   = GetTerrainHeight(wx, wz, wP, wF, wH, wM, wS, out bool ir);
                riverMask[bx,bz] = ir;
            }

            for (int bx = 0; bx < chunkSize; bx++)
            for (int bz = 0; bz < chunkSize; bz++)
            {
                int h = heights[bx,bz]; bool ir = riverMask[bx,bz];
                BiomeType biome = DominantBiome(weights[bx,bz,0],weights[bx,bz,1],weights[bx,bz,2],weights[bx,bz,3],weights[bx,bz,4]);
                for (int by = 0; by < chunkSize; by++)
                    blocks[bx,by,bz] = GetBlockAt(worldX+bx, worldY+by, worldZ+bz, h, biome, ir);
            }

            PlaceTrees(blocks, worldX, worldY, worldZ, chunkSize, heights, weights, riverMask, 0);
            return blocks;
        }

        // ============================================================
        //  BIOMAS
        // ============================================================

        private const int BIOME_COUNT = 5;
        private enum BiomeType { Plains, Forest, Hills, Mountains, SnowPeaks }

        private void SoftBiomeWeights(float wx, float wz,
            out float wPlains, out float wForest, out float wHills, out float wMountain, out float wSnow)
        {
            float warpX = OctaveNoise2D(wx, wz, 250f, 2, 0.5f, _seed+500) * 30f;
            float warpZ = OctaveNoise2D(wx, wz, 250f, 2, 0.5f, _seed+501) * 30f;
            float wx2 = wx+warpX, wz2 = wz+warpZ;

            float temp = (OctaveNoise2D(wx2, wz2, 500f, 3, 0.5f, _seed)   + 1f) * 0.5f;
            float hum  = (OctaveNoise2D(wx2, wz2, 350f, 3, 0.5f, _seed+1) + 1f) * 0.5f;
            float rug  = (OctaveNoise2D(wx2, wz2, 300f, 3, 0.5f, _seed+2) + 1f) * 0.5f;

            wSnow     = SmoothRange(rug, 0.62f, 0.78f) * SmoothRange(1f-temp, 0.45f, 0.65f);
            wMountain = SmoothRange(rug, 0.55f, 0.72f) * (1f - SmoothRange(1f-temp, 0.55f, 0.70f));
            wHills    = SmoothRange(rug, 0.30f, 0.50f) * SmoothRange(1f-rug, 0.30f, 0.50f);
            wForest   = SmoothRange(temp, 0.45f, 0.65f) * SmoothRange(hum, 0.45f, 0.65f) * (1f - SmoothRange(rug, 0.50f, 0.65f));
            wPlains   = SmoothRange(1f-rug, 0.45f, 0.65f) * (1f - SmoothRange(hum, 0.55f, 0.70f));

            float total = wPlains+wForest+wHills+wMountain+wSnow;
            if (total < 0.001f) { wPlains=1f; wForest=wHills=wMountain=wSnow=0f; return; }
            float inv = 1f/total; wPlains*=inv; wForest*=inv; wHills*=inv; wMountain*=inv; wSnow*=inv;
        }

        private static float SmoothRange(float x, float edge0, float edge1)
        {
            float t = Math.Clamp((x-edge0)/(edge1-edge0+0.0001f), 0f, 1f);
            return t*t*(3f-2f*t);
        }

        private static BiomeType DominantBiome(float wP, float wF, float wH, float wM, float wS)
        {
            if (wS>=wP&&wS>=wF&&wS>=wH&&wS>=wM) return BiomeType.SnowPeaks;
            if (wM>=wP&&wM>=wF&&wM>=wH)          return BiomeType.Mountains;
            if (wH>=wP&&wH>=wF)                  return BiomeType.Hills;
            if (wF>=wP)                           return BiomeType.Forest;
            return BiomeType.Plains;
        }

        // ============================================================
        //  ALTURA DE TERRENO (completa, para HQ y LP)
        // ============================================================

        private int GetTerrainHeight(float wx, float wz,
            float wPlains, float wForest, float wHills, float wMountain, float wSnow, out bool isRiver)
        {
            isRiver = false;
            float plainsH   = BaseHeight + 2f  + OctaveNoise2D(wx, wz, 120f, 3, 0.40f, _seed+10) * 8f;
            float forestH   = BaseHeight + 4f  + OctaveNoise2D(wx, wz, 100f, 4, 0.45f, _seed+30) * 12f;
            float hillsH    = BaseHeight + 8f  + OctaveNoise2D(wx, wz, 80f,  4, 0.50f, _seed+20) * 22f;
            float mountainH = BaseHeight + 18f + OctaveNoise2D(wx, wz, 60f,  6, 0.55f, _seed+40) * 38f + RidgeNoise2D(wx, wz, 50f, 4, _seed+41) * 28f;
            float snowH     = BaseHeight + 38f + OctaveNoise2D(wx, wz, 50f,  6, 0.60f, _seed+50) * 42f + RidgeNoise2D(wx, wz, 40f, 5, _seed+51) * 32f;
            float h = plainsH*wPlains + forestH*wForest + hillsH*wHills + mountainH*wMountain + snowH*wSnow;

            if (wPlains+wForest+wHills > 0.5f)
            {
                float ri = GetRiverInfluence(wx, wz, out _);
                if (ri > 0f) { h = h*(1f-ri)+(SeaLevel-3f)*ri; if (ri > 0.2f) isRiver = true; }
            }
            return (int)Math.Clamp(h, MinHeight, MaxHeight);
        }

        // ============================================================
        //  ALTURA VLP — biome-aware, menos octavas que HQ
        //
        //  Reutiliza SoftBiomeWeights (ya barato) y blendea las mismas
        //  curvas de altura que el sistema real pero con 2-3 octavas en
        //  vez de 4-6. Resultado: montañas visibles en el horizonte sin
        //  pop-in, con un coste ~2x respecto al noise único anterior
        //  pero todavía mucho más ligero que GenerateChunk completo.
        // ============================================================

        private int GetTerrainHeightVLP(float wx, float wz)
        {
            SoftBiomeWeights(wx, wz, out float wP, out float wF, out float wH, out float wM, out float wS);

            float plainsH   = BaseHeight + 2f  + OctaveNoise2D(wx, wz, 120f, 2, 0.45f, _seed + 10) * 8f;
            float forestH   = BaseHeight + 4f  + OctaveNoise2D(wx, wz, 100f, 2, 0.45f, _seed + 30) * 12f;
            float hillsH    = BaseHeight + 8f  + OctaveNoise2D(wx, wz,  80f, 3, 0.50f, _seed + 20) * 22f;
            float mountainH = BaseHeight + 18f + OctaveNoise2D(wx, wz,  60f, 3, 0.55f, _seed + 40) * 38f
                                               + RidgeNoise2D (wx, wz,  50f, 2,         _seed + 41) * 28f;
            float snowH     = BaseHeight + 38f + OctaveNoise2D(wx, wz,  50f, 3, 0.60f, _seed + 50) * 42f
                                               + RidgeNoise2D (wx, wz,  40f, 3,         _seed + 51) * 32f;

            float h = plainsH*wP + forestH*wF + hillsH*wH + mountainH*wM + snowH*wS;
            return (int)Math.Clamp(h, MinHeight, MaxHeight);
        }

        // ============================================================
        //  RÍOS
        // ============================================================

        private float GetRiverInfluence(float wx, float wz, out float depth01)
        {
            float rwX = OctaveNoise2D(wx, wz, 150f, 2, 0.5f, _seed+600) * 30f;
            float rwZ = OctaveNoise2D(wx, wz, 150f, 2, 0.5f, _seed+601) * 30f;
            float n = OctaveNoise2D(wx+rwX, wz+rwZ, 300f, 4, 0.5f, _seed+700);
            n = (n+1f)*0.5f;
            float raw = 1f - Math.Abs(n*2f-1f);
            raw = (float)Math.Pow(raw, 3f);
            depth01 = raw;
            const float threshold = 0.55f;
            if (raw < threshold) return 0f;
            return Math.Clamp((raw-threshold)/(1f-threshold), 0f, 1f);
        }

        // ============================================================
        //  TIPO DE BLOQUE
        // ============================================================

        private byte GetBlockAt(int wx, int wy, int wz, int terrainH, BiomeType biome, bool isRiver)
        {
            if (wy > terrainH) return wy <= SeaLevel ? BlockType.Water : BlockType.Air;
            if (wy > 4 && wy < terrainH)
            {
                float cave = (OctaveNoise3D(wx, wy, wz, 40f, 2, 0.5f, _seed+99) + 1f) * 0.5f;
                if (cave > CaveThreshold) return BlockType.Air;
            }
            if (wy == terrainH)
            {
                if (isRiver || terrainH <= SeaLevel) return BlockType.Sand;
                return biome switch { BiomeType.SnowPeaks=>BlockType.Snow, BiomeType.Mountains=>BlockType.Stone, _=>BlockType.Grass };
            }
            if (wy >= terrainH-2) return terrainH <= SeaLevel ? BlockType.Sand : BlockType.Dirt;
            return BlockType.Stone;
        }

        // ============================================================
        //  ÁRBOLES
        // ============================================================

        private static readonly (int,int,int)[] _trunk0    = {(0,0,0),(0,1,0),(0,2,0),(0,3,0),(0,4,0),(0,5,0)};
        private static readonly (int,int,int)[] _branches0 = {};
        private static readonly (int,int,int)[] _tips0     = {(0,5,0),(1,5,0),(-1,5,0),(0,5,1),(0,5,-1),(0,6,0),(1,6,0),(-1,6,0),(0,6,1),(0,6,-1)};
        private static readonly (int,int,int)[] _trunk1    = {(0,0,0),(0,1,0),(0,2,0),(0,3,0),(0,4,0),(0,5,0),(0,6,0)};
        private static readonly (int,int,int)[] _branches1 = {(1,4,0),(2,4,0),(3,4,0),(-1,4,0),(-2,4,0),(-3,4,0),(0,4,1),(0,4,2),(0,4,3),(0,4,-1),(0,4,-2),(0,4,-3),(2,5,2),(-2,5,2),(2,5,-2),(-2,5,-2)};
        private static readonly (int,int,int)[] _tips1     = {(3,4,0),(-3,4,0),(0,4,3),(0,4,-3),(2,5,2),(-2,5,2),(2,5,-2),(-2,5,-2),(1,6,0),(-1,6,0),(0,6,1),(0,6,-1),(0,6,0)};
        private static readonly (int,int,int)[] _trunk2    = {(0,0,0),(0,1,0),(0,2,0),(1,3,0),(1,4,0),(2,5,0),(2,6,0),(2,7,1),(2,8,1)};
        private static readonly (int,int,int)[] _branches2 = {(-1,3,0),(-2,4,0),(-3,4,-1),(3,5,0),(4,6,0),(5,6,1),(3,7,1),(4,7,1),(3,8,2)};
        private static readonly (int,int,int)[] _tips2     = {(-3,4,-1),(5,6,1),(3,8,2),(2,8,2),(1,8,1),(3,8,0),(2,9,1)};
        private static readonly (int,int,int)[] _trunk3    = {(0,0,0),(0,1,0),(0,2,0),(0,3,0),(0,4,0),(0,5,0),(0,6,0),(0,7,0),(0,8,0),(0,9,0),(0,10,0)};
        private static readonly (int,int,int)[] _branches3 = {(1,6,0),(2,6,0),(3,6,1),(-1,6,0),(-2,6,0),(-3,6,-1),(0,6,1),(0,6,2),(1,6,3),(0,6,-1),(0,6,-2),(1,8,0),(2,8,0),(-1,8,0),(-2,8,0),(0,8,1),(0,8,-1),(1,10,0),(-1,10,0),(0,10,1),(0,10,-1)};
        private static readonly (int,int,int)[] _tips3     = {(3,6,1),(-3,6,-1),(1,6,3),(2,8,0),(-2,8,0),(0,8,1),(0,8,-1),(2,8,2),(-2,8,2),(1,10,0),(-1,10,0),(0,10,1),(0,10,-1),(0,10,0)};
        private static readonly (int,int,int)[] _trunk4    = {(0,0,0),(0,1,0),(0,2,0),(1,3,1),(1,4,1)};
        private static readonly (int,int,int)[] _branches4 = {(2,3,0),(3,3,0),(4,3,1),(-1,3,0),(-2,3,0),(-3,3,-1),(0,3,2),(0,3,3),(1,3,4),(2,4,0),(3,4,0),(-1,4,0),(-2,4,0),(0,4,2),(0,4,3)};
        private static readonly (int,int,int)[] _tips4     = {(4,3,1),(-3,3,-1),(1,3,4),(3,4,0),(-2,4,0),(0,4,3),(2,5,1),(-1,5,1),(1,5,2),(0,5,0)};
        private static readonly (int,int,int)[] _leafCluster =
        {
            (0,0,0),(1,0,0),(-1,0,0),(0,0,1),(0,0,-1),(0,1,0),(0,-1,0),
            (1,1,0),(-1,1,0),(0,1,1),(0,1,-1),(2,0,0),(-2,0,0),(0,0,2),(0,0,-2),
            (1,0,1),(-1,0,1),(1,0,-1),(-1,0,-1),(1,2,0),(-1,2,0),(0,2,1),(0,2,-1),
        };
        private struct TreeVariant { public (int,int,int)[] Trunk, Branches, Tips; }
        private static readonly TreeVariant[] _treeVariants =
        {
            new TreeVariant{Trunk=_trunk0,Branches=_branches0,Tips=_tips0},
            new TreeVariant{Trunk=_trunk1,Branches=_branches1,Tips=_tips1},
            new TreeVariant{Trunk=_trunk2,Branches=_branches2,Tips=_tips2},
            new TreeVariant{Trunk=_trunk3,Branches=_branches3,Tips=_tips3},
            new TreeVariant{Trunk=_trunk4,Branches=_branches4,Tips=_tips4},
        };

        private void PlaceTrees(byte[,,] blocks, int worldX, int worldY, int worldZ, int chunkSize,
            int[,] heights, float[,,] biomeWeights, bool[,] riverMask, int simplificationLevel = 0)
        {
            int gridSize = simplificationLevel switch { 0=>5, 1=>10, _=>20 };
            for (int gx = 0; gx < chunkSize/gridSize+1; gx++)
            for (int gz = 0; gz < chunkSize/gridSize+1; gz++)
            {
                int cwx=worldX+gx*gridSize, cwz=worldZ+gz*gridSize;
                float chance = Hash2Df(cwx, cwz, _seed+200);
                int offX=(int)(Hash2Df(cwx,cwz,_seed+201)*(gridSize-1));
                int offZ=(int)(Hash2Df(cwx,cwz,_seed+202)*(gridSize-1));
                int lx=gx*gridSize+offX, lz=gz*gridSize+offZ;
                if (lx>=chunkSize||lz>=chunkSize) continue;
                if (riverMask[lx,lz]) continue;
                float prob = biomeWeights[lx,lz,0]*0.10f+biomeWeights[lx,lz,1]*0.55f+biomeWeights[lx,lz,2]*0.18f;
                if (chance>prob) continue;
                int ly=heights[lx,lz]+1-worldY;
                int vi=Math.Clamp((int)(Hash2Df(cwx+5,cwz+5,_seed+210)*_treeVariants.Length),0,_treeVariants.Length-1);
                PlaceTreeVariant(blocks,lx,ly,lz,vi,chunkSize);
            }
        }

        private void PlaceTreeVariant(byte[,,] blocks, int lx, int ly, int lz, int vi, int size)
        {
            ref readonly var v = ref _treeVariants[vi];
            foreach (var (dx,dy,dz) in v.Trunk)   { int bx=lx+dx,by=ly+dy,bz=lz+dz; if (InBounds(bx,by,bz,size)) blocks[bx,by,bz]=BlockType.Wood; }
            foreach (var (dx,dy,dz) in v.Branches) { int bx=lx+dx,by=ly+dy,bz=lz+dz; if (InBounds(bx,by,bz,size)) blocks[bx,by,bz]=BlockType.Wood; }
            foreach (var (tx,ty,tz) in v.Tips)
            foreach (var (cx,cy,cz) in _leafCluster)
            {
                int bx=lx+tx+cx,by=ly+ty+cy,bz=lz+tz+cz;
                if (!InBounds(bx,by,bz,size)) continue;
                if (blocks[bx,by,bz]==BlockType.Wood) continue;
                blocks[bx,by,bz]=BlockType.Leaves;
            }
        }

        private static bool InBounds(int x, int y, int z, int size) =>
            x>=0&&x<size&&y>=0&&y<size&&z>=0&&z<size;

        // ============================================================
        //  LOD SIMPLIFICADO
        // ============================================================

        public byte[,,] GenerateLowPolyChunk(int chunkX, int chunkY, int chunkZ, int chunkSize, int simplificationLevel = 0)
        {
            byte[,,] blocks = new byte[chunkSize, chunkSize, chunkSize];
            int worldX=chunkX*chunkSize, worldY=chunkY*chunkSize, worldZ=chunkZ*chunkSize;

            int[,]    heights   = new int[chunkSize, chunkSize];
            float[,,] weights   = new float[chunkSize, chunkSize, BIOME_COUNT];
            bool[,]   riverMask = new bool[chunkSize, chunkSize];

            for (int bx = 0; bx < chunkSize; bx++)
            for (int bz = 0; bz < chunkSize; bz++)
            {
                float wx=worldX+bx, wz=worldZ+bz;
                SoftBiomeWeights(wx,wz,out float wP,out float wF,out float wH,out float wM,out float wS);
                weights[bx,bz,0]=wP;weights[bx,bz,1]=wF;weights[bx,bz,2]=wH;weights[bx,bz,3]=wM;weights[bx,bz,4]=wS;
                heights[bx,bz]=GetTerrainHeight(wx,wz,wP,wF,wH,wM,wS,out bool ir);
                riverMask[bx,bz]=ir;
            }

            int sc = simplificationLevel switch { 0=>20, 1=>10, _=>5 };
            for (int bx = 0; bx < chunkSize; bx++)
            for (int bz = 0; bz < chunkSize; bz++)
            {
                int h=heights[bx,bz]; bool ir=riverMask[bx,bz];
                BiomeType biome=DominantBiome(weights[bx,bz,0],weights[bx,bz,1],weights[bx,bz,2],weights[bx,bz,3],weights[bx,bz,4]);
                for (int by = 0; by < chunkSize; by++)
                {
                    int wy=worldY+by;
                    if      (wy>=h-sc&&wy<=h)              blocks[bx,by,bz]=wy==h?GetSurfaceBlock(biome,ir,h):BlockType.Dirt;
                    else if (wy>=SeaLevel&&wy<h-sc)        blocks[bx,by,bz]=BlockType.Stone;
                    else if (wy<=SeaLevel&&wy>h)           blocks[bx,by,bz]=BlockType.Water;
                }
            }
            PlaceTrees(blocks,worldX,worldY,worldZ,chunkSize,heights,weights,riverMask,simplificationLevel);
            return blocks;
        }

        private byte GetSurfaceBlock(BiomeType biome, bool isRiver, int h)
        {
            if (isRiver||h<=SeaLevel) return BlockType.Sand;
            return biome switch { BiomeType.SnowPeaks=>BlockType.Snow, BiomeType.Mountains=>BlockType.Stone, _=>BlockType.Grass };
        }

        // ============================================================
        //  VERY LOW POLY — heightmap (chunkSize+2)x(chunkSize+2)
        // ============================================================

        public int[,] GenerateVeryLowPolyChunk(int chunkX, int chunkY, int chunkZ, int chunkSize)
        {
            int gridSize = chunkSize + 2;
            int[,] heightMap = new int[gridSize, gridSize];
            int worldX = chunkX*chunkSize, worldZ = chunkZ*chunkSize;

            for (int bx = 0; bx < gridSize; bx++)
            for (int bz = 0; bz < gridSize; bz++)
            {
                float wx = worldX + bx - 1;
                float wz = worldZ + bz - 1;
                heightMap[bx, bz] = GetTerrainHeightVLP(wx, wz);
            }

            return heightMap;
        }

        // ============================================================
        //  RUIDO PERLIN 2D / 3D
        // ============================================================

        private float Perlin2D(float x, float y)
        {
            int X=(int)Math.Floor(x)&255, Y=(int)Math.Floor(y)&255;
            x-=(float)Math.Floor(x); y-=(float)Math.Floor(y);
            float u=Fade(x), v=Fade(y);
            int a=_perm[X]+Y,aa=_perm[a],ab=_perm[a+1],b=_perm[X+1]+Y,ba=_perm[b],bb=_perm[b+1];
            return Lerp(Lerp(Grad2(aa,x,y),Grad2(ba,x-1,y),u),Lerp(Grad2(ab,x,y-1),Grad2(bb,x-1,y-1),u),v);
        }

        private float Perlin3D(float x, float y, float z)
        {
            int X=(int)Math.Floor(x)&255,Y=(int)Math.Floor(y)&255,Z=(int)Math.Floor(z)&255;
            x-=(float)Math.Floor(x);y-=(float)Math.Floor(y);z-=(float)Math.Floor(z);
            float u=Fade(x),v=Fade(y),w=Fade(z);
            int a=_perm[X]+Y,aa=_perm[a]+Z,ab=_perm[a+1]+Z,b=_perm[X+1]+Y,ba=_perm[b]+Z,bb=_perm[b+1]+Z;
            return Lerp(
                Lerp(Lerp(Grad3(aa,x,y,z),Grad3(ba,x-1,y,z),u),Lerp(Grad3(ab,x,y-1,z),Grad3(bb,x-1,y-1,z),u),v),
                Lerp(Lerp(Grad3(aa+1,x,y,z-1),Grad3(ba+1,x-1,y,z-1),u),Lerp(Grad3(ab+1,x,y-1,z-1),Grad3(bb+1,x-1,y-1,z-1),u),v),w);
        }

        private float OctaveNoise2D(float x, float z, float scale, int octaves, float persistence, int seed)
        {
            float total=0f,freq=1f/scale,amp=1f,maxV=0f;
            float ox=(seed*0.13f)%1000f, oz=(seed*0.17f)%1000f;
            for (int i=0;i<octaves;i++){total+=Perlin2D((x+ox)*freq,(z+oz)*freq)*amp;maxV+=amp;amp*=persistence;freq*=2f;}
            return total/maxV;
        }

        private float OctaveNoise3D(float x, float y, float z, float scale, int octaves, float persistence, int seed)
        {
            float total=0f,freq=1f/scale,amp=1f,maxV=0f;
            float ox=(seed*0.13f)%1000f,oy=(seed*0.19f)%1000f,oz=(seed*0.17f)%1000f;
            for (int i=0;i<octaves;i++){total+=Perlin3D((x+ox)*freq,(y+oy)*freq,(z+oz)*freq)*amp;maxV+=amp;amp*=persistence;freq*=2f;}
            return total/maxV;
        }

        private float RidgeNoise2D(float x, float z, float scale, int octaves, int seed)
        {
            float total=0f,freq=1f/scale,amp=1f,maxV=0f;
            float ox=(seed*0.11f)%1000f,oz=(seed*0.23f)%1000f;
            for (int i=0;i<octaves;i++){float n=Perlin2D((x+ox)*freq,(z+oz)*freq);n=1f-Math.Abs(n);n*=n;total+=n*amp;maxV+=amp;amp*=0.5f;freq*=2f;}
            return total/maxV;
        }

        private static float Fade(float t) => t*t*t*(t*(t*6-15)+10);
        private static float Lerp(float a, float b, float t) => a+(b-a)*t;

        private float Grad2(int hash, float x, float y)
        { int h=_perm[hash&255]&3; float u=h<2?x:y,v=h<2?y:x; return ((h&1)==0?u:-u)+((h&2)==0?v:-v); }

        private float Grad3(int hash, float x, float y, float z)
        { int h=_perm[hash&255]&15; float u=h<8?x:y,v=h<4?y:(h==12||h==14?x:z); return ((h&1)==0?u:-u)+((h&2)==0?v:-v); }

        private static float Hash2Df(int x, int z, int seed)
        { int h=unchecked(seed^(x*374761393)^(z*668265263)); h=unchecked((h^(h>>13))*1274126177); h^=h>>16; return (float)((uint)h)/uint.MaxValue; }
    }
}