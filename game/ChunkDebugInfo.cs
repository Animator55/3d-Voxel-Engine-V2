using System;

namespace game
{
    /// <summary>
    /// Datos de debug y profiling para un chunk.
    /// Captura tiempos de generación y estadísticas del mesh.
    /// </summary>
    public class ChunkDebugInfo
    {
        public long MeshGenerationTimeMs { get; set; }        // Tiempo total de mesh generation
        public long GreedyMeshingTimeMs { get; set; }         // Tiempo del greedy meshing
        public int VertexCount { get; set; }                  // Cantidad de vértices
        public int IndexCount { get; set; }                   // Cantidad de índices (tris * 3)
        public int TriangleCount => IndexCount / 3;           // Cantidad de triángulos
        public DateTime LastMeshGenerationTime { get; set; }  // Cuándo se generó la malla

        public override string ToString()
        {
            return $"Mesh Gen: {MeshGenerationTimeMs}ms | Greedy: {GreedyMeshingTimeMs}ms | Verts: {VertexCount} | Tris: {TriangleCount}";
        }
    }
}
