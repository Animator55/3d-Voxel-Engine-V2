using System.Runtime.CompilerServices;
namespace game
{
    public class AmbientOcclusionCalculator
    {
        private readonly IAOProvider _provider;

        public AmbientOcclusionCalculator(IAOProvider provider, float aoStrength = 0.6f)
        {
            _provider = provider;
        }

        public void CalculateForFace(
            int bx0, int by0, int bz0,
            int bx1, int by1, int bz1,
            int bx2, int by2, int bz2,
            int bx3, int by3, int bz3,
            int nx, int ny, int nz,
            float[] aoBuffer)
        {
            float strength = GreedyMesher.AoStrength;
            if (strength == 0f)
            {
                aoBuffer[0] = aoBuffer[1] = aoBuffer[2] = aoBuffer[3] = 1f;
                return;
            }

            (int tx1, int ty1, int tz1, int tx2, int ty2, int tz2) = GetTangentVectors(nx, ny, nz);

            aoBuffer[0] = VertexAO(bx0 + nx, by0 + ny, bz0 + nz, -tx1, -ty1, -tz1, -tx2, -ty2, -tz2, strength);
            aoBuffer[1] = VertexAO(bx1 + nx, by1 + ny, bz1 + nz,  tx1,  ty1,  tz1, -tx2, -ty2, -tz2, strength);
            aoBuffer[2] = VertexAO(bx2 + nx, by2 + ny, bz2 + nz,  tx1,  ty1,  tz1,  tx2,  ty2,  tz2, strength);
            aoBuffer[3] = VertexAO(bx3 + nx, by3 + ny, bz3 + nz, -tx1, -ty1, -tz1,  tx2,  ty2,  tz2, strength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float VertexAO(int fx, int fy, int fz,
                                int s1x, int s1y, int s1z,
                                int s2x, int s2y, int s2z,
                                float strength)
        {
            bool side1  = _provider.IsSolid(fx + s1x, fy + s1y, fz + s1z);
            bool side2  = _provider.IsSolid(fx + s2x, fy + s2y, fz + s2z);
            bool corner = _provider.IsSolid(fx + s1x + s2x, fy + s1y + s2y, fz + s1z + s2z);

            if (side1 && side2)
                return 1f - strength;

            int count = (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);
            return 1f - (count / 3f) * strength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int tx1, int ty1, int tz1, int tx2, int ty2, int tz2)
            GetTangentVectors(int nx, int ny, int nz)
        {
            if (ny != 0) return (1, 0, 0, 0, 0, 1);
            if (nx != 0) return (0, 1, 0, 0, 0, 1);
            return (1, 0, 0, 0, 1, 0);
        }
    }

    public interface IAOProvider
    {
        bool IsSolid(int x, int y, int z);
    }
}