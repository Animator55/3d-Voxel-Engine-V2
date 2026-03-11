// ChunkManager_VoxelLitDraw.cs
// Drop this file into your project alongside ChunkManager.cs.
// It adds a Draw() overload for VoxelLitEffect without touching
// the original ChunkManager source.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace game
{
    public partial class ChunkManager
    {
        public void Draw(VoxelLitEffect effect,
                         BoundingFrustum cameraFrustum,
                         Vector3Int?     currentChunk = null,
                         bool            wireframeOnly = false)
        {
            lock (_chunkLock)
            {
                foreach (var chunk in _chunks.Values.ToList())
                {
                    if (wireframeOnly && currentChunk.HasValue &&
                        (chunk.X != currentChunk.Value.X ||
                         chunk.Z != currentChunk.Value.Z)) continue;

                    if (!chunk.HasMesh) continue;
                    if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum)) continue;

                    effect.World = Matrix.CreateTranslation(
                        chunk.X * _chunkSize,
                        chunk.Y * _chunkSize,
                        chunk.Z * _chunkSize);

                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);
                }

                foreach (var chunk in _lowPolyChunks.Values.ToList())
                {
                    if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum)) continue;

                    var hqPos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                    if (_chunks.TryGetValue(hqPos, out var hq) && hq.HasMesh) continue;

                    int renderLevel = FindBestAvailableLevel(chunk, chunk.ActiveLevel);
                    if (!chunk.HasMeshForLevel(renderLevel)) continue;

                    int saved = chunk.ActiveLevel;
                    chunk.ActiveLevel = renderLevel;

                    effect.World = Matrix.CreateTranslation(
                        chunk.X * _chunkSize,
                        chunk.Y * _chunkSize,
                        chunk.Z * _chunkSize);

                    effect.CurrentTechnique.Passes[0].Apply();
                    chunk.Draw(_graphicsDevice, cameraFrustum);
                    chunk.ActiveLevel = saved;
                }

                if (_enableVeryLowPoly)
                {
                    foreach (var chunk in _veryLowPolyChunks.Values.ToList())
                    {
                        if (!chunk.HasMesh) continue;

                        var pos = new Vector3Int(chunk.X, chunk.Y, chunk.Z);
                        if (_chunks.TryGetValue(pos, out var hq) && hq.HasMesh) continue;

                        if (_lowPolyChunks.TryGetValue(pos, out var lp))
                        {
                            int best = FindBestAvailableLevel(lp, lp.ActiveLevel);
                            if (lp.HasMeshForLevel(best)) continue;
                        }

                        if (!IsChunkInFrustum(chunk.X, chunk.Y, chunk.Z, cameraFrustum)) continue;

                        effect.World = Matrix.CreateTranslation(
                            chunk.X * _chunkSize,
                            chunk.Y * _chunkSize,
                            chunk.Z * _chunkSize);

                        effect.CurrentTechnique.Passes[0].Apply();
                        chunk.Draw(_graphicsDevice, cameraFrustum);
                    }
                }
            }
        }
    }
}