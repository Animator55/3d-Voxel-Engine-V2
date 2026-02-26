using System.Runtime.CompilerServices;

namespace game
{
    /// <summary>
    /// Calcula el Ambient Occlusion (AO) por vértice para las caras de un chunk.
    ///
    /// CÓMO FUNCIONA:
    /// Para cada vértice de una cara, se miran los 2 bloques "lado" y 1 bloque "esquina"
    /// adyacentes a ese vértice. Cuantos más bloques sólidos haya, más oscuro se pone el vértice.
    ///
    /// Resultado: valores [0..1] donde 1.0 = sin oclusión, valores menores = más oscuro.
    /// Se multiplica por el color del vértice al construir la malla.
    /// </summary>
    public class AmbientOcclusionCalculator
    {
        private readonly IAOProvider _provider;
        private readonly float _aoStrength;

        // Offsets de los 4 vértices por cada cara (t1Sign, t2Sign).
        // Cada cara tiene 4 vértices; cada vértice se define por el signo de los dos
        // vectores tangentes de esa cara.
        private static readonly int[] TopOffsets      = { -1, -1,  1, -1, -1,  1,  1,  1 };
        private static readonly int[] BottomOffsets   = { -1, -1,  1, -1, -1,  1,  1,  1 };
        private static readonly int[] RightOffsets    = {  1,  1,  1, -1, -1,  1, -1, -1 };
        private static readonly int[] LeftOffsets     = {  1, -1,  1,  1, -1, -1, -1,  1 };
        private static readonly int[] ForwardOffsets  = { -1,  1,  1,  1, -1, -1,  1, -1 };
        private static readonly int[] BackwardOffsets = {  1,  1, -1,  1,  1, -1, -1, -1 };

        /// <param name="provider">Proveedor que indica si hay un bloque sólido en (x,y,z).</param>
        /// <param name="aoStrength">Intensidad del AO. 0 = sin efecto, 1 = máximo oscurecimiento.</param>
        public AmbientOcclusionCalculator(IAOProvider provider, float aoStrength = 0.6f)
        {
            _provider  = provider;
            _aoStrength = aoStrength;
        }

        /// <summary>
        /// Calcula los 4 valores de AO para la cara de un bloque definida por su normal (nx,ny,nz).
        /// Escribe los resultados en <paramref name="aoBuffer"/> (debe tener longitud >= 4).
        ///
        /// Orden de vértices igual al que usa el MeshBuilder:
        ///   [0]=top-left, [1]=top-right, [2]=bottom-left, [3]=bottom-right  (relativo a la cara)
        /// </summary>
        /// <param name="x">Coordenada local X del bloque.</param>
        /// <param name="y">Coordenada local Y del bloque.</param>
        /// <param name="z">Coordenada local Z del bloque.</param>
        /// <param name="nx">Componente X de la normal de cara (-1, 0 o 1).</param>
        /// <param name="ny">Componente Y de la normal de cara (-1, 0 o 1).</param>
        /// <param name="nz">Componente Z de la normal de cara (-1, 0 o 1).</param>
        /// <param name="aoBuffer">Buffer de salida con los 4 valores AO [0..1].</param>
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

        // ─────────────────────────────────────────────────────────────────────
        // Lógica interna
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Calcula el AO para un solo vértice de la cara.
        ///
        /// Algoritmo estándar de AO voxel por vértice:
        ///  • Si los dos bloques "lado" están sólidos → máxima oclusión posible (atajo).
        ///  • De lo contrario, se cuentan cuántos de los 3 vecinos (lado1, lado2, esquina) son sólidos
        ///    y se escala la oscuridad proporcionalmente.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateVertexAO(
            int x,  int y,  int z,
            int nx, int ny, int nz,
            int tx1, int ty1, int tz1,
            int tx2, int ty2, int tz2)
        {
            // Posición del bloque vecino en la dirección de la normal
            int fx = x + nx;
            int fy = y + ny;
            int fz = z + nz;

            bool side1  = _provider.IsSolid(fx + tx1, fy + ty1, fz + tz1);
            bool side2  = _provider.IsSolid(fx + tx2, fy + ty2, fz + tz2);

            // Atajo: si ambos lados están tapados, la esquina no importa
            if (side1 && side2)
                return 1f - _aoStrength;

            bool corner = _provider.IsSolid(fx + tx1 + tx2, fy + ty1 + ty2, fz + tz1 + tz2);
            int  count  = (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);

            return 1f - (count / 3f) * _aoStrength;
        }

        /// <summary>
        /// Devuelve los vectores tangentes ortogonales a la normal dada.
        /// Se usan para recorrer los vecinos alrededor de cada vértice de la cara.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int tx1, int ty1, int tz1, int tx2, int ty2, int tz2)
            GetTangentVectors(int nx, int ny, int nz)
        {
            if (ny != 0) return (1, 0, 0,  0, 0, 1);   // cara top / bottom
            if (nx != 0) return (0, 1, 0,  0, 0, 1);   // cara right / left
                         return (1, 0, 0,  0, 1, 0);   // cara front / back
        }

        /// <summary>
        /// Devuelve el array de offsets (t1Sign, t2Sign) para los 4 vértices de la cara indicada.
        /// Usa arrays estáticos pre-calculados para evitar allocations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int[] GetVertexOffsets(int ny, int nx, int nz)
        {
            if (ny  > 0) return TopOffsets;
            if (ny  < 0) return BottomOffsets;
            if (nx  > 0) return RightOffsets;
            if (nx  < 0) return LeftOffsets;
            if (nz  > 0) return ForwardOffsets;
                         return BackwardOffsets;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Interfaz
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Interfaz mínima que necesita el calculador de AO:
    /// solo saber si una coordenada contiene un bloque sólido.
    /// </summary>
    public interface IAOProvider
    {
        /// <summary>
        /// Retorna true si la posición (x,y,z) contiene un bloque que ocluye luz.
        /// Debe retornar false para posiciones fuera de límites.
        /// </summary>
        bool IsSolid(int x, int y, int z);
    }
}