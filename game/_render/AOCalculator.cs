using System.Runtime.CompilerServices;
namespace game
{
    public class AmbientOcclusionCalculator
    {
        private readonly IAOProvider _provider;
        private readonly float _aoStrength;


        private static readonly int[] TopOffsets = { -1, -1, 1, -1, -1, 1, 1, 1 };
        private static readonly int[] BottomOffsets = { -1, -1, 1, -1, -1, 1, 1, 1 };
        private static readonly int[] RightOffsets = { 1, 1, 1, -1, -1, 1, -1, -1 };
        private static readonly int[] LeftOffsets = { 1, -1, 1, 1, -1, -1, -1, 1 };
        private static readonly int[] ForwardOffsets = { -1, 1, 1, 1, -1, -1, 1, -1 };
        private static readonly int[] BackwardOffsets = { 1, 1, -1, 1, 1, -1, -1, -1 };

        public AmbientOcclusionCalculator(IAOProvider provider, float aoStrength = 0.6f)
        {
            _provider = provider;
            _aoStrength = aoStrength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CalculateForFace(int x, int y, int z, int nx, int ny, int nz, float[] aoBuffer)
        {
            var (tx1, ty1, tz1, tx2, ty2, tz2) = GetTangentVectors(nx, ny, nz);
            int[] offsets = GetVertexOffsets(ny, nx, nz);
            for (int i = 0; i < 4; i++)
            {
                int t1Sign = offsets[i * 2];
                int t2Sign = offsets[i * 2 + 1];
                aoBuffer[i] = CalculateVertexAO(
                    x, y, z,
                    nx, ny, nz,
                    tx1 * t1Sign, ty1 * t1Sign, tz1 * t1Sign,
                    tx2 * t2Sign, ty2 * t2Sign, tz2 * t2Sign);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateVertexAO(
            int x, int y, int z,
            int nx, int ny, int nz,
            int tx1, int ty1, int tz1,
            int tx2, int ty2, int tz2)
        {

            int fx = x + nx;
            int fy = y + ny;
            int fz = z + nz;
            bool side1 = _provider.IsSolid(fx + tx1, fy + ty1, fz + tz1);
            bool side2 = _provider.IsSolid(fx + tx2, fy + ty2, fz + tz2);

            if (side1 && side2)
                return 1f - _aoStrength;
            bool corner = _provider.IsSolid(fx + tx1 + tx2, fy + ty1 + ty2, fz + tz1 + tz2);
            int count = (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);
            return 1f - (count / 3f) * _aoStrength;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int tx1, int ty1, int tz1, int tx2, int ty2, int tz2)
            GetTangentVectors(int nx, int ny, int nz)
        {
            if (ny != 0) return (1, 0, 0, 0, 0, 1);
            if (nx != 0) return (0, 1, 0, 0, 0, 1);
            return (1, 0, 0, 0, 1, 0);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int[] GetVertexOffsets(int ny, int nx, int nz)
        {
            if (ny > 0) return TopOffsets;
            if (ny < 0) return BottomOffsets;
            if (nx > 0) return RightOffsets;
            if (nx < 0) return LeftOffsets;
            if (nz > 0) return ForwardOffsets;
            return BackwardOffsets;
        }
    }

    public interface IAOProvider
    {


        bool IsSolid(int x, int y, int z);
    }
}