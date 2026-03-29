using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    /// <summary>
    /// Renderiza entidades como meshes de voxels procedurales.
    ///
    /// Soporta EntityShape: Box, BiPed, QuadruPed, Plant.
    /// El estilo visual se extrae de EntityDefinition (colores, escala).
    ///
    /// Reutiliza un único buffer de vértices compartido, por lo que
    /// TODAS las entidades se acumulan en un solo draw call por frame.
    /// </summary>
    public sealed class EntityRenderer : IDisposable
    {
        private const int MaxVerts = 65536;
        private const int MaxIdx = MaxVerts * 3;

        private readonly GraphicsDevice _gd;
        private readonly BasicEffect _effect;
        private readonly VertexPositionColor[] _verts = new VertexPositionColor[MaxVerts];
        private readonly short[] _idx = new short[MaxIdx];
        private int _vc, _ic;

        // ── Easing ───────────────────────────────────────────────────
        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        public EntityRenderer(GraphicsDevice gd)
        {
            _gd = gd;
            _effect = new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                TextureEnabled = false,
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  API: acumular + flush
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Agrega la geometría de una entidad al buffer.
        /// Llamar para cada entidad viva antes de Flush().
        /// </summary>
        public void Add(Entity entity, GameTime gameTime, Vector3 cameraPosition)
        {
            var def = entity.Definition;
            var pos = entity.VisualPosition;
            float t = (float)gameTime.TotalGameTime.TotalSeconds;
            float s = def.VisualScale;

            // ── Animación de muerte: voltear hacia el suelo ───────────
            float deathTilt = 0f;
            float deathSink = 0f;
            if (entity.LifeState == EntityLifeState.Dying ||
                entity.LifeState == EntityLifeState.Dead)
            {
                float p = EaseOut(entity.DeathAnimProgress);
                deathTilt = p * MathHelper.PiOver2;
                deathSink = p * def.CollisionHeight * 0.6f;
            }

            // ── Flash de daño ─────────────────────────────────────────
            float flashT = entity.HitFlashTimer > 0f
                ? 1f - (entity.HitFlashTimer / 0.12f)
                : 0f;
            Color tintOverride = entity.HitFlashTimer > 0f
                ? LerpColor(Color.White, def.PrimaryColor, flashT)
                : def.PrimaryColor;

            var bodyRot = Matrix.CreateRotationX(deathTilt)
                        * Matrix.CreateRotationY(entity.Facing);

            Vector3 renderPos = pos - new Vector3(0f, deathSink, 0f);

            // ── Barra de vida ─────────────────────────────────────────
            if (entity.HealthBarTimer > 0f && entity.LifeState == EntityLifeState.Alive)
                BuildHealthBar(entity, cameraPosition);

            // ── Shape ─────────────────────────────────────────────────
            switch (def.Shape)
            {
                case EntityShape.Box:
                    BuildBox(renderPos, s, def, bodyRot, tintOverride);
                    break;
                case EntityShape.BiPed:
                    BuildBiPed(renderPos, s, def, bodyRot, t, entity, tintOverride);
                    break;
                case EntityShape.QuadruPed:
                    BuildQuadruPed(renderPos, s, def, bodyRot, t, entity, tintOverride);
                    break;
                case EntityShape.Plant:
                    BuildPlant(renderPos, s, def, t, tintOverride);
                    break;
            }
        }

        /// <summary>
        /// Envía el buffer acumulado a la GPU y lo limpia.
        /// </summary>
        public void Flush(Matrix view, Matrix projection)
        {
            if (_vc == 0) return;

            _effect.View = view;
            _effect.Projection = projection;
            _effect.World = Matrix.Identity;

            _gd.DepthStencilState = DepthStencilState.Default;
            _gd.BlendState = BlendState.Opaque;
            _gd.RasterizerState = RasterizerState.CullNone; // CullNone para que las barras se vean desde cualquier ángulo

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _verts, 0, _vc,
                    _idx, 0, _ic / 3);
            }

            _vc = _ic = 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Health bar
        // ─────────────────────────────────────────────────────────────

        private void BuildHealthBar(Entity entity, Vector3 cameraPos)
        {
            float barWidth = entity.Definition.CollisionRadius * 2.2f;
            float barHeight = 0.15f;
            float barY = entity.VisualPosition.Y + entity.Definition.CollisionHeight + 0.25f;

            // Billboarding: cross product corregido para que right apunte
            // siempre a la derecha desde el punto de vista de la cámara
            Vector3 toCamera = cameraPos - entity.VisualPosition;
            toCamera.Y = 0f;
            if (toCamera.LengthSquared() < 0.001f) return;
            toCamera.Normalize();

            Vector3 right = Vector3.Cross(toCamera, Vector3.UnitY);
            if (right.LengthSquared() < 0.001f) return;
            right.Normalize();

            Vector3 center = new Vector3(entity.VisualPosition.X, barY, entity.VisualPosition.Z);

            // Fondo oscuro — barra completa
            EmitBarQuad(center, right, barWidth, barHeight, new Color(20, 20, 20));

            // Relleno de vida — ancho proporcional a HP
            float hpFrac = entity.CurrentHealth / entity.MaxHealth;
            Color fillColor = hpFrac > 0.5f ? new Color(60, 200, 80)
                            : hpFrac > 0.25f ? new Color(230, 180, 30)
                            : new Color(210, 50, 40);

            // Borde izquierdo fijo: desplazamos el centro del relleno
            float fillWidth = barWidth * hpFrac;
            float fillOffset = (barWidth - fillWidth) * 0.5f;
            Vector3 fillCenter = center - right * fillOffset + toCamera * 0.01f;

            EmitBarQuad(fillCenter, right, fillWidth, barHeight * 0.72f, fillColor);
        }

        /// <summary>
        /// Emite un quad plano centrado en 'center', orientado a lo largo de 'right'.
        /// Se emite por ambas caras para evitar problemas de culling.
        /// </summary>
        private void EmitBarQuad(Vector3 center, Vector3 right, float width, float height, Color col)
        {
            Vector3 r = right * (width * 0.5f);
            Vector3 up = Vector3.UnitY * (height * 0.5f);

            Vector3 tl = center - r + up;
            Vector3 tr = center + r + up;
            Vector3 br = center + r - up;
            Vector3 bl = center - r - up;

            // Cara frontal
            EmitQuad(tl, tr, br, bl, col);
            // Cara trasera (orden invertido)
            EmitQuad(bl, br, tr, tl, col);
        }

        // ─────────────────────────────────────────────────────────────
        //  Shapes
        // ─────────────────────────────────────────────────────────────

        // ── Caja simple ───────────────────────────────────────────────
        private void BuildBox(Vector3 pos, float s, EntityDefinition def,
                              Matrix rot, Color tint)
        {
            float hw = def.CollisionRadius * s;
            float h = def.CollisionHeight * s;
            EmitBox(-hw, 0f, -hw, hw, h, hw,
                    Vector3.Zero, Matrix.Identity, rot, pos, 0f,
                    tint, def.SecondaryColor);
        }

        // ── Bípedo (zombie / humanoide) ───────────────────────────────
        private void BuildBiPed(Vector3 pos, float s, EntityDefinition def,
                                Matrix bodyRot, float time, Entity entity, Color tint)
        {
            float v = 0.07f * s;
            bool moving = entity.Velocity.LengthSquared() > 0.1f;
            float swing = moving ? (float)Math.Sin(time * 14f) * 0.4f : 0f;

            Color pri = tint;
            Color sec = def.SecondaryColor;
            Color acc = def.AccentColor;

            // Pies
            EmitOval(-1.5f * v, 0f, -0.5f * v, 1.5f * v, 2.5f * v, 2f * v,
                     new Vector3(-1.5f * v, 2.5f * v, 0f),
                     Matrix.CreateRotationX(swing), bodyRot, pos, 0f, acc, sec);
            EmitOval(0f, 0f, -0.5f * v, 3f * v, 2.5f * v, 2f * v,
                     new Vector3(1.5f * v, 2.5f * v, 0f),
                     Matrix.CreateRotationX(-swing), bodyRot, pos, 0f, acc, sec);

            // Cuerpo
            EmitOval(-2.5f * v, 2.5f * v, -1.5f * v, 2.5f * v, 5f * v, 1.5f * v,
                     Vector3.Zero, Matrix.Identity, bodyRot, pos, 0f, pri, sec);

            // Brazos
            EmitOval(-4f * v, 5f * v, -v, v, 4.5f * v, v,
                     new Vector3(-4f * v, 7.5f * v, 0f),
                     Matrix.CreateRotationX(-swing), bodyRot, pos, 0f, pri, sec);
            EmitOval(3f * v, 5f * v, -v, 5f * v, 4.5f * v, v,
                     new Vector3(4f * v, 7.5f * v, 0f),
                     Matrix.CreateRotationX(swing), bodyRot, pos, 0f, pri, sec);

            // Cabeza
            EmitOval(-2.5f * v, 7.5f * v, -2f * v, 2.5f * v, 3f * v, 2f * v,
                     Vector3.Zero, Matrix.Identity, bodyRot, pos, 0f, pri, acc);
        }

        // ── Cuadrúpedo (cerdo, oveja) ─────────────────────────────────
        private void BuildQuadruPed(Vector3 pos, float s, EntityDefinition def,
                                    Matrix bodyRot, float time, Entity entity, Color tint)
        {
            float v = 0.065f * s;
            bool moving = entity.Velocity.LengthSquared() > 0.1f;
            float swing = moving ? (float)Math.Sin(time * 12f) * 0.35f : 0f;

            Color pri = tint;
            Color sec = def.SecondaryColor;
            Color acc = def.AccentColor;

            // Cuatro patas
            BuildLeg(-2.5f * v, 0f, -3f * v, v, swing, bodyRot, pos, pri, sec);
            BuildLeg(1.5f * v, 0f, -3f * v, v, -swing, bodyRot, pos, pri, sec);
            BuildLeg(-2.5f * v, 0f, 1.5f * v, v, -swing, bodyRot, pos, pri, sec);
            BuildLeg(1.5f * v, 0f, 1.5f * v, v, swing, bodyRot, pos, pri, sec);

            // Cuerpo (elipsoide aplanado)
            EmitOval(-3f * v, 3f * v, -3.5f * v, 3f * v, 3.5f * v, 4f * v,
                     Vector3.Zero, Matrix.Identity, bodyRot, pos, 0f, pri, sec);

            // Cabeza
            EmitOval(-2f * v, 4.5f * v, -5.5f * v, 2f * v, 2.5f * v, 2.5f * v,
                     Vector3.Zero, Matrix.Identity, bodyRot, pos, 0f, pri, acc);

            // Hocico
            EmitOval(-1f * v, 4.5f * v, -7.5f * v, v, v, v,
                     Vector3.Zero, Matrix.Identity, bodyRot, pos, 0f, acc, sec);
        }

        private void BuildLeg(float cx, float cy, float cz, float v,
                               float swing, Matrix bodyRot, Vector3 pos,
                               Color light, Color dark)
        {
            var pivot = new Vector3(cx + v * 0.5f, cy + 3.5f * v, cz + v * 0.5f);
            var swM = Matrix.CreateRotationX(swing);
            EmitBox(cx, cy, cz, cx + v, cy + 3.5f * v, cz + v,
                    pivot, swM, bodyRot, pos, 0f, light, dark);
        }

        // ── Planta ────────────────────────────────────────────────────
        private void BuildPlant(Vector3 pos, float s, EntityDefinition def,
                                 float time, Color tint)
        {
            float v = 0.055f * s;
            float sway = (float)Math.Sin(time * 1.2f + pos.X * 0.3f) * 0.03f;

            Color pri = tint;
            Color sec = def.SecondaryColor;
            Color acc = def.AccentColor;

            // Tallo central
            EmitBox(-v, 0f, -v, v, 7f * v, v,
                    new Vector3(0f, 0f, 0f),
                    Matrix.CreateRotationX(sway),
                    Matrix.Identity, pos, 0f, sec, dark: LerpColor(sec, Color.Black, 0.3f));

            // Hojas: 4 cruces en X
            float[] angles = { 0f, MathHelper.PiOver2, MathHelper.Pi * 0.25f, MathHelper.Pi * 0.75f };
            foreach (float angle in angles)
            {
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                float r = 3.5f * v;
                float hx = cos * r, hz = sin * r;
                float hy = 4f * v + (float)Math.Abs(Math.Cos(angle * 2f)) * v;

                var leafRot = Matrix.CreateRotationX(sway * 0.5f)
                            * Matrix.CreateRotationY(angle);

                EmitBox(-3f * v, hy - v * 0.5f, -v * 0.5f,
                         3f * v, hy + v * 0.8f, v * 0.5f,
                        Vector3.Zero, Matrix.Identity,
                        leafRot, pos + new Vector3(0f, 0f, 0f), 0f,
                        acc, pri);
            }

            // Flor/cabeza según color acento
            EmitBox(-v * 1.2f, 7f * v, -v * 1.2f, v * 1.2f, 9f * v, v * 1.2f,
                    new Vector3(0f, 7f * v, 0f),
                    Matrix.CreateRotationX(sway * 2f),
                    Matrix.Identity, pos, 0f,
                    def.AccentColor, sec);
        }

        // ─────────────────────────────────────────────────────────────
        //  Geometría primitiva
        // ─────────────────────────────────────────────────────────────

        /// <summary>Emite una caja con transformaciones.</summary>
        private void EmitBox(float x0, float y0, float z0,
                              float x1, float y1, float z1,
                              Vector3 pivotLocal, Matrix swingMat,
                              Matrix rot, Vector3 worldOrigin, float bobY,
                              Color colLight, Color dark)
        {
            Color side = LerpColor(colLight, dark, 0.18f);

            Span<Vector3> c = stackalloc Vector3[8];
            c[0] = new Vector3(x0, y0, z0); c[1] = new Vector3(x1, y0, z0);
            c[2] = new Vector3(x1, y1, z0); c[3] = new Vector3(x0, y1, z0);
            c[4] = new Vector3(x0, y0, z1); c[5] = new Vector3(x1, y0, z1);
            c[6] = new Vector3(x1, y1, z1); c[7] = new Vector3(x0, y1, z1);

            for (int i = 0; i < 8; i++)
                c[i] = Vector3.Transform(c[i] - pivotLocal, swingMat) + pivotLocal;

            var bv = new Vector3(0f, bobY, 0f);
            for (int i = 0; i < 8; i++)
                c[i] = Vector3.Transform(c[i], rot) + worldOrigin + bv;

            EmitQuad(c[0], c[1], c[2], c[3], colLight);
            EmitQuad(c[5], c[4], c[7], c[6], LerpColor(dark, Color.Black, 0.30f));
            EmitQuad(c[4], c[0], c[3], c[7], LerpColor(side, dark, 0.25f));
            EmitQuad(c[1], c[5], c[6], c[2], LerpColor(side, colLight, 0.10f));
            EmitQuad(c[3], c[2], c[6], c[7], Lighten(colLight, 30));
            EmitQuad(c[4], c[5], c[1], c[0], LerpColor(dark, Color.Black, 0.18f));
        }

        /// <summary>
        /// Emite una caja usando una forma ovalada (celdas dentro de elipsoide).
        /// Permite crear formas más orgánicas que una caja recta.
        /// </summary>
        private void EmitOval(float x0, float y0, float z0,
                               float rX, float rY, float rZ,
                               Vector3 pivotLocal, Matrix swingMat,
                               Matrix rot, Vector3 worldOrigin, float bobY,
                               Color colLight, Color dark)
        {
            float v = Math.Min(rX, Math.Min(rY, rZ)) * 0.5f;
            if (v < 0.001f) return;

            int iX = Math.Max(1, (int)Math.Ceiling(rX / v));
            int iY = Math.Max(1, (int)Math.Ceiling(rY / v));
            int iZ = Math.Max(1, (int)Math.Ceiling(rZ / v));

            float cx = x0 + rX;
            float cy = y0 + rY;
            float cz = z0 + rZ;

            for (int xi = -iX; xi < iX; xi++)
                for (int yi = -iY; yi < iY; yi++)
                    for (int zi = -iZ; zi < iZ; zi++)
                    {
                        float nx = (xi + 0.5f) / iX;
                        float ny = (yi + 0.5f) / iY;
                        float nz = (zi + 0.5f) / iZ;
                        if (nx * nx + ny * ny + nz * nz > 1.0f) continue;

                        float wx0 = cx + xi * v;
                        float wy0 = cy + yi * v;
                        float wz0 = cz + zi * v;

                        float yFade = (yi + iY) / (float)(iY * 2);
                        Color light = LerpColor(dark, colLight, yFade);
                        Color drk = LerpColor(Color.Black, dark, yFade * 0.5f + 0.3f);

                        EmitBox(wx0, wy0, wz0, wx0 + v, wy0 + v, wz0 + v,
                                pivotLocal, swingMat, rot, worldOrigin, bobY,
                                light, drk);
                    }
        }

        private void EmitQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            if (_vc + 4 > _verts.Length || _ic + 6 > _idx.Length) return;
            short b0 = (short)_vc;
            _verts[_vc++] = new VertexPositionColor(a, col);
            _verts[_vc++] = new VertexPositionColor(b, col);
            _verts[_vc++] = new VertexPositionColor(c, col);
            _verts[_vc++] = new VertexPositionColor(d, col);
            _idx[_ic++] = b0; _idx[_ic++] = (short)(b0 + 1); _idx[_ic++] = (short)(b0 + 2);
            _idx[_ic++] = b0; _idx[_ic++] = (short)(b0 + 2); _idx[_ic++] = (short)(b0 + 3);
        }

        // ── Utilidades de color ───────────────────────────────────────
        private static Color Lighten(Color c, int a)
            => new Color(Math.Min(255, c.R + a), Math.Min(255, c.G + a), Math.Min(255, c.B + a));

        private static Color LerpColor(Color a, Color b, float t)
            => new Color((int)(a.R + (b.R - a.R) * t),
                         (int)(a.G + (b.G - a.G) * t),
                         (int)(a.B + (b.B - a.B) * t));

        public void Dispose() => _effect?.Dispose();
    }
}