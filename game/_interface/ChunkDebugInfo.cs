using System;
namespace game
{
    public class ChunkDebugInfo
    {
        public long MeshGenerationTimeMs { get; set; }
        public long GreedyMeshingTimeMs { get; set; }
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }
        public int TriangleCount => IndexCount / 3;
        public DateTime LastMeshGenerationTime { get; set; }
        public override string ToString()
        {
            return $"Mesh Gen: {MeshGenerationTimeMs}ms | Greedy: {GreedyMeshingTimeMs}ms | Verts: {VertexCount} | Tris: {TriangleCount}";
        }
    }
}
