using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    public class WaterEffect : IDisposable
    {
        private readonly Effect _fx;
        private EffectParameter P(string n) => _fx.Parameters[n];

        private readonly EffectParameter _pWorld, _pView, _pProj;
        private readonly EffectParameter _pTime;
        private readonly EffectParameter _pDirLightDir, _pDirLightColor, _pAmbient, _pDirEnabled;
        private readonly EffectParameter _pFogEnabled, _pFogStart, _pFogEnd, _pFogColor;
        private readonly EffectParameter _pCameraPos;
        private readonly EffectParameter _pWaveHeight, _pWaveSpeed;
        private readonly EffectParameter _pWaterAlpha, _pSpecPow, _pSpecStr;
        private readonly EffectParameter _pFresnelBias, _pFresnelScale, _pFresnelPow;
        private readonly EffectParameter _pPLPos, _pPLCol, _pPLRad, _pPLInt, _pPLCount;
        private readonly EffectParameter _pSkyZenith, _pSkyHorizon, _pSunColor, _pSunSharpness, _pSunStr;

        private const int MAX_LIGHTS = 8;
        private readonly Vector3[] _plPos = new Vector3[MAX_LIGHTS];
        private readonly Vector3[] _plCol = new Vector3[MAX_LIGHTS];
        private readonly float[]   _plRad = new float[MAX_LIGHTS];
        private readonly float[]   _plInt = new float[MAX_LIGHTS];
        private int _plCount;

        private Matrix  _world, _view, _proj;
        private float   _time;
        private Vector3 _ambientLightColor;
        private bool    _dirEnabled;
        private Vector3 _dirDir, _dirColor;
        private bool    _fogEnabled;
        private float   _fogStart, _fogEnd;
        private Vector3 _fogColor;
        private float   _waveHeight, _waveSpeed;
        private float   _waterAlpha, _specPow, _specStr;
        private float   _fresnelBias, _fresnelScale, _fresnelPow;

        public WaterEffect(Effect fx)
        {
            _fx = fx ?? throw new ArgumentNullException(nameof(fx));

            _pWorld = P("World");
            _pView  = P("View");
            _pProj  = P("Projection");
            _pTime  = P("Time");

            _pDirLightDir   = P("DirectionalLightDir");
            _pDirLightColor = P("DirectionalLightColor");
            _pAmbient       = P("AmbientLightColor");
            _pDirEnabled    = P("DirectionalLightEnabled");

            _pFogEnabled = P("FogEnabled");
            _pFogStart   = P("FogStart");
            _pFogEnd     = P("FogEnd");
            _pFogColor   = P("FogColor");

            _pCameraPos    = P("CameraPosition");
            _pWaveHeight   = P("WaveHeight");
            _pWaveSpeed    = P("WaveSpeed");
            _pWaterAlpha   = P("WaterAlpha");
            _pSpecPow      = P("SpecularPower");
            _pSpecStr      = P("SpecularStr");
            _pFresnelBias  = P("FresnelBias");
            _pFresnelScale = P("FresnelScale");
            _pFresnelPow   = P("FresnelPow");

            _pPLPos   = P("PointLightPositions");
            _pPLCol   = P("PointLightColors");
            _pPLRad   = P("PointLightRadii");
            _pPLInt   = P("PointLightIntensities");
            _pPLCount = P("PointLightCount");

            _pSkyZenith    = P("SkyColorZenith");
            _pSkyHorizon   = P("SkyColorHorizon");
            _pSunColor     = P("SunColor");
            _pSunSharpness = P("SunSharpness");
            _pSunStr       = P("SunStr");

            // Defaults
            WaveHeight    = 0.08f;
            WaveSpeed     = 0.6f;
            WaterAlpha    = 1.0f;
            SpecularPower = 120f;
            SpecularStr   = 2.2f;
            FresnelBias   = 0.08f;
            FresnelScale  = 0.92f;
            FresnelPow    = 3.0f;

            AmbientLightColor       = new Vector3(0.4f, 0.4f, 0.4f);
            DirectionalLightEnabled = true;
            DirectionalLightDir     = Vector3.Normalize(new Vector3(0.6f, 1f, 0.4f));
            DirectionalLightColor   = new Vector3(1f, 0.95f, 0.85f);

            FogEnabled = true;
            FogStart   = 200f;
            FogEnd     = 400f;
            FogColor   = new Vector3(0.5f, 0.7f, 1f);

            _pSkyZenith   ?.SetValue(new Vector3(0.10f, 0.40f, 0.85f));
            _pSkyHorizon  ?.SetValue(new Vector3(0.50f, 0.72f, 1.00f));
            _pSunColor    ?.SetValue(new Vector3(1.0f,  0.95f, 0.80f));
            _pSunSharpness?.SetValue(320f);
            _pSunStr      ?.SetValue(3.5f);
        }

        public Matrix World
        {
            get => _world;
            set { _world = value; _pWorld?.SetValue(value); }
        }
        public Matrix View
        {
            get => _view;
            set { _view = value; _pView?.SetValue(value); }
        }
        public Matrix Projection
        {
            get => _proj;
            set { _proj = value; _pProj?.SetValue(value); }
        }

        public float Time
        {
            get => _time;
            set { _time = value; _pTime?.SetValue(value); }
        }

        public Vector3 AmbientLightColor
        {
            get => _ambientLightColor;
            set { _ambientLightColor = value; _pAmbient?.SetValue(value); }
        }
        public bool DirectionalLightEnabled
        {
            get => _dirEnabled;
            set { _dirEnabled = value; _pDirEnabled?.SetValue(value); }
        }
        public Vector3 DirectionalLightDir
        {
            get => _dirDir;
            set { _dirDir = value; _pDirLightDir?.SetValue(value); }
        }
        public Vector3 DirectionalLightColor
        {
            get => _dirColor;
            set { _dirColor = value; _pDirLightColor?.SetValue(value); }
        }

        public bool FogEnabled
        {
            get => _fogEnabled;
            set { _fogEnabled = value; _pFogEnabled?.SetValue(value); }
        }
        public float FogStart
        {
            get => _fogStart;
            set { _fogStart = value; _pFogStart?.SetValue(value); }
        }
        public float FogEnd
        {
            get => _fogEnd;
            set { _fogEnd = value; _pFogEnd?.SetValue(value); }
        }
        public Vector3 FogColor
        {
            get => _fogColor;
            set { _fogColor = value; _pFogColor?.SetValue(value); }
        }

        public float WaveHeight
        {
            get => _waveHeight;
            set { _waveHeight = value; _pWaveHeight?.SetValue(value); }
        }
        public float WaveSpeed
        {
            get => _waveSpeed;
            set { _waveSpeed = value; _pWaveSpeed?.SetValue(value); }
        }
        public float WaterAlpha
        {
            get => _waterAlpha;
            set { _waterAlpha = value; _pWaterAlpha?.SetValue(value); }
        }
        public float SpecularPower
        {
            get => _specPow;
            set { _specPow = value; _pSpecPow?.SetValue(value); }
        }
        public float SpecularStr
        {
            get => _specStr;
            set { _specStr = value; _pSpecStr?.SetValue(value); }
        }
        public float FresnelBias
        {
            get => _fresnelBias;
            set { _fresnelBias = value; _pFresnelBias?.SetValue(value); }
        }
        public float FresnelScale
        {
            get => _fresnelScale;
            set { _fresnelScale = value; _pFresnelScale?.SetValue(value); }
        }
        public float FresnelPow
        {
            get => _fresnelPow;
            set { _fresnelPow = value; _pFresnelPow?.SetValue(value); }
        }

        public void SetCameraPosition(Vector3 pos) => _pCameraPos?.SetValue(pos);

        public void ApplyFromSkybox(ProceduralSkybox sky)
        {
            Vector3 sunDir = sky.GetSunDirection();
            DirectionalLightDir = sunDir;

            float sunH      = sunDir.Y;
            float dayFactor    = Smoothstep(-0.25f, 0.30f, sunH);
            float sunsetFactor = Smoothstep(-0.30f, 0.02f, sunH)
                               * Smoothstep( 0.45f, 0.05f, sunH);

            // Ambient
            Vector3 ambDay    = new Vector3(0.55f, 0.58f, 0.65f);
            Vector3 ambSunset = new Vector3(0.45f, 0.28f, 0.18f);
            Vector3 ambNight  = new Vector3(0.10f, 0.10f, 0.15f);
            Vector3 ambient   = Vector3.Lerp(ambNight,  ambDay,    dayFactor);
            ambient           = Vector3.Lerp(ambient,   ambSunset, sunsetFactor);
            AmbientLightColor = ambient;

            // Diffuse
            Vector3 diffDay    = new Vector3(0.90f, 0.88f, 0.80f);
            Vector3 diffSunset = new Vector3(1.00f, 0.55f, 0.20f);
            Vector3 diff       = Vector3.Lerp(Vector3.Zero, diffDay,    dayFactor);
            diff               = Vector3.Lerp(diff,         diffSunset, sunsetFactor * 0.8f);
            DirectionalLightColor   = diff;
            DirectionalLightEnabled = sunH > -0.25f;

            // Sky reflection colors
            Vector3 zenithDay    = new Vector3(0.10f, 0.40f, 0.85f);
            Vector3 zenithSunset = new Vector3(0.55f, 0.25f, 0.15f);
            Vector3 zenithNight  = new Vector3(0.03f, 0.05f, 0.15f);
            Vector3 zenith = Vector3.Lerp(zenithNight, zenithDay,    dayFactor);
            zenith         = Vector3.Lerp(zenith,      zenithSunset, sunsetFactor);

            Vector3 horizDay    = new Vector3(0.50f, 0.72f, 1.00f);
            Vector3 horizSunset = new Vector3(0.95f, 0.45f, 0.15f);
            Vector3 horizNight  = new Vector3(0.05f, 0.08f, 0.20f);
            Vector3 horiz = Vector3.Lerp(horizNight, horizDay,    dayFactor);
            horiz         = Vector3.Lerp(horiz,      horizSunset, sunsetFactor);

            _pSkyZenith ?.SetValue(zenith);
            _pSkyHorizon?.SetValue(horiz);
            _pSunStr    ?.SetValue(2.5f + sunsetFactor * 1.5f);
            _pSunColor  ?.SetValue(DirectionalLightColor);

            FogColor = sky.GetFogColor();
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - edge0) / (edge1 - edge0)));
            return t * t * (3f - 2f * t);
        }

        public void ClearPointLights() => _plCount = 0;

        public void AddPointLight(Vector3 position, Vector3 color, float radius, float intensity)
        {
            if (_plCount >= MAX_LIGHTS) return;
            _plPos[_plCount] = position;
            _plCol[_plCount] = color;
            _plRad[_plCount] = radius;
            _plInt[_plCount] = intensity;
            _plCount++;
        }

        public void UploadPointLights()
        {
            _pPLPos  ?.SetValue(_plPos);
            _pPLCol  ?.SetValue(_plCol);
            _pPLRad  ?.SetValue(_plRad);
            _pPLInt  ?.SetValue(_plInt);
            _pPLCount?.SetValue(_plCount);
        }

        public EffectTechnique CurrentTechnique => _fx.CurrentTechnique;
        public void Apply() => _fx.CurrentTechnique.Passes[0].Apply();
        public void Dispose() => _fx?.Dispose();
    }
}