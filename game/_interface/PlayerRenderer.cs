using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    /// <summary>
    /// Veloren / Cube World style blocky player.
    /// Body (torso + legs + arms) rotates toward movement direction (BodyYaw).
    /// Head rotates toward camera direction, clamped to ±90° of body (HeadYawOffset).
    /// </summary>
    public class PlayerRenderer : IDisposable
    {
        // ── Palette ────────────────────────────────────────────────────
        private static readonly Color CSkin      = new Color(255, 213, 170);
        private static readonly Color CSkinShade = new Color(215, 170, 130);
        private static readonly Color CSkinDark  = new Color(180, 135, 100);

        private static readonly Color CHair      = new Color(55,  32,  10);
        private static readonly Color CHairDark  = new Color(38,  22,   6);

        private static readonly Color CEyeWhite  = new Color(240, 240, 240);
        private static readonly Color CEyeIris   = new Color(60, 100, 200);
        private static readonly Color CEyePupil  = new Color(20,  20,  40);

        private static readonly Color CTunic     = new Color( 80, 130, 200);
        private static readonly Color CTunicDark = new Color( 50,  90, 155);
        private static readonly Color CTunicSide = new Color( 60, 110, 175);

        private static readonly Color CBelt      = new Color(120,  80,  35);
        private static readonly Color CBeltDark  = new Color( 80,  50,  20);

        private static readonly Color CPants     = new Color( 55,  55, 110);
        private static readonly Color CPantsDark = new Color( 35,  35,  80);

        private static readonly Color CBoot      = new Color(100,  65,  28);
        private static readonly Color CBootDark  = new Color( 65,  40,  15);
        private static readonly Color CBootSole  = new Color( 45,  28,  10);

        // ── Scale ──────────────────────────────────────────────────────
        private const float S         = 0.075f;
        private const float MoveSpeed = 6f;

        private readonly GraphicsDevice _gd;
        private BasicEffect _effect;

        private readonly VertexPositionColor[] _verts = new VertexPositionColor[4096];
        private readonly short[]               _idx   = new short[8192];
        private int _vc, _ic;

        public PlayerRenderer(GraphicsDevice gd)
        {
            _gd    = gd;
            _effect = new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                LightingEnabled    = false,
                TextureEnabled     = false,
            };
        }

        // ──────────────────────────────────────────────────────────────
        /// <param name="bodyYaw">      PlayerController.BodyYaw      </param>
        /// <param name="headYawOffset">PlayerController.HeadYawOffset </param>
        public void Draw(Vector3 feetPos,
                         float bodyYaw,
                         float headYawOffset,
                         Matrix view, Matrix proj,
                         bool isGrounded, Vector3 velocity,
                         GameTime gameTime)
        {
            _vc = _ic = 0;

            float time  = (float)gameTime.TotalGameTime.TotalSeconds;
            float hspd  = (float)Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            float walkT = isGrounded ? Math.Min(hspd / MoveSpeed, 1f) : 0f;
            float runT  = Math.Min(hspd / (MoveSpeed * 1.8f), 1f);

            float walkPhase = time * 8f;
            float swing     = (float)Math.Sin(walkPhase) * walkT;
            float swingFast = (float)Math.Sin(walkPhase) * runT * 1.3f;
            float bob       = (float)Math.Abs(Math.Sin(walkPhase * 2f)) * walkT * 0.025f;
            float airLean   = !isGrounded && hspd > 1f ? 0.12f : 0f;

            // Body rotation (torso, legs, arms)
            Matrix bodyRot = Matrix.CreateRotationY(bodyYaw)
                           * Matrix.CreateRotationX(-airLean);

            // Head rotation = body + offset toward camera
            Matrix headRot = Matrix.CreateRotationY(bodyYaw + headYawOffset);

            // ── Layout ────────────────────────────────────────────────
            const float legH  = 8 * S;
            const float bodyH = 8 * S;
            const float neckH = 1 * S;

            float legBot  = 0f;
            float bodyBot = legBot  + legH;
            float neckBot = bodyBot + bodyH;
            float headBot = neckBot + neckH;

            // ── Boots ─────────────────────────────────────────────────
            BuildBoot(-2f * S, legBot,  swing, bodyRot, feetPos, bob);
            BuildBoot( 2f * S, legBot, -swing, bodyRot, feetPos, bob);

            // ── Legs ──────────────────────────────────────────────────
            BuildLeg(-2f * S, legBot + 1 * S,  swing, bodyRot, feetPos, bob);
            BuildLeg( 2f * S, legBot + 1 * S, -swing, bodyRot, feetPos, bob);

            // ── Belt ──────────────────────────────────────────────────
            const float beltW = 8 * S, beltH = 1.5f * S, beltD = 4 * S;
            AddBox(-beltW * .5f, bodyBot, -beltD * .5f,
                    beltW * .5f, bodyBot + beltH, beltD * .5f,
                   Vector3.Zero, Matrix.Identity, bodyRot, feetPos, bob,
                   CBelt, CBeltDark);

            // ── Torso ─────────────────────────────────────────────────
            const float torsoW = 8 * S, torsoD = 4 * S;
            float torsoBot = bodyBot + beltH;
            float torsoTop = bodyBot + bodyH;
            AddBox(-torsoW * .5f, torsoBot, -torsoD * .5f,
                    torsoW * .5f, torsoTop,  torsoD * .5f,
                   Vector3.Zero, Matrix.Identity, bodyRot, feetPos, bob,
                   CTunic, CTunicDark, CTunicSide);

            // ── Arms ──────────────────────────────────────────────────
            float armTopY = torsoTop - 0.5f * S;
            BuildArm(-4.5f * S, armTopY,  swingFast, bodyRot, feetPos, bob);
            BuildArm( 4.5f * S, armTopY, -swingFast, bodyRot, feetPos, bob);

            // ── Head (uses headRot instead of bodyRot) ─────────────────
            BuildHead(headBot, headRot, feetPos, bob);

            // ── Render ───────────────────────────────────────────────
            if (_vc == 0) return;

            _effect.View       = view;
            _effect.Projection = proj;
            _effect.World      = Matrix.Identity;

            _gd.DepthStencilState = DepthStencilState.Default;
            _gd.BlendState        = BlendState.Opaque;
            _gd.RasterizerState   = RasterizerState.CullCounterClockwise;

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _verts, 0, _vc,
                    _idx,   0, _ic / 3);
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Part builders
        // ──────────────────────────────────────────────────────────────

        private void BuildBoot(float localX, float botY, float swing,
                                Matrix bodyRot, Vector3 feetWorld, float bob)
        {
            const int W = 4, H = 2, D = 4;
            float hw = W * 0.5f * S, hd = D * 0.5f * S;
            float y0 = botY, y1 = botY + H * S;
            var pivot  = new Vector3(localX, y1, 0);
            var swingM = Matrix.CreateRotationX(swing * 0.28f);
            AddBox(localX - hw, y0,             -hd, localX + hw, y0 + 0.5f * S, hd,
                   pivot, swingM, bodyRot, feetWorld, bob, CBootSole, CBootSole);
            AddBox(localX - hw, y0 + 0.5f * S, -hd, localX + hw, y1, hd,
                   pivot, swingM, bodyRot, feetWorld, bob, CBoot, CBootDark);
        }

        private void BuildLeg(float localX, float botY, float swing,
                               Matrix bodyRot, Vector3 feetWorld, float bob)
        {
            const int W = 3, H = 6, D = 3;
            float hw = W * 0.5f * S, hd = D * 0.5f * S;
            float y1 = botY + H * S;
            var pivot  = new Vector3(localX, y1, 0);
            var swingM = Matrix.CreateRotationX(swing * 0.35f);
            AddBox(localX - hw, botY, -hd, localX + hw, y1, hd,
                   pivot, swingM, bodyRot, feetWorld, bob, CPants, CPantsDark);
        }

        private void BuildArm(float localX, float topY, float swing,
                               Matrix bodyRot, Vector3 feetWorld, float bob)
        {
            const int W = 3, H = 8, D = 3;
            float hw = W * 0.5f * S, hd = D * 0.5f * S;
            float y0 = topY - H * S, y1 = topY;
            var pivot  = new Vector3(localX, y1, 0);
            var swingM = Matrix.CreateRotationX(swing * 0.45f);
            // Sleeve
            AddBox(localX - hw, y0 + H * 0.45f * S, -hd, localX + hw, y1, hd,
                   pivot, swingM, bodyRot, feetWorld, bob, CTunic, CTunicDark, CTunicSide);
            // Forearm / hand
            AddBox(localX - hw, y0, -hd, localX + hw, y0 + H * 0.45f * S, hd,
                   pivot, swingM, bodyRot, feetWorld, bob, CSkin, CSkinDark);
        }

        // Head uses its own headRot matrix (body yaw + head offset).
        private void BuildHead(float botY, Matrix headRot, Vector3 feetWorld, float bob)
        {
            const int Sz = 8;
            float hw  = Sz * 0.5f * S;
            float topY = botY + Sz * S;

            // Base skin
            AddBox(-hw, botY, -hw, hw, topY, hw,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob,
                   CSkin, CSkinShade);

            // Hair top cap
            AddBox(-hw - 0.2f * S, topY - 2.5f * S, -hw - 0.2f * S,
                    hw + 0.2f * S, topY + 0.4f * S,  hw + 0.2f * S,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob,
                   CHair, CHairDark);
            // Side panels
            AddBox(-hw - 0.4f * S, botY + 3f * S, -hw + 0.2f * S,
                   -hw + 0.4f * S, topY + 0.4f * S,  hw - 0.2f * S,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob,
                   CHair, CHairDark);
            AddBox( hw - 0.4f * S, botY + 3f * S, -hw + 0.2f * S,
                    hw + 0.4f * S, topY + 0.4f * S,  hw - 0.2f * S,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob,
                   CHair, CHairDark);
            // Back hair
            AddBox(-hw - 0.2f * S, botY + 1.5f * S, hw - 0.5f * S,
                    hw + 0.2f * S, topY + 0.4f * S,  hw + 0.6f * S,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob,
                   CHair, CHairDark);

            // Eyes (front = local -Z)
            float eyeZ   = -hw - 0.02f;
            float eyeBot = botY + 3.8f * S;
            float eyeTop = botY + 5.5f * S;
            float eyeW   = 1.8f * S;
            float eyeD   = 0.4f  * S;

            AddBox(-hw + 1.0f * S, eyeBot, eyeZ, -hw + 1.0f * S + eyeW, eyeTop, eyeZ + eyeD,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CEyeWhite, CEyeWhite);
            AddBox( hw - 1.0f * S - eyeW, eyeBot, eyeZ,  hw - 1.0f * S, eyeTop, eyeZ + eyeD,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CEyeWhite, CEyeWhite);

            float irisW = 1.2f * S, irisH = 1.2f * S;
            AddBox(-hw + 1.3f * S, eyeBot + 0.4f * S, eyeZ - 0.02f,
                   -hw + 1.3f * S + irisW, eyeBot + 0.4f * S + irisH, eyeZ,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CEyeIris, CEyeIris);
            AddBox( hw - 1.3f * S - irisW, eyeBot + 0.4f * S, eyeZ - 0.02f,
                    hw - 1.3f * S,          eyeBot + 0.4f * S + irisH, eyeZ,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CEyeIris, CEyeIris);

            float pupW = 0.6f * S;
            AddBox(-hw + 1.6f * S, eyeBot + 0.7f * S, eyeZ - 0.04f,
                   -hw + 1.6f * S + pupW, eyeBot + 0.7f * S + pupW, eyeZ - 0.02f,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CEyePupil, CEyePupil);
            AddBox( hw - 1.6f * S - pupW, eyeBot + 0.7f * S, eyeZ - 0.04f,
                    hw - 1.6f * S,         eyeBot + 0.7f * S + pupW, eyeZ - 0.02f,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CEyePupil, CEyePupil);

            // Eyebrows
            float browY = eyeTop + 0.2f * S;
            AddBox(-hw + 0.8f * S, browY, eyeZ - 0.01f, -hw + 2.4f * S, browY + 0.5f * S, eyeZ,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CHair, CHairDark);
            AddBox( hw - 2.4f * S, browY, eyeZ - 0.01f,  hw - 0.8f * S, browY + 0.5f * S, eyeZ,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CHair, CHairDark);

            // Nose
            float noseY = botY + 2.5f * S;
            AddBox(-0.5f * S, noseY, eyeZ - 0.3f * S, 0.5f * S, noseY + 1.0f * S, eyeZ,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CSkinShade, CSkinDark);

            // Mouth
            float mouthY = botY + 1.8f * S;
            AddBox(-1.2f * S, mouthY, eyeZ - 0.02f, 1.2f * S, mouthY + 0.5f * S, eyeZ,
                   Vector3.Zero, Matrix.Identity, headRot, feetWorld, bob, CSkinDark, CSkinDark);
        }

        // ──────────────────────────────────────────────────────────────
        //  Core box emitter
        // ──────────────────────────────────────────────────────────────
        private void AddBox(float x0, float y0, float z0,
                            float x1, float y1, float z1,
                            Vector3 pivotLocal, Matrix swingMat,
                            Matrix rot, Vector3 worldOrigin, float bobY,
                            Color colLight, Color colDark,
                            Color? colSide = null)
        {
            Color sideCol = colSide ?? colLight;

            Span<Vector3> c = stackalloc Vector3[8];
            c[0] = new Vector3(x0, y0, z0);
            c[1] = new Vector3(x1, y0, z0);
            c[2] = new Vector3(x1, y1, z0);
            c[3] = new Vector3(x0, y1, z0);
            c[4] = new Vector3(x0, y0, z1);
            c[5] = new Vector3(x1, y0, z1);
            c[6] = new Vector3(x1, y1, z1);
            c[7] = new Vector3(x0, y1, z1);

            for (int i = 0; i < 8; i++)
                c[i] = Vector3.Transform(c[i] - pivotLocal, swingMat) + pivotLocal;

            var bobVec = new Vector3(0, bobY, 0);
            for (int i = 0; i < 8; i++)
                c[i] = Vector3.Transform(c[i], rot) + worldOrigin + bobVec;

            Color top    = Lighten(colLight, 30);
            Color front  = sideCol;
            Color back   = Lerp(sideCol, colDark, 0.35f);
            Color left   = Lerp(sideCol, colDark, 0.20f);
            Color right2 = Lerp(sideCol, colDark, 0.10f);
            Color bot    = colDark;

            EmitQuad(c[0], c[1], c[2], c[3], front);
            EmitQuad(c[5], c[4], c[7], c[6], back);
            EmitQuad(c[4], c[0], c[3], c[7], left);
            EmitQuad(c[1], c[5], c[6], c[2], right2);
            EmitQuad(c[3], c[2], c[6], c[7], top);
            EmitQuad(c[4], c[5], c[1], c[0], bot);
        }

        private void EmitQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            if (_vc + 4 > _verts.Length || _ic + 6 > _idx.Length) return;
            short b0 = (short)_vc;
            _verts[_vc++] = new VertexPositionColor(a, col);
            _verts[_vc++] = new VertexPositionColor(b, col);
            _verts[_vc++] = new VertexPositionColor(c, col);
            _verts[_vc++] = new VertexPositionColor(d, col);
            _idx[_ic++] = b0;         _idx[_ic++] = (short)(b0+1); _idx[_ic++] = (short)(b0+2);
            _idx[_ic++] = b0;         _idx[_ic++] = (short)(b0+2); _idx[_ic++] = (short)(b0+3);
        }

        private static Color Lighten(Color c, int a) =>
            new Color(Math.Min(255, c.R + a), Math.Min(255, c.G + a), Math.Min(255, c.B + a));
        private static Color Lerp(Color a, Color b, float t) =>
            new Color((int)(a.R + (b.R - a.R) * t),
                      (int)(a.G + (b.G - a.G) * t),
                      (int)(a.B + (b.B - a.B) * t));

        public void Dispose() => _effect?.Dispose();
    }
}