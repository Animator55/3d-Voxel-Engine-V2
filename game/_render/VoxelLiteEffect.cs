using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    /// <summary>
    /// Thin C# wrapper around VoxelLit.fx.
    /// Mirrors the subset of BasicEffect used by ChunkManager/Game1,
    /// so the swap is near-mechanical.
    ///
    /// Point lights (emissive blocks + camera) live entirely in shader
    /// uniforms — no chunk rebuilds ever needed.
    /// </summary>
    public sealed class VoxelLitEffect : IDisposable
    {
        // ── constants ────────────────────────────────────────────────
        public const int MAX_POINT_LIGHTS = 8;

        // ── backing Effect ────────────────────────────────────────────
        private readonly Effect _fx;

        // ── cached parameter handles (avoid string lookup every frame) ─
        private readonly EffectParameter _pWorld;
        private readonly EffectParameter _pView;
        private readonly EffectParameter _pProjection;

        private readonly EffectParameter _pAmbient;
        private readonly EffectParameter _pDirDir;
        private readonly EffectParameter _pDirDiff;
        private readonly EffectParameter _pDirEnabled;

        private readonly EffectParameter _pCamPos;
        private readonly EffectParameter _pCamLightEnabled;
        private readonly EffectParameter _pCamRadius;
        private readonly EffectParameter _pCamIntensity;
        private readonly EffectParameter _pCamColor;

        private readonly EffectParameter _pPlPos;
        private readonly EffectParameter _pPlColor;
        private readonly EffectParameter _pPlRadius;
        private readonly EffectParameter _pPlIntensity;
        private readonly EffectParameter _pPlCount;

        private readonly EffectParameter _pFogEnabled;
        private readonly EffectParameter _pFogStart;
        private readonly EffectParameter _pFogEnd;
        private readonly EffectParameter _pFogColor;

        // ── point-light staging arrays (avoid per-frame allocation) ───
        private readonly Vector3[] _plPos       = new Vector3[MAX_POINT_LIGHTS];
        private readonly Vector3[] _plColor     = new Vector3[MAX_POINT_LIGHTS];
        private readonly float[]   _plRadius    = new float  [MAX_POINT_LIGHTS];
        private readonly float[]   _plIntensity = new float  [MAX_POINT_LIGHTS];
        private int _plCount;

        // ── public properties ─────────────────────────────────────────

        // Matrices
        public Matrix World      { set => _pWorld     .SetValue(value); }
        public Matrix View       { set => _pView      .SetValue(value); }
        public Matrix Projection { set => _pProjection.SetValue(value); }

        // Ambient / sun  (matches naming used in Game1 / ProceduralSkybox)
        public Vector3 AmbientLightColor
        {
            set => _pAmbient.SetValue(value);
        }

        private bool _dirEnabled = true;
        public bool DirectionalLightEnabled
        {
            get => _dirEnabled;
            set { _dirEnabled = value; _pDirEnabled.SetValue(value); }
        }

        /// <summary>Direction the light travels (i.e. -sunDir in BasicEffect terms).</summary>
        public Vector3 DirectionalLightDirection
        {
            set => _pDirDir.SetValue(value);
        }

        public Vector3 DirectionalLightDiffuse
        {
            set => _pDirDiff.SetValue(value);
        }

        // Fog
        private bool _fogEnabled = true;
        public bool FogEnabled
        {
            get => _fogEnabled;
            set { _fogEnabled = value; _pFogEnabled.SetValue(value); }
        }
        public float FogStart { set => _pFogStart.SetValue(value); }
        public float FogEnd   { set => _pFogEnd  .SetValue(value); }
        public Vector3 FogColor { set => _pFogColor.SetValue(value); }

        // Camera light
        private bool _cameraLightEnabled = true;
        public bool CameraLightEnabled
        {
            get => _cameraLightEnabled;
            set { _cameraLightEnabled = value; _pCamLightEnabled.SetValue(value); }
        }
        public float   CameraLightRadius    { set => _pCamRadius   .SetValue(value); }
        public float   CameraLightIntensity { set => _pCamIntensity.SetValue(value); }
        public Vector3 CameraLightColor     { set => _pCamColor    .SetValue(value); }

        /// <summary>
        /// Call once per frame before drawing chunks.
        /// Uploads camera world position so the shader can compute distances.
        /// </summary>
        public void SetCameraPosition(Vector3 pos) => _pCamPos.SetValue(pos);

        // ── technique / pass ──────────────────────────────────────────
        public EffectTechnique CurrentTechnique => _fx.CurrentTechnique;

        // ── ctor ──────────────────────────────────────────────────────
        public VoxelLitEffect(Effect loadedEffect)
        {
            _fx = loadedEffect ?? throw new ArgumentNullException(nameof(loadedEffect));

            // matrices
            _pWorld      = _fx.Parameters["World"];
            _pView       = _fx.Parameters["View"];
            _pProjection = _fx.Parameters["Projection"];

            // sun / ambient
            _pAmbient    = _fx.Parameters["AmbientLightColor"];
            _pDirDir     = _fx.Parameters["DirLight0Direction"];
            _pDirDiff    = _fx.Parameters["DirLight0Diffuse"];
            _pDirEnabled = _fx.Parameters["DirLight0Enabled"];

            // camera light
            _pCamPos          = _fx.Parameters["CameraPosition"];
            _pCamLightEnabled = _fx.Parameters["CameraLightEnabled"];
            _pCamRadius       = _fx.Parameters["CameraLightRadius"];
            _pCamIntensity    = _fx.Parameters["CameraLightIntensity"];
            _pCamColor        = _fx.Parameters["CameraLightColor"];

            // point lights
            _pPlPos       = _fx.Parameters["PointLightPos"];
            _pPlColor     = _fx.Parameters["PointLightColor"];
            _pPlRadius    = _fx.Parameters["PointLightRadius"];
            _pPlIntensity = _fx.Parameters["PointLightIntensity"];
            _pPlCount     = _fx.Parameters["PointLightCount"];

            // fog
            _pFogEnabled = _fx.Parameters["FogEnabled"];
            _pFogStart   = _fx.Parameters["FogStart"];
            _pFogEnd     = _fx.Parameters["FogEnd"];
            _pFogColor   = _fx.Parameters["FogColor"];

            // safe defaults
            _pCamRadius   .SetValue(18f);
            _pCamIntensity.SetValue(1.4f);
            _pCamColor    .SetValue(new Vector3(1f, 0.92f, 0.75f));
            _pCamLightEnabled.SetValue(true);
            _pPlCount.SetValue(0);
        }

        // ── Point-light management ────────────────────────────────────

        /// <summary>
        /// Clears the emissive-block list.  Call at the start of each frame
        /// if you rebuild the list dynamically, or whenever blocks change.
        /// </summary>
        public void ClearPointLights()
        {
            _plCount = 0;
        }

        /// <summary>
        /// Adds one emissive point light.  Silently ignored if MAX_POINT_LIGHTS reached.
        /// <paramref name="worldCenter"/> is the centre of the emissive block in world space.
        /// </summary>
        public void AddPointLight(Vector3 worldCenter, Vector3 color, float radius, float intensity)
        {
            if (_plCount >= MAX_POINT_LIGHTS) return;
            _plPos      [_plCount] = worldCenter;
            _plColor    [_plCount] = color;
            _plRadius   [_plCount] = radius;
            _plIntensity[_plCount] = intensity;
            _plCount++;
        }

        /// <summary>
        /// Flushes the point-light staging arrays to the GPU.
        /// Call once per frame AFTER all AddPointLight calls, before drawing.
        /// </summary>
        public void UploadPointLights()
        {
            _pPlCount.SetValue(_plCount);
            if (_plCount > 0)
            {
                _pPlPos      .SetValue(_plPos);
                _pPlColor    .SetValue(_plColor);
                _pPlRadius   .SetValue(_plRadius);
                _pPlIntensity.SetValue(_plIntensity);
            }
        }

        // ── bridge helpers used by ApplyLightingToEffect ──────────────

        /// <summary>
        /// Mirrors the BasicEffect bridge used in ProceduralSkybox.ApplyLightingToEffect.
        /// Call this from a small extension / overload so Skybox doesn't need refactoring.
        /// </summary>
        public void ApplyFromSkybox(ProceduralSkybox skybox)
        {
            Vector3 sunDir = skybox.GetSunDirection();
            float sunH = sunDir.Y;

            float Smoothstep(float e0, float e1, float x)
            {
                float t = MathHelper.Clamp((x - e0) / (e1 - e0), 0f, 1f);
                return t * t * (3f - 2f * t);
            }

            float dayFactor    = Smoothstep(-0.25f, 0.30f, sunH);
            float sunsetFactor = Smoothstep(-0.30f, 0.02f, sunH)
                               * Smoothstep( 0.45f, 0.05f, sunH);

            Vector3 ambDay    = new Vector3(0.55f, 0.58f, 0.65f);
            Vector3 ambSunset = new Vector3(0.45f, 0.28f, 0.18f);
            Vector3 ambNight  = new Vector3(0.10f, 0.10f, 0.15f);

            Vector3 ambColor = Vector3.Lerp(ambNight, ambDay, dayFactor);
            ambColor         = Vector3.Lerp(ambColor, ambSunset, sunsetFactor);
            AmbientLightColor = ambColor;

            DirectionalLightDirection = Vector3.Normalize(-sunDir);

            Vector3 diffDay    = new Vector3(0.90f, 0.88f, 0.80f);
            Vector3 diffSunset = new Vector3(1.00f, 0.55f, 0.20f);
            Vector3 diffNight  = Vector3.Zero;

            Vector3 diffColor = Vector3.Lerp(diffNight, diffDay, dayFactor);
            diffColor         = Vector3.Lerp(diffColor, diffSunset, sunsetFactor * 0.8f);
            DirectionalLightDiffuse   = diffColor;
            DirectionalLightEnabled   = sunH > -0.25f;
        }

        // ── IDisposable ───────────────────────────────────────────────
        public void Dispose() => _fx.Dispose();
    }
}