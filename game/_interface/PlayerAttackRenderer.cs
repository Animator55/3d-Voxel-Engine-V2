using Microsoft.Xna.Framework;
using System;

namespace game
{
    /// <summary>
    /// Frames de ataque del combo (3 golpes) y deformaciones corporales
    /// asociadas a cada golpe.
    ///
    ///  Golpe 0 — Swing horizontal D→I
    ///  Golpe 1 — Swing horizontal I→D (espejo)
    ///  Golpe 2 — Estocada profunda
    /// </summary>
    public partial class PlayerRenderer
    {
        // ─────────────────────────────────────────────────────────────
        //  Deformaciones corporales según el estado de combate
        //  Devuelve los offsets que el método Draw aplica a bodyRot/headRot.
        // ─────────────────────────────────────────────────────────────
        private void GetCombatBodyOffsets(
            float rawP,
            out float bodyLeanX,
            out float bodyLeanZ,
            out float bodyYawOff,
            out float feetSpreadExtra,
            out float headLookZ,
            out float headNodX,
            out float impactShake)
        {
            bodyLeanX       = 0f;
            bodyLeanZ       = 0f;
            bodyYawOff      = 0f;
            feetSpreadExtra = 0f;
            headLookZ       = 0f;
            headNodX        = 0f;
            impactShake     = 0f;

            float hitFrac = rawP < 0.62f ? 0f : EaseOutImpact((rawP - 0.62f) / 0.38f);

            switch (_comboStep)
            {
                // ── Golpe 0: Swing D→I ────────────────────────────────
                // Cuerpo rota hacia la derecha en wind-up y luego barre
                // hacia la izquierda con lean lateral pronunciado.
                case 0:
                {
                    float sf     = rawP < 0.25f ? 0f : EaseOutImpact((rawP - 0.25f) / 0.75f);
                    float windup = rawP < 0.25f ? EaseOutCubic(rawP / 0.25f) : 0f;

                    bodyYawOff  = MathHelper.Lerp(0f, MathHelper.ToRadians(-22f), windup)
                                + MathHelper.Lerp(0f, MathHelper.ToRadians( 28f), sf);
                    bodyLeanZ   = MathHelper.Lerp(0f, MathHelper.ToRadians(-18f), sf);
                    bodyLeanX   = MathHelper.Lerp(0f, MathHelper.ToRadians( 12f), sf);
                    headLookZ   = MathHelper.Lerp(0f, MathHelper.ToRadians(-20f), sf);
                    headNodX    = MathHelper.Lerp(0f, MathHelper.ToRadians( 10f), sf);
                    impactShake = (float)Math.Sin(rawP * MathHelper.Pi) * 0.025f * hitFrac;
                    break;
                }

                // ── Golpe 1: Swing I→D ────────────────────────────────
                // Espejo exacto del golpe 0.
                case 1:
                {
                    float sf     = rawP < 0.25f ? 0f : EaseOutImpact((rawP - 0.25f) / 0.75f);
                    float windup = rawP < 0.25f ? EaseOutCubic(rawP / 0.25f) : 0f;

                    bodyYawOff  = MathHelper.Lerp(0f, MathHelper.ToRadians( 22f), windup)
                                + MathHelper.Lerp(0f, MathHelper.ToRadians(-28f), sf);
                    bodyLeanZ   = MathHelper.Lerp(0f, MathHelper.ToRadians( 18f), sf);
                    bodyLeanX   = MathHelper.Lerp(0f, MathHelper.ToRadians( 12f), sf);
                    headLookZ   = MathHelper.Lerp(0f, MathHelper.ToRadians( 20f), sf);
                    headNodX    = MathHelper.Lerp(0f, MathHelper.ToRadians( 10f), sf);
                    impactShake = (float)Math.Sin(rawP * MathHelper.Pi) * 0.025f * hitFrac;
                    break;
                }

                // ── Golpe 2: Estocada profunda ────────────────────────
                // Cuerpo se lanza hacia adelante; pies más separados.
                case 2:
                {
                    float tf = rawP < 0.20f ? 0f
                             : rawP <= 0.65f ? EaseOutImpact((rawP - 0.20f) / 0.45f)
                             : 1f - EaseOut((rawP - 0.65f) / 0.35f);
                    tf = MathHelper.Clamp(tf, 0f, 1f);

                    bodyLeanX       = MathHelper.Lerp(0f, MathHelper.ToRadians(-28f), tf);
                    feetSpreadExtra = tf * 1.8f * S;
                    headNodX        = MathHelper.Lerp(0f, MathHelper.ToRadians(-18f), tf);
                    impactShake     = tf * 0.018f;
                    break;
                }
            }

            // Durante draw: pequeño lean hacia la espada
            if (Mode == SwordMode.Drawing)
            {
                float dp  = MathHelper.Clamp(_stateTimer / DrawDuration, 0f, 1f);
                bodyLeanZ = MathHelper.Lerp(0f, MathHelper.ToRadians(12f), EaseOut(dp));
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Frame de ataque — despacha al golpe correcto
        // ─────────────────────────────────────────────────────────────
        private void BuildAttackFrame(int step, float rawP,
                                      float bodyTop, float walkSwing,
                                      Matrix bodyRot, Vector3 feet, float bob)
        {
            const float ShoulderX  =  5.0f * S;
            const float ShoulderXL = -5.0f * S;
            float shoulderY = bodyTop - 1.2f * S;

            switch (step)
            {
                case 0: BuildAttack0(rawP, ShoulderX, ShoulderXL, shoulderY, bodyRot, feet, bob); break;
                case 1: BuildAttack1(rawP, ShoulderX, ShoulderXL, shoulderY, bodyRot, feet, bob); break;
                case 2: BuildAttack2(rawP, ShoulderX, ShoulderXL, shoulderY, bodyRot, feet, bob); break;
                default:
                    BuildSwordAtArmEnd(ShoulderX, shoulderY, -0.35f,
                                       MathHelper.ToRadians(-15f), 0f,
                                       bodyRot, feet, bob);
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Golpe 0 — Swing horizontal D→I
        //  Wind-up exagerado (0.85), barrido amplio (-85° → +110°).
        //  El brazo derecho ataca; el izquierdo da el contrapeso.
        // ─────────────────────────────────────────────────────────────
        private void BuildAttack0(float rawP,
                                   float sX, float sXL, float shoulderY,
                                   Matrix bodyRot, Vector3 feet, float bob)
        {
            float armPitch, leftArm;

            if (rawP < 0.25f)
            {
                float wf = rawP / 0.25f;
                armPitch = MathHelper.Lerp(-0.35f,  0.85f, wf);
                leftArm  = MathHelper.Lerp( 0.20f,  0.60f, wf);
            }
            else
            {
                float sf = EaseOutImpact((rawP - 0.25f) / 0.75f);
                armPitch = MathHelper.Lerp( 0.85f, -1.05f, sf);
                leftArm  = MathHelper.Lerp( 0.60f, -0.65f, sf);
            }

            BuildVoxelArm(sXL, shoulderY, leftArm,  bodyRot, feet, bob);
            BuildVoxelArm(sX,  shoulderY, armPitch, bodyRot, feet, bob);

            float handX = sX;
            float handY = shoulderY - ArmLen * (float)Math.Cos(armPitch);
            float handZ = -ArmLen   * (float)Math.Sin(armPitch);

            float swingFrac = rawP < 0.25f ? 0f
                            : EaseOutImpact((rawP - 0.25f) / 0.75f);
            float sweepY = MathHelper.Lerp(MathHelper.ToRadians(-85f),
                                           MathHelper.ToRadians(110f), swingFrac);
            float dropZ  = MathHelper.Lerp(0f, MathHelper.ToRadians(38f), swingFrac);

            var sRot = Matrix.CreateRotationX(MathHelper.ToRadians(-92f))
                     * Matrix.CreateRotationY(sweepY)
                     * Matrix.CreateRotationZ(-dropZ);
            BuildSwordInHand(new Vector3(handX, handY, handZ), sRot, bodyRot, feet, bob);
        }

        // ─────────────────────────────────────────────────────────────
        //  Golpe 1 — Swing horizontal I→D
        //  Espejo exacto del golpe 0: brazo IZQUIERDO ataca,
        //  barrido en sentido contrario (+85° → -110°).
        //  La geometría espejo garantiza que la mano no cruce el torso.
        // ─────────────────────────────────────────────────────────────
        private void BuildAttack1(float rawP,
                                   float sX, float sXL, float shoulderY,
                                   Matrix bodyRot, Vector3 feet, float bob)
        {
            float armPitch, rightArm;

            if (rawP < 0.25f)
            {
                float wf = rawP / 0.25f;
                armPitch = MathHelper.Lerp(-0.35f,  0.85f, wf);
                rightArm = MathHelper.Lerp(-0.35f,  0.60f, wf);
            }
            else
            {
                float sf = EaseOutImpact((rawP - 0.25f) / 0.75f);
                armPitch = MathHelper.Lerp( 0.85f, -1.05f, sf);
                rightArm = MathHelper.Lerp( 0.60f, -0.65f, sf);
            }

            // Brazo izquierdo ataca; derecho da el contrapeso
            BuildVoxelArm(sXL, shoulderY, armPitch, bodyRot, feet, bob);
            BuildVoxelArm(sX,  shoulderY, rightArm, bodyRot, feet, bob);

            // Mano en el lado DERECHO — la espada cruza de derecha a izquierda
            float handX = sX;
            float handY = shoulderY - ArmLen * (float)Math.Cos(armPitch);
            float handZ = -ArmLen   * (float)Math.Sin(armPitch);

            float swingFrac = rawP < 0.25f ? 0f
                            : EaseOutImpact((rawP - 0.25f) / 0.75f);
            float sweepY = MathHelper.Lerp(MathHelper.ToRadians( 85f),
                                           MathHelper.ToRadians(-110f), swingFrac);
            float dropZ  = MathHelper.Lerp(0f, MathHelper.ToRadians(38f), swingFrac);

            var sRot = Matrix.CreateRotationX(MathHelper.ToRadians(-92f))
                     * Matrix.CreateRotationY(sweepY)
                     * Matrix.CreateRotationZ(dropZ);   // dropZ invertido respecto al golpe 0
            BuildSwordInHand(new Vector3(handX, handY, handZ), sRot, bodyRot, feet, bob);
        }

        // ─────────────────────────────────────────────────────────────
        //  Golpe 2 — Estocada profunda
        //  Small wind-up hacia atrás → lanzada recta → recovery.
        //  handY fijo (nivel hold) para evitar arco vertical.
        //  Lean en Z aumentado (6*S) para sensación de embestida.
        // ─────────────────────────────────────────────────────────────
        private void BuildAttack2(float rawP,
                                   float sX, float sXL, float shoulderY,
                                   Matrix bodyRot, Vector3 feet, float bob)
        {
            float thrust;

            if (rawP < 0.20f)
            {
                // Wind-up: brazo retrocede levemente
                float wf = rawP / 0.20f;
                thrust   = -0.12f * wf;
            }
            else if (rawP <= 0.65f)
            {
                thrust = EaseOutImpact((rawP - 0.20f) / 0.45f);
            }
            else
            {
                thrust = 1f - EaseOut((rawP - 0.65f) / 0.35f);
            }

            float thrustC    = MathHelper.Clamp(thrust, 0f, 1f);
            float thrustBack = rawP < 0.20f ? MathHelper.Clamp(-thrust, 0f, 1f) : 0f;

            float armPitch = rawP < 0.20f
                ? MathHelper.Lerp(-0.35f, -0.10f, thrustBack)
                : MathHelper.Lerp(-0.35f, -MathHelper.Pi * 0.82f, thrustC);
            float leftArm  = MathHelper.Lerp( 0.20f, -0.85f, thrustC);

            BuildVoxelArm(sXL, shoulderY, leftArm,  bodyRot, feet, bob);
            BuildVoxelArm(sX,  shoulderY, armPitch, bodyRot, feet, bob);

            // handY fijo al nivel hold: sin arco vertical
            float holdY = shoulderY - ArmLen * (float)Math.Cos(-0.35f);
            float handX = sX;
            float handY = holdY;
            float leanZ = thrustC * 6.0f * S;
            float handZ = -ArmLen * (float)Math.Sin(armPitch) - leanZ;

            float swordX = MathHelper.Lerp(MathHelper.ToRadians(-87f),
                                           MathHelper.ToRadians(-93f), thrustC);
            var sRot = Matrix.CreateRotationX(swordX)
                     * Matrix.CreateRotationZ(MathHelper.ToRadians(-4f));
            BuildSwordInHand(new Vector3(handX, handY, handZ), sRot, bodyRot, feet, bob);
        }
    }
}