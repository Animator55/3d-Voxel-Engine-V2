using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    /// <summary>
    /// Construye la geometría de la espada y sus posiciones:
    /// Sheathed, Drawing/Sheathing, Holding, y en mano (BuildSwordInHand).
    /// </summary>
    public partial class PlayerRenderer
    {
        // ── Paleta espada ─────────────────────────────────────────────
        private static readonly Color CBladeHi     = new Color(230, 238, 255);
        private static readonly Color CBladeMid    = new Color(170, 185, 215);
        private static readonly Color CBladeDark   = new Color( 80,  95, 125);
        private static readonly Color CBladeFuller = new Color( 55,  68, 100);
        private static readonly Color CGuard       = new Color(210, 165,  50);
        private static readonly Color CGuardHi     = new Color(240, 205,  90);
        private static readonly Color CGuardDk     = new Color(115,  85,  20);
        private static readonly Color CGrip        = new Color( 50,  30,  15);
        private static readonly Color CGripWrap    = new Color(100,  62,  28);
        private static readonly Color CGripWrapHi  = new Color(125,  82,  42);
        private static readonly Color CPommel      = new Color(190, 165,  60);
        private static readonly Color CPommelHi    = new Color(225, 205, 100);
        private static readonly Color CPommelDk    = new Color(110,  90,  28);

        // ─────────────────────────────────────────────────────────────
        //  Posición: sheathed (espalda)
        // ─────────────────────────────────────────────────────────────
        private void BuildSheathedSword(Matrix bodyRot, Vector3 feet, float bob)
        {
            var rot = Matrix.CreateRotationZ(MathHelper.ToRadians(142f));
            BuildSword(new Vector3(10f * S, 18f * S, 5.9f * S), rot, bodyRot, feet, bob);
        }

        // ─────────────────────────────────────────────────────────────
        //  Posición: drawing / sheathing (interpolación)
        // ─────────────────────────────────────────────────────────────
        private void BuildDrawingSword(float p, Matrix bodyRot, Vector3 feet, float bob)
        {
            float angleZ = MathHelper.Lerp(MathHelper.ToRadians(142f),
                                           MathHelper.ToRadians(-15f), p);
            float angleX = MathHelper.Lerp(MathHelper.ToRadians( 15f),
                                           MathHelper.ToRadians(  5f), p);
            float angleY = MathHelper.Lerp(MathHelper.ToRadians( 45f),
                                           MathHelper.ToRadians(  -15f), p);
            float xOff   = MathHelper.Lerp( 1.5f * S,  7.5f * S, p);
            float yOff   = MathHelper.Lerp( 9.5f * S,  1f * S, p);
            float zOff   = MathHelper.Lerp( 1.8f * S, -1.5f * S, p);

            var rot = Matrix.CreateRotationZ(angleZ)
                    * Matrix.CreateRotationX(angleX)
                    * Matrix.CreateRotationY(angleY);
            BuildSword(new Vector3(xOff, yOff, zOff), rot, bodyRot, feet, bob);
        }

        // ─────────────────────────────────────────────────────────────
        //  Helper: espada al extremo del brazo (holding idle)
        // ─────────────────────────────────────────────────────────────
        private void BuildSwordAtArmEnd(float armCx, float shoulderY, float armPitch,
                                        float swordTiltZ, float swordRollY,
                                        Matrix bodyRot, Vector3 feet, float bob)
        {
            float handX = armCx;
            float handY = shoulderY - ArmLen * (float)Math.Cos(armPitch);
            float handZ = -ArmLen   * (float)Math.Sin(armPitch);

            // -30° → espada más horizontal en Holding
            float baseX = armPitch + MathHelper.ToRadians(-30f);
            var   sRot  = Matrix.CreateRotationX(baseX)
                        * Matrix.CreateRotationZ(swordTiltZ)
                        * Matrix.CreateRotationY(swordRollY);

            BuildSwordInHand(new Vector3(handX, handY, handZ), sRot, bodyRot, feet, bob);
        }

        // ─────────────────────────────────────────────────────────────
        //  Helper: coloca la espada con la mano en el centro del grip
        // ─────────────────────────────────────────────────────────────
        private void BuildSwordInHand(Vector3 handPos, Matrix localRot,
                                      Matrix bodyRot, Vector3 feet, float bob)
        {
            float v          = S * 0.85f;
            float gripCenter = 2.6f * v + 2.5f * 1.4f * v;
            var   gripOff    = Vector3.Transform(new Vector3(0f, -gripCenter, 0f), localRot);
            BuildSword(handPos + gripOff, localRot, bodyRot, feet, bob);
        }

        // ─────────────────────────────────────────────────────────────
        //  Geometría completa de la espada
        // ─────────────────────────────────────────────────────────────
        private void BuildSword(Vector3 origin, Matrix localRot,
                                Matrix bodyRot, Vector3 feet, float bob)
        {
            float v = S * 0.85f;

            // ── Pommel octagonal ──────────────────────────────────────
            AddSwordBox(-1.1f*v, 0f,     -1.1f*v,  1.1f*v, 0.8f*v,  1.1f*v,
                        origin, localRot, bodyRot, feet, bob, CPommel, CPommelDk);
            AddSwordBox(-1.3f*v, 0.8f*v, -0.8f*v,  1.3f*v, 2.2f*v,  0.8f*v,
                        origin, localRot, bodyRot, feet, bob, CPommel, CPommelDk);
            AddSwordBox(-0.8f*v, 0.8f*v, -1.3f*v,  0.8f*v, 2.2f*v,  1.3f*v,
                        origin, localRot, bodyRot, feet, bob, CPommel, CPommelDk);
            AddSwordBox(-0.6f*v, 2.2f*v, -0.6f*v,  0.6f*v, 2.6f*v,  0.6f*v,
                        origin, localRot, bodyRot, feet, bob, CPommelHi, CPommel);

            // ── Grip — 5 tramos alternados ────────────────────────────
            float gripBase = 2.6f * v;
            for (int i = 0; i < 5; i++)
            {
                float y0     = gripBase + i * 1.4f * v;
                bool  isWrap = (i % 2 == 0);
                Color gc     = isWrap ? CGripWrap   : CGrip;
                Color ghi    = isWrap ? CGripWrapHi : Lerp(CGrip, Color.White, 0.08f);
                Color gdk    = Lerp(gc, Color.Black, 0.35f);
                AddSwordBox(-0.55f*v, y0, -0.55f*v,
                             0.55f*v, y0 + 1.4f*v, 0.55f*v,
                             origin, localRot, bodyRot, feet, bob, gc, gdk);
                if (isWrap)
                    AddSwordBox( 0.45f*v, y0+0.15f*v, -0.30f*v,
                                 0.60f*v, y0+1.25f*v,  0.30f*v,
                                 origin, localRot, bodyRot, feet, bob, ghi, gc);
            }

            // ── Guardamano ────────────────────────────────────────────
            float gY = gripBase + 5f * 1.4f * v;
            AddSwordBox(-4.2f*v, gY,         -0.75f*v,
                         4.2f*v, gY + 1.3f*v,  0.75f*v,
                         origin, localRot, bodyRot, feet, bob, CGuard, CGuardDk);
            AddSwordBox(-4.0f*v, gY + 0.9f*v, -0.55f*v,
                         4.0f*v, gY + 1.3f*v,  0.55f*v,
                         origin, localRot, bodyRot, feet, bob, CGuardHi, CGuard);
            AddSwordBox(-1.1f*v, gY - 0.5f*v, -1.1f*v,
                         1.1f*v, gY + 1.8f*v,  1.1f*v,
                         origin, localRot, bodyRot, feet, bob, CGuard, CGuardDk);
            AddSwordBox(-0.5f*v, gY + 0.1f*v, -1.15f*v,
                         0.5f*v, gY + 1.1f*v,  -0.9f*v,
                         origin, localRot, bodyRot, feet, bob, CGuardHi, CGuard);

            // ── Hoja — 9 segmentos con taper ─────────────────────────
            float bladeY0   = gY + 1.8f * v;
            int   bladeSegs = 9;
            for (int i = 0; i < bladeSegs; i++)
            {
                float taper = 1f - (float)i / (bladeSegs - 1) * 0.92f;
                float hw    = v * 1.00f * taper;
                float th    = v * 0.22f * taper;
                float ful   = v * 0.12f * taper;
                float y0    = bladeY0 + i * 1.6f * v;
                float y1    = y0 + 1.6f * v;

                // Fuller (canal central), lado izquierdo, lado derecho
                AddSwordBox(-hw,  y0, -th, -ful, y1,  th,
                            origin, localRot, bodyRot, feet, bob, CBladeMid, CBladeDark);
                AddSwordBox(-ful, y0, -th,  ful, y1,  th,
                            origin, localRot, bodyRot, feet, bob, CBladeFuller, CBladeDark);
                AddSwordBox( ful, y0, -th,  hw,  y1,  th,
                            origin, localRot, bodyRot, feet, bob, CBladeHi, CBladeMid);

                // Filo brillante
                if (i < bladeSegs - 1)
                    AddSwordBox(hw - v*0.12f*taper, y0, -th*0.6f,
                                 hw, y1, th*0.6f,
                                 origin, localRot, bodyRot, feet, bob,
                                 Lighten(CBladeHi, 25), CBladeHi);
            }

            // ── Punta ─────────────────────────────────────────────────
            float tipY = bladeY0 + bladeSegs * 1.6f * v;
            AddSwordBox(-v*0.10f, tipY, -v*0.08f,
                         v*0.10f, tipY + v*0.9f, v*0.08f,
                         origin, localRot, bodyRot, feet, bob, CBladeHi, CBladeMid);
        }

        // ─────────────────────────────────────────────────────────────
        //  Primitiva de caja para la espada
        //  (usa bodyRot + feet en lugar de pivotLocal/swingMat)
        // ─────────────────────────────────────────────────────────────
        private void AddSwordBox(float x0, float y0, float z0,
                                 float x1, float y1, float z1,
                                 Vector3 origin, Matrix localRot,
                                 Matrix bodyRot, Vector3 feet, float bob,
                                 Color light, Color dark, Color? side = null)
        {
            Color s = side ?? Lerp(light, dark, 0.18f);

            Span<Vector3> c = stackalloc Vector3[8];
            c[0] = new Vector3(x0, y0, z0); c[1] = new Vector3(x1, y0, z0);
            c[2] = new Vector3(x1, y1, z0); c[3] = new Vector3(x0, y1, z0);
            c[4] = new Vector3(x0, y0, z1); c[5] = new Vector3(x1, y0, z1);
            c[6] = new Vector3(x1, y1, z1); c[7] = new Vector3(x0, y1, z1);

            for (int i = 0; i < 8; i++)
            {
                c[i] = Vector3.Transform(c[i], localRot);
                c[i] += origin;
                c[i] = Vector3.Transform(c[i], bodyRot);
                c[i] += feet + new Vector3(0f, bob, 0f);
            }

            EmitQuad(c[0], c[1], c[2], c[3], light);
            EmitQuad(c[5], c[4], c[7], c[6], Lerp(dark, Color.Black, 0.30f));
            EmitQuad(c[4], c[0], c[3], c[7], Lerp(s, dark, 0.25f));
            EmitQuad(c[1], c[5], c[6], c[2], Lerp(s, light, 0.10f));
            EmitQuad(c[3], c[2], c[6], c[7], Lighten(light, 35));
            EmitQuad(c[4], c[5], c[1], c[0], Lerp(dark, Color.Black, 0.18f));
        }
    }
}