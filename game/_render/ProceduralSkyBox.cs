using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
namespace game
{
    public class ProceduralSkybox
    {
        private readonly Effect _skyEffect;
        private readonly VertexBuffer _vb;
        private readonly IndexBuffer _ib;
        private readonly GraphicsDevice _gd;
        private float _time;
        private float _timeOfDay;
        public float DayDurationSeconds = 240f;
        public float TimeOfDay => _timeOfDay;
        public bool IsDayTime => _timeOfDay >= 6f && _timeOfDay <= 18f;
        public ProceduralSkybox(GraphicsDevice device, Effect effect, float startHour = 8f)
        {
            _gd = device;
            _skyEffect = effect;
            _timeOfDay = startHour % 24f;
            (_vb, _ib) = CreateGeometry(device);
        }
        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _time += dt;
            _timeOfDay += dt * (24f / DayDurationSeconds);
            if (_timeOfDay >= 24f) _timeOfDay -= 24f;
        }
        public void Draw(Matrix view, Matrix projection, Vector3 eyePosition)
        {
            // El cubo se traslada al ojo → la view matrix lo lleva de vuelta
            // al origen → el cielo siempre rodea al observador sin importar
            // dónde esté en el mundo.
            Matrix world = Matrix.CreateScale(500f) *
                           Matrix.CreateTranslation(eyePosition);

            Vector3 sunDir = GetSunDirection();
            Vector3 moonDir = -sunDir;

            TrySet("World", world);
            TrySet("View", view);
            TrySet("Projection", projection);
            TrySet("Time", _time);
            TrySet("TimeOfDay", _timeOfDay);
            TrySet("SunDirection", sunDir);
            TrySet("MoonDirection", moonDir);

            var oldDepth = _gd.DepthStencilState;
            var oldRaster = _gd.RasterizerState;
            _gd.DepthStencilState = DepthStencilState.None;
            _gd.RasterizerState = RasterizerState.CullNone;
            _gd.SetVertexBuffer(_vb);
            _gd.Indices = _ib;

            foreach (EffectPass pass in _skyEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 12);
            }

            _gd.DepthStencilState = oldDepth;
            _gd.RasterizerState = oldRaster;
        }
        public void ApplyLightingToEffect(BasicEffect effect)
        {
            Vector3 sunDir = GetSunDirection();
            float sunH = sunDir.Y;

            float dayFactor = Smoothstep(-0.25f, 0.30f, sunH);
            float sunsetFactor = Smoothstep(-0.30f, 0.02f, sunH)
                               * Smoothstep(0.45f, 0.05f, sunH);

            // Brighter night ambient
            Vector3 ambDay = new Vector3(0.55f, 0.58f, 0.65f);
            Vector3 ambSunset = new Vector3(0.45f, 0.28f, 0.18f);
            Vector3 ambNight = new Vector3(0.10f, 0.10f, 0.15f);

            Vector3 ambColor = Vector3.Lerp(ambNight, ambDay, dayFactor);
            ambColor = Vector3.Lerp(ambColor, ambSunset, sunsetFactor);
            effect.AmbientLightColor = ambColor;

            if (effect.DirectionalLight0 != null)
            {
                effect.DirectionalLight0.Direction = Vector3.Normalize(-sunDir);

                Vector3 diffDay = new Vector3(0.90f, 0.88f, 0.80f);
                Vector3 diffSunset = new Vector3(1.00f, 0.55f, 0.20f);
                Vector3 diffNight = Vector3.Zero;

                Vector3 diffColor = Vector3.Lerp(diffNight, diffDay, dayFactor);
                diffColor = Vector3.Lerp(diffColor, diffSunset, sunsetFactor * 0.8f);
                effect.DirectionalLight0.DiffuseColor = diffColor;
                effect.DirectionalLight0.SpecularColor = new Vector3(0.25f, 0.23f, 0.18f) * dayFactor;
                effect.DirectionalLight0.Enabled = sunH > -0.25f;
            }
        }
        public Vector3 GetFogColor()
        {
            Vector3 sunDir = GetSunDirection();
            float sunH = sunDir.Y;
            float dayFactor = Smoothstep(-0.12f, 0.18f, sunH);
            float sunsetFactor = Smoothstep(-0.20f, 0.00f, sunH)
                               * Smoothstep(0.30f, 0.05f, sunH);
            Vector3 fogDay = new Vector3(0.62f, 0.75f, 0.95f);
            Vector3 fogSunset = new Vector3(0.95f, 0.50f, 0.22f);
            Vector3 fogNight = new Vector3(0.01f, 0.01f, 0.04f);
            Vector3 fog = Vector3.Lerp(fogNight, fogDay, dayFactor);
            fog = Vector3.Lerp(fog, fogSunset, sunsetFactor * 0.65f);
            return fog;
        }
        public Vector3 GetSunDirection()
        {
            float angle = (_timeOfDay / 24f) * MathHelper.TwoPi - MathHelper.PiOver2;
            var dir = new Vector3(
                (float)Math.Cos(angle) * 0.2f,
                (float)Math.Sin(angle),
                0.5f);
            dir.Normalize();
            return dir;
        }
        public void SetTimeOfDay(float hours) => _timeOfDay = hours % 24f;
        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = MathHelper.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
        private void TrySet(string name, Matrix v) { _skyEffect.Parameters[name]?.SetValue(v); }
        private void TrySet(string name, float v) { _skyEffect.Parameters[name]?.SetValue(v); }
        private void TrySet(string name, Vector3 v) { _skyEffect.Parameters[name]?.SetValue(v); }
        private static (VertexBuffer vb, IndexBuffer ib) CreateGeometry(GraphicsDevice gd)
        {
            Vector3[] pos =
            {
                new(-1,-1,-1), new(1,-1,-1), new(1,1,-1),  new(-1,1,-1),
                new(1,-1, 1),  new(-1,-1,1), new(-1,1, 1), new(1, 1, 1),
                new(-1,-1, 1), new(-1,-1,-1),new(-1,1,-1), new(-1,1, 1),
                new(1,-1,-1),  new(1,-1, 1), new(1, 1, 1), new(1, 1,-1),
                new(-1,1,-1),  new(1, 1,-1), new(1, 1, 1), new(-1,1, 1),
                new(-1,-1, 1), new(1,-1, 1), new(1,-1,-1), new(-1,-1,-1),
            };
            var verts = new VertexPositionColor[24];
            for (int i = 0; i < 24; i++)
                verts[i] = new VertexPositionColor(pos[i], Color.White);
            var vb = new VertexBuffer(gd, typeof(VertexPositionColor), 24, BufferUsage.WriteOnly);
            vb.SetData(verts);
            var idx = new short[36];
            for (int i = 0; i < 6; i++)
            {
                idx[i * 6 + 0] = (short)(i * 4 + 0); idx[i * 6 + 1] = (short)(i * 4 + 1);
                idx[i * 6 + 2] = (short)(i * 4 + 2); idx[i * 6 + 3] = (short)(i * 4 + 0);
                idx[i * 6 + 4] = (short)(i * 4 + 2); idx[i * 6 + 5] = (short)(i * 4 + 3);
            }
            var ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, 36, BufferUsage.WriteOnly);
            ib.SetData(idx);
            return (vb, ib);
        }
        public void Dispose()
        {
            _vb?.Dispose();
            _ib?.Dispose();
        }
    }
}