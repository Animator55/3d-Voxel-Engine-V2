using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace game
{
    // ──────────────────────────────────────────────────────────────────
    //  Partícula individual
    // ──────────────────────────────────────────────────────────────────

    internal struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Color   StartColor;
        public Color   EndColor;
        public float   Size;
        public float   SizeEnd;
        public float   Life;       // tiempo restante (segundos)
        public float   MaxLife;
        public bool    Alive;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Descriptor de emisión — define cómo se generan partículas
    // ──────────────────────────────────────────────────────────────────

    public sealed class ParticleEmitParams
    {
        // Número de partículas a emitir
        public int   Count          = 8;

        // Posición central del burst
        public Vector3 Origin       = Vector3.Zero;

        // Rango de velocidades (aleatorio entre min y max)
        public Vector3 VelocityMin  = new Vector3(-3f,  2f, -3f);
        public Vector3 VelocityMax  = new Vector3( 3f,  6f,  3f);

        // Duración de vida (segundos)
        public float LifeMin        = 0.4f;
        public float LifeMax        = 0.9f;

        // Tamaño del cubo partícula (unidades mundo)
        public float SizeStart      = 0.12f;
        public float SizeEnd        = 0.04f;

        // Color (interpola de Start a End durante la vida)
        public Color ColorStart     = Color.White;
        public Color ColorEnd       = Color.Transparent;

        // Gravedad propia (negativa = cae)
        public float Gravity        = -12f;

        // Drag (0 = sin resistencia, 1 = se detiene al instante)
        public float Drag           = 0.85f;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Presets de partículas de uso común
    // ──────────────────────────────────────────────────────────────────

    public static class ParticlePresets
    {
        public static ParticleEmitParams Hit(Vector3 origin, Color color) => new ParticleEmitParams
        {
            Count        = 6,
            Origin       = origin,
            VelocityMin  = new Vector3(-4f, 1f, -4f),
            VelocityMax  = new Vector3( 4f, 5f,  4f),
            LifeMin      = 0.25f,
            LifeMax      = 0.55f,
            SizeStart    = 0.10f,
            SizeEnd      = 0.02f,
            ColorStart   = color,
            ColorEnd     = new Color((int)color.R, (int)color.G, (int)color.B, 0),
            Gravity      = -14f,
            Drag         = 0.80f,
        };

        public static ParticleEmitParams Death(Vector3 origin, Color color) => new ParticleEmitParams
        {
            Count        = 18,
            Origin       = origin + Vector3.UnitY * 0.5f,
            VelocityMin  = new Vector3(-5f, 2f, -5f),
            VelocityMax  = new Vector3( 5f, 9f,  5f),
            LifeMin      = 0.5f,
            LifeMax      = 1.1f,
            SizeStart    = 0.14f,
            SizeEnd      = 0.02f,
            ColorStart   = color,
            ColorEnd     = new Color(color.R / 2, color.G / 2, color.B / 2, 0),
            Gravity      = -16f,
            Drag         = 0.75f,
        };

        public static ParticleEmitParams Leaf(Vector3 origin) => new ParticleEmitParams
        {
            Count        = 4,
            Origin       = origin + Vector3.UnitY * 0.3f,
            VelocityMin  = new Vector3(-1.5f, 0.5f, -1.5f),
            VelocityMax  = new Vector3( 1.5f, 2.5f,  1.5f),
            LifeMin      = 0.6f,
            LifeMax      = 1.2f,
            SizeStart    = 0.09f,
            SizeEnd      = 0.03f,
            ColorStart   = new Color(80, 180, 60),
            ColorEnd     = new Color(50, 130, 30, 0),
            Gravity      = -4f,
            Drag         = 0.70f,
        };

        public static ParticleEmitParams Dust(Vector3 origin) => new ParticleEmitParams
        {
            Count        = 5,
            Origin       = origin,
            VelocityMin  = new Vector3(-1f, 0.2f, -1f),
            VelocityMax  = new Vector3( 1f, 1.0f,  1f),
            LifeMin      = 0.3f,
            LifeMax      = 0.6f,
            SizeStart    = 0.07f,
            SizeEnd      = 0.02f,
            ColorStart   = new Color(200, 180, 140),
            ColorEnd     = new Color(160, 140, 100, 0),
            Gravity      = -2f,
            Drag         = 0.60f,
        };
    }

    // ──────────────────────────────────────────────────────────────────
    //  ParticleSystem
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sistema central de partículas.
    ///
    /// Uso:
    ///   // En LoadContent:
    ///   _particles = new ParticleSystem(GraphicsDevice, maxParticles: 4096);
    ///
    ///   // Emitir:
    ///   _particles.Emit(ParticlePresets.Hit(pos, color));
    ///
    ///   // En Update:
    ///   _particles.Update(dt);
    ///
    ///   // En Draw:
    ///   _particles.Draw(view, projection);
    /// </summary>
    public sealed class ParticleSystem : IDisposable
    {
        private static readonly Random _rng = new Random();

        // ── Pool de partículas ────────────────────────────────────────
        private readonly Particle[] _pool;
        private int _alive = 0;

        // ── Render ────────────────────────────────────────────────────
        private readonly GraphicsDevice    _gd;
        private readonly BasicEffect       _effect;
        private readonly VertexPositionColor[] _verts;
        private readonly short[]           _idx;

        // ── Stats (debug) ─────────────────────────────────────────────
        public int AliveCount => _alive;

        // ─────────────────────────────────────────────────────────────
        public ParticleSystem(GraphicsDevice gd, int maxParticles = 4096)
        {
            _gd   = gd;
            _pool = new Particle[maxParticles];

            // Cada partícula es un cubo billboard = 6 caras × 4 verts = 24 verts, 36 idx
            // Simplificamos a un quad billboard para mantener el presupuesto razonable:
            // 4 verts + 6 idx por partícula
            _verts = new VertexPositionColor[maxParticles * 4];
            _idx   = new short[maxParticles * 6];

            _effect = new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                LightingEnabled    = false,
                TextureEnabled     = false,
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  API pública
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Emite un burst de partículas según los parámetros.
        /// Si el pool está lleno, no lanza excepción: simplemente descarta el exceso.
        /// </summary>
        public void Emit(ParticleEmitParams p)
        {
            for (int n = 0; n < p.Count; n++)
            {
                int slot = FindFreeSlot();
                if (slot < 0) return; // pool lleno

                float life = p.LifeMin + (float)_rng.NextDouble() * (p.LifeMax - p.LifeMin);

                _pool[slot] = new Particle
                {
                    Position   = p.Origin + RandomSpread(0.15f),
                    Velocity   = RandomRange(p.VelocityMin, p.VelocityMax),
                    StartColor = p.ColorStart,
                    EndColor   = p.ColorEnd,
                    Size       = p.SizeStart,
                    SizeEnd    = p.SizeEnd,
                    Life       = life,
                    MaxLife    = life,
                    Alive      = true,
                };
            }
        }

        public void Update(float dt)
        {
            _alive = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Alive) continue;

                ref var p = ref _pool[i];

                p.Velocity.Y += -12f * dt;  // gravedad genérica (cada preset puede ajustar en Emit)
                p.Velocity.X *= 1f - (1f - 0.80f) * dt * 10f;
                p.Velocity.Z *= 1f - (1f - 0.80f) * dt * 10f;

                p.Position += p.Velocity * dt;
                p.Life     -= dt;

                if (p.Life <= 0f)
                {
                    p.Alive = false;
                    continue;
                }

                _alive++;
            }
        }

        public void Draw(Matrix view, Matrix projection)
        {
            if (_alive == 0) return;

            // Extraer vectores right/up de la vista para el billboard
            var right = new Vector3(view.M11, view.M21, view.M31);
            var up    = new Vector3(view.M12, view.M22, view.M32);

            int vc = 0, ic = 0;

            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Alive) continue;
                ref var p = ref _pool[i];

                float t     = 1f - (p.Life / p.MaxLife);
                float size  = MathHelper.Lerp(p.Size, p.SizeEnd, t) * 0.5f;
                Color color = LerpColor(p.StartColor, p.EndColor, t);

                Vector3 a = p.Position + (-right - up) * size;
                Vector3 b = p.Position + ( right - up) * size;
                Vector3 c = p.Position + ( right + up) * size;
                Vector3 d = p.Position + (-right + up) * size;

                short b0 = (short)vc;
                _verts[vc++] = new VertexPositionColor(a, color);
                _verts[vc++] = new VertexPositionColor(b, color);
                _verts[vc++] = new VertexPositionColor(c, color);
                _verts[vc++] = new VertexPositionColor(d, color);

                _idx[ic++] = b0;
                _idx[ic++] = (short)(b0 + 1);
                _idx[ic++] = (short)(b0 + 2);
                _idx[ic++] = b0;
                _idx[ic++] = (short)(b0 + 2);
                _idx[ic++] = (short)(b0 + 3);
            }

            if (vc == 0) return;

            _effect.View       = view;
            _effect.Projection = projection;
            _effect.World      = Matrix.Identity;

            _gd.DepthStencilState = DepthStencilState.DepthRead;
            _gd.BlendState        = BlendState.NonPremultiplied;
            _gd.RasterizerState   = RasterizerState.CullNone;

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _verts, 0, vc,
                    _idx,   0, ic / 3);
            }

            // Restaurar estados
            _gd.DepthStencilState = DepthStencilState.Default;
            _gd.BlendState        = BlendState.Opaque;
            _gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        }

        // ─────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────

        private int FindFreeSlot()
        {
            // Búsqueda lineal — para pools pequeños es suficiente.
            // Si el pool es grande (>8k) considerar una free-list.
            for (int i = 0; i < _pool.Length; i++)
                if (!_pool[i].Alive) return i;
            return -1;
        }

        private static Vector3 RandomSpread(float radius)
            => new Vector3(
                (float)(_rng.NextDouble() * 2 - 1) * radius,
                (float)(_rng.NextDouble() * 2 - 1) * radius,
                (float)(_rng.NextDouble() * 2 - 1) * radius);

        private static Vector3 RandomRange(Vector3 min, Vector3 max)
            => new Vector3(
                min.X + (float)_rng.NextDouble() * (max.X - min.X),
                min.Y + (float)_rng.NextDouble() * (max.Y - min.Y),
                min.Z + (float)_rng.NextDouble() * (max.Z - min.Z));

        private static Color LerpColor(Color a, Color b, float t)
            => new Color(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t),
                (int)(a.A + (b.A - a.A) * t));

        public void Dispose() => _effect?.Dispose();
    }
}