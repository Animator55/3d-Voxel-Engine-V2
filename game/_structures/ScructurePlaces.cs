using System;
using System.Collections.Generic;

namespace game
{
    public static class StructurePlacer
    {
        // OVERLAP_RADIUS debe cubrir el MinSpacing más grande.
        // Con chunkSize=32 y MinSpacing máximo de ~180: 180/32+1 ≈ 7.
        private const int OVERLAP_RADIUS = 7;

        public static List<StructurePlacement> GetStructuresForChunk(
            int chunkX, int chunkY, int chunkZ, int chunkSize,
            int seed, Func<float, float, int> getTerrainHeight,
            Func<float, float, byte> getSurfaceBlock)
        {
            var result = new List<StructurePlacement>();

            for (int ox = -OVERLAP_RADIUS; ox <= OVERLAP_RADIUS; ox++)
                for (int oz = -OVERLAP_RADIUS; oz <= OVERLAP_RADIUS; oz++)
                {
                    int cx = chunkX + ox, cz = chunkZ + oz;

                    foreach (var def in Structures.All)
                    {
                        float candidateRoll = Hash3f(cx, cz, def.Name.GetHashCode() ^ seed);
                        if (candidateRoll > def.SpawnChance) continue;

                        int jx = (int)(Hash3f(cx + 1, cz, seed ^ 0xABCD) * chunkSize);
                        int jz = (int)(Hash3f(cx, cz + 1, seed ^ 0x1234) * chunkSize);

                        int wx = cx * chunkSize + jx;
                        int wz = cz * chunkSize + jz;
                        int wy = getTerrainHeight(wx, wz);

                        // Verificar que al menos parte de la estructura caiga en este chunk Y.
                        int structBaseY = wy + 1;
                        int chunkWorldYMin = chunkY * chunkSize;
                        int chunkWorldYMax = chunkWorldYMin + chunkSize - 1;

                        int maxDy = 0;
                        foreach (var b in def.Blocks)
                            if (b.Dy > maxDy) maxDy = b.Dy;

                        int structTopY = structBaseY + maxDy;

                        if (structTopY < chunkWorldYMin || structBaseY > chunkWorldYMax) continue;

                        // ValidSurfaces check
                        byte surface = getSurfaceBlock(wx, wz);
                        bool validSurface = false;
                        foreach (var vs in def.ValidSurfaces)
                            if (vs == surface) { validSurface = true; break; }
                        if (!validSurface) continue;

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
            int halfSpacing = Math.Min(def.MinSpacing / chunkSize + 1, OVERLAP_RADIUS);

            for (int ox = -halfSpacing; ox <= halfSpacing; ox++)
                for (int oz = -halfSpacing; oz <= halfSpacing; oz++)
                {
                    if (ox == 0 && oz == 0) continue;
                    int cx2 = selfCx + ox, cz2 = selfCz + oz;

                    float roll2 = Hash3f(cx2, cz2, def.Name.GetHashCode() ^ seed);
                    if (roll2 > def.SpawnChance) continue;

                    int jx2 = (int)(Hash3f(cx2 + 1, cz2, seed ^ 0xABCD) * chunkSize);
                    int jz2 = (int)(Hash3f(cx2, cz2 + 1, seed ^ 0x1234) * chunkSize);

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