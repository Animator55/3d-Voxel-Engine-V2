using System;
using System.Collections.Generic;

namespace game
{
    public readonly struct BlockOverride
    {
    
        public readonly int Dx, Dy, Dz;
        public readonly byte BlockType;
    
        public readonly bool OnlyIfAir;

        public BlockOverride(int dx, int dy, int dz, byte blockType, bool onlyIfAir = false)
        {
            Dx = dx; Dy = dy; Dz = dz;
            BlockType = blockType;
            OnlyIfAir = onlyIfAir;
        }
    }


    public class StructureDef
    {
        public string Name;
    
        public float SpawnChance;
    
        public int MinSpacing;
    
        public byte[] ValidSurfaces;
        public BlockOverride[] Blocks;
    }


    public readonly struct StructurePlacement
    {
        public readonly string StructureName;
    
        public readonly int WorldX, WorldY, WorldZ;
        public readonly BlockOverride[] Blocks;

        public StructurePlacement(string name, int wx, int wy, int wz, BlockOverride[] blocks)
        {
            StructureName = name; WorldX = wx; WorldY = wy; WorldZ = wz; Blocks = blocks;
        }
    }


    public static class StructurePlacer
    {
    
        private const int OVERLAP_RADIUS = 2;  
        private static readonly StructureDef GlowstonePillar = new StructureDef
        {
            Name        = "GlowstonePillar",
            SpawnChance = 0.5f,
            MinSpacing  = 120,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Sand, BlockType.Stone, BlockType.Snow },
            Blocks = new[]
            {
                new BlockOverride(0, 0, 0, BlockType.Stone),
                new BlockOverride(0, 1, 0, BlockType.Glowstone),
                new BlockOverride(0, 2, 0, BlockType.Glowstone),
                new BlockOverride(0, 3, 0, BlockType.Glowstone),
                new BlockOverride(0, 4, 0, BlockType.Stone),
            }
        };

    
    
        private static readonly StructureDef GlowstoneLantern = new StructureDef
        {
            Name        = "GlowstoneLantern",
            SpawnChance = 0.5f,
            MinSpacing  = 200,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Snow },
            Blocks = new[]
            {
            
                new BlockOverride( 0, 0,  0, BlockType.Stone),
                new BlockOverride( 0, 1,  0, BlockType.Stone),
                new BlockOverride( 0, 2,  0, BlockType.Stone),
            
                new BlockOverride( 1, 2,  0, BlockType.Glowstone),
                new BlockOverride(-1, 2,  0, BlockType.Glowstone),
                new BlockOverride( 0, 2,  1, BlockType.Glowstone),
                new BlockOverride( 0, 2, -1, BlockType.Glowstone),
            
                new BlockOverride( 0, 3,  0, BlockType.Stone),
            }
        };

    
    
        private static readonly StructureDef StoneRuin = new StructureDef
        {
            Name        = "StoneRuin",
            SpawnChance = 0.018f,
            MinSpacing  = 300,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Stone, BlockType.Snow },
            Blocks = new[]
            {
            
                new BlockOverride(-1,0,-2,BlockType.Stone), new BlockOverride(0,0,-2,BlockType.Stone), new BlockOverride(1,0,-2,BlockType.Stone),
                new BlockOverride(-1,1,-2,BlockType.Stone), new BlockOverride(0,1,-2,BlockType.Stone), new BlockOverride(1,1,-2,BlockType.Stone),
                new BlockOverride(-1,2,-2,BlockType.Stone),                                            new BlockOverride(1,2,-2,BlockType.Stone),
            
                new BlockOverride(-1,0, 2,BlockType.Stone), new BlockOverride(0,0, 2,BlockType.Stone),
                new BlockOverride(-1,1, 2,BlockType.Stone),
            
                new BlockOverride(-2,0,-1,BlockType.Stone), new BlockOverride(-2,0,0,BlockType.Stone), new BlockOverride(-2,0,1,BlockType.Stone),
                new BlockOverride(-2,1,-1,BlockType.Stone), new BlockOverride(-2,1,0,BlockType.Stone), new BlockOverride(-2,1,1,BlockType.Stone),
                new BlockOverride(-2,2, 0,BlockType.Stone),
            
                new BlockOverride( 2,0,-1,BlockType.Stone), new BlockOverride( 2,0,0,BlockType.Stone), new BlockOverride( 2,0,1,BlockType.Stone),
                new BlockOverride( 2,1,-1,BlockType.Stone), new BlockOverride( 2,1,0,BlockType.Stone), new BlockOverride( 2,1,1,BlockType.Stone),
                new BlockOverride( 2,2,-1,BlockType.Stone), new BlockOverride( 2,2,0,BlockType.Stone), new BlockOverride( 2,2,1,BlockType.Stone),
            
                new BlockOverride( 0,0, 0,BlockType.Glowstone),
            }
        };

    
    
        private static readonly StructureDef DesertObelisk = new StructureDef
        {
            Name        = "DesertObelisk",
            SpawnChance = 0.03f,
            MinSpacing  = 250,
            ValidSurfaces = new[] { BlockType.Sand },
            Blocks = new[]
            {
                new BlockOverride( 0,0, 0,BlockType.Stone),
                new BlockOverride(-1,0,-1,BlockType.Stone), new BlockOverride(1,0,-1,BlockType.Stone),
                new BlockOverride(-1,0, 1,BlockType.Stone), new BlockOverride(1,0, 1,BlockType.Stone),
                new BlockOverride( 0,1, 0,BlockType.Stone),
                new BlockOverride(-1,1,-1,BlockType.Stone), new BlockOverride(1,1,-1,BlockType.Stone),
                new BlockOverride(-1,1, 1,BlockType.Stone), new BlockOverride(1,1, 1,BlockType.Stone),
                new BlockOverride( 0,2, 0,BlockType.Stone),
                new BlockOverride(-1,2, 0,BlockType.Stone), new BlockOverride(1,2, 0,BlockType.Stone),
                new BlockOverride( 0,2,-1,BlockType.Stone), new BlockOverride(0,2,1,BlockType.Stone),
                new BlockOverride( 0,3, 0,BlockType.Stone),
                new BlockOverride( 0,4, 0,BlockType.Stone),
                new BlockOverride( 0,5, 0,BlockType.Glowstone),  
            }
        };

    
    
        private static readonly StructureDef StoneCircle = new StructureDef
        {
            Name        = "StoneCircle",
            SpawnChance = 0.012f,
            MinSpacing  = 400,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Snow },
            Blocks = new[]
            {
            
                new BlockOverride( 4,0, 0,BlockType.Stone), new BlockOverride( 4,1, 0,BlockType.Stone), new BlockOverride( 4,2, 0,BlockType.Stone),
                new BlockOverride(-4,0, 0,BlockType.Stone), new BlockOverride(-4,1, 0,BlockType.Stone), new BlockOverride(-4,2, 0,BlockType.Stone),
                new BlockOverride( 0,0, 4,BlockType.Stone), new BlockOverride( 0,1, 4,BlockType.Stone), new BlockOverride( 0,2, 4,BlockType.Stone),
                new BlockOverride( 0,0,-4,BlockType.Stone), new BlockOverride( 0,1,-4,BlockType.Stone), new BlockOverride( 0,2,-4,BlockType.Stone),
                new BlockOverride( 3,0, 3,BlockType.Stone), new BlockOverride( 3,1, 3,BlockType.Stone),
                new BlockOverride(-3,0, 3,BlockType.Stone), new BlockOverride(-3,1, 3,BlockType.Stone),
                new BlockOverride( 3,0,-3,BlockType.Stone), new BlockOverride( 3,1,-3,BlockType.Stone),
                new BlockOverride(-3,0,-3,BlockType.Stone), new BlockOverride(-3,1,-3,BlockType.Stone),
            
                new BlockOverride( 0,0, 0,BlockType.Stone),
                new BlockOverride( 1,0, 0,BlockType.Stone), new BlockOverride(-1,0, 0,BlockType.Stone),
                new BlockOverride( 0,0, 1,BlockType.Stone), new BlockOverride( 0,0,-1,BlockType.Stone),
                new BlockOverride( 0,1, 0,BlockType.Glowstone),
            }
        };

    
        private static readonly StructureDef[] _structures =
        {
            GlowstonePillar,
            GlowstoneLantern,
            StoneRuin,
            DesertObelisk,
            StoneCircle,
        };

        public static List<StructurePlacement> GetStructuresForChunk(
            int chunkX, int chunkY, int chunkZ, int chunkSize,
            int seed, Func<float, float, int> getTerrainHeight)
        {
            var result = new List<StructurePlacement>();

            for (int ox = -OVERLAP_RADIUS; ox <= OVERLAP_RADIUS; ox++)
            for (int oz = -OVERLAP_RADIUS; oz <= OVERLAP_RADIUS; oz++)
            {
                int cx = chunkX + ox, cz = chunkZ + oz;

                foreach (var def in _structures)
                {
                
                    float candidateRoll = Hash3f(cx, cz, def.Name.GetHashCode() ^ seed);
                    if (candidateRoll > def.SpawnChance) continue;

                
                    int jx = (int)(Hash3f(cx + 1, cz,     seed ^ 0xABCD) * chunkSize);
                    int jz = (int)(Hash3f(cx,     cz + 1, seed ^ 0x1234) * chunkSize);

                    int wx = cx * chunkSize + jx;
                    int wz = cz * chunkSize + jz;

                    int wy = getTerrainHeight(wx, wz);

                
                    if (!IsSpacingOk(def, wx, wz, cx, cz, chunkSize, seed)) continue;

                    result.Add(new StructurePlacement(def.Name, wx, wy, wz, def.Blocks));
                }
            }

            return result;
        }

        public static void Apply(
            byte[,,] blocks,
            StructurePlacement placement,
            int chunkX, int chunkY, int chunkZ, int chunkSize,
            byte[] surfaceBlocks)  
        {
            int originX = placement.WorldX - chunkX * chunkSize;
            int originY = placement.WorldY + 1 - chunkY * chunkSize;  
            int originZ = placement.WorldZ - chunkZ * chunkSize;

            foreach (var b in placement.Blocks)
            {
                int lx = originX + b.Dx;
                int ly = originY + b.Dy;
                int lz = originZ + b.Dz;

                if (lx < 0 || lx >= chunkSize) continue;
                if (ly < 0 || ly >= chunkSize) continue;
                if (lz < 0 || lz >= chunkSize) continue;

                if (b.OnlyIfAir && blocks[lx, ly, lz] != BlockType.Air) continue;

                blocks[lx, ly, lz] = b.BlockType;
            }
        }
        private static bool IsSpacingOk(StructureDef def, int wx, int wz,
                                         int selfCx, int selfCz,
                                         int chunkSize, int seed)
        {
            int halfSpacing = def.MinSpacing / chunkSize + 1;

            for (int ox = -halfSpacing; ox <= halfSpacing; ox++)
            for (int oz = -halfSpacing; oz <= halfSpacing; oz++)
            {
                if (ox == 0 && oz == 0) continue;
                int cx2 = selfCx + ox, cz2 = selfCz + oz;

                float roll2 = Hash3f(cx2, cz2, def.Name.GetHashCode() ^ seed);
                if (roll2 > def.SpawnChance) continue;

                int jx2 = (int)(Hash3f(cx2 + 1, cz2,     seed ^ 0xABCD) * chunkSize);
                int jz2 = (int)(Hash3f(cx2,     cz2 + 1, seed ^ 0x1234) * chunkSize);

                int wx2 = cx2 * chunkSize + jx2;
                int wz2 = cz2 * chunkSize + jz2;

                float dx = wx - wx2, dz = wz - wz2;
                if (dx * dx + dz * dz < (float)def.MinSpacing * def.MinSpacing)
                    return false;
            }
            return true;
        }

    
        private static float Hash3f(int x, int z, int seed)
        {
            int h = unchecked(seed ^ (x * 374761393) ^ (z * 668265263));
            h = unchecked((h ^ (h >> 13)) * 1274126177);
            h ^= h >> 16;
            return (float)((uint)h) / (float)uint.MaxValue;
        }
    }
}