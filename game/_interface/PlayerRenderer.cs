using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace game
{
    public enum SwordMode
    {
        Sheathed,
        Drawing,
        Holding,
        Attacking,
        Sheathing,
    }

    /// <summary>
    /// Cube World chibi player con espada.
    ///
    /// API pública:
    ///   RequestAttack()  — llamar en MouseButton.Left pressed.
    ///   RequestSheathe() — llamar en MouseButton.Right pressed (o automático).
    ///   SwordMode Mode   — estado actual (lectura).
    ///   HitStopTimer     — > 0 durante hitstop (útil para shake de cámara).
    ///
    /// La clase usa partial para dividir responsabilidades:
    ///   PlayerRenderer.cs      — núcleo: estado, Draw, cuerpo, primitivas.
    ///   SwordRenderer.cs       — geometría de la espada y sus posiciones.
    ///   AttackRenderer.cs      — frames de ataque y deformaciones corporales.
    /// </summary>
    public partial class PlayerRenderer : IDisposable
    {
        // ── Duraciones ────────────────────────────────────────────────
        private const float DrawDuration    = 0.14f;
        private const float SheathDuration  = 0.18f;
        private const float HoldAutoSheathe = 6.0f;
        private const float AttackDuration  = 0.18f;
        private const float HitStopDuration = 0.04f;
        private const int   ComboLength     = 3;

        // ── Escala ────────────────────────────────────────────────────
        private const float S         = 0.068f;
        private const float MoveSpeed = 6f;
        private const float ArmLen    = 4.4f * S;

        // ── Paleta personaje ──────────────────────────────────────────
        private static readonly Color CSkin      = new Color(240, 200, 160);
        private static readonly Color CSkinShade = new Color(205, 165, 125);
        private static readonly Color CSkinDark  = new Color(170, 128,  90);

        private static readonly Color CHair      = new Color(255, 210,  40);
        private static readonly Color CHairMid   = new Color(220, 175,  20);
        private static readonly Color CHairDark  = new Color(175, 135,  10);

        private static readonly Color CEyeBlack  = new Color( 20,  18,  22);
        private static readonly Color CEyeBlue   = new Color( 60, 120, 230);
        private static readonly Color CEyeWhite  = new Color(235, 235, 240);

        private static readonly Color CTunic     = new Color(115,  85, 165);
        private static readonly Color CTunicMid  = new Color( 90,  65, 135);
        private static readonly Color CTunicDark = new Color( 65,  45, 105);

        private static readonly Color CCollar    = new Color( 80, 145,  70);
        private static readonly Color CCollarDk  = new Color( 55, 105,  48);

        private static readonly Color CBuckle    = new Color(195, 195, 200);
        private static readonly Color CBuckleDk  = new Color(140, 140, 148);

        private static readonly Color CBoot      = new Color( 68,  65,  72);
        private static readonly Color CBootMid   = new Color( 50,  48,  55);
        private static readonly Color CBootDark  = new Color( 32,  30,  36);
        private static readonly Color CBootSole  = new Color( 24,  22,  28);

        // ── Estado ────────────────────────────────────────────────────
        public  SwordMode Mode         { get; private set; } = SwordMode.Sheathed;
        public  float     HitStopTimer { get; private set; } = 0f;
        private float     _stateTimer    = 0f;
        private int       _comboStep     = 0;
        private bool      _pendingAttack = false;
        private bool      _inHitStop     = false;

        // ── GFX ───────────────────────────────────────────────────────
        private readonly GraphicsDevice _gd;
        private BasicEffect _effect;

        private readonly VertexPositionColor[] _verts = new VertexPositionColor[131072];
        private readonly short[]               _idx   = new short[262144];
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

        // ─────────────────────────────────────────────────────────────
        //  API pública
        // ─────────────────────────────────────────────────────────────

        public void RequestAttack()
        {
            switch (Mode)
            {
                case SwordMode.Sheathed:
                case SwordMode.Sheathing:
                    Mode           = SwordMode.Drawing;
                    _stateTimer    = 0f;
                    _pendingAttack = true;
                    break;
                case SwordMode.Drawing:
                    _pendingAttack = true;
                    break;
                case SwordMode.Holding:
                    _comboStep  = 0;
                    Mode        = SwordMode.Attacking;
                    _stateTimer = 0f;
                    _inHitStop  = false;
                    break;
                case SwordMode.Attacking:
                    if (_comboStep < ComboLength - 1)
                        _pendingAttack = true;
                    break;
            }
        }

        public void RequestSheathe()
        {
            if (Mode == SwordMode.Holding || Mode == SwordMode.Attacking)
            {
                Mode           = SwordMode.Sheathing;
                _stateTimer    = 0f;
                _pendingAttack = false;
                _inHitStop     = false;
                HitStopTimer   = 0f;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Draw
        // ─────────────────────────────────────────────────────────────
        public void Draw(Vector3 feetPos,
                         float bodyYaw, float headYawOffset,
                         Matrix view, Matrix proj,
                         bool isGrounded, Vector3 velocity,
                         GameTime gameTime,
                         bool isMoving = false)   // ← NUEVO parámetro
        {
            _vc = _ic = 0;

            float dt    = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float time  = (float)gameTime.TotalGameTime.TotalSeconds;
            float hspd  = (float)Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            float walkT = isGrounded ? Math.Min(hspd / MoveSpeed, 1f) : 0f;

            // ── Pose en el aire ───────────────────────────────────────
            // Si el jugador está en el aire con input activo, congelamos
            // walkPhase en π/2 (pico del seno = zancada más extendida) y
            // forzamos walkT a 1 para que la pose sea completamente visible.
            bool airWithInput = !isGrounded && isMoving;

            float walkPhase;
            if (airWithInput)
            {
                walkPhase = MathHelper.PiOver2;   // congela en el paso más extendido
                walkT     = 1f;                   // pose completamente aplicada
            }
            else
            {
                walkPhase = time * 16f;
                // walkT ya está calculado arriba (0 en el aire sin input)
            }

            float swing = (float)Math.Sin(walkPhase) * walkT;
            float bob   = (float)Math.Abs(Math.Sin(walkPhase * 2f)) * walkT * 0.022f;

            UpdateSwordState(dt);

            // rawP normalizado del ataque actual, 0 fuera de ataque
            float rawP = (Mode == SwordMode.Attacking)
                       ? MathHelper.Clamp(_stateTimer / AttackDuration, 0f, 1f)
                       : 0f;

            // ── Deformaciones de combate ──────────────────────────────
            float bodyLeanX, bodyLeanZ, bodyYawOff, feetSpreadExtra;
            float headLookZ, headNodX, impactShake;

            if (Mode == SwordMode.Attacking || Mode == SwordMode.Drawing || Mode == SwordMode.Sheathing)
            {
                GetCombatBodyOffsets(rawP,
                    out bodyLeanX, out bodyLeanZ, out bodyYawOff,
                    out feetSpreadExtra, out headLookZ, out headNodX, out impactShake);
            }
            else
            {
                bodyLeanX = bodyLeanZ = bodyYawOff = 0f;
                feetSpreadExtra = headLookZ = headNodX = impactShake = 0f;
            }

            // ── Matrices con deformación ──────────────────────────────
            Matrix bodyRot = Matrix.CreateRotationX(bodyLeanX)
                           * Matrix.CreateRotationZ(bodyLeanZ)
                           * Matrix.CreateRotationY(bodyYaw + bodyYawOff);

            Matrix headRot = Matrix.CreateRotationX(headNodX)
                           * Matrix.CreateRotationZ(headLookZ)
                           * Matrix.CreateRotationY(bodyYaw + bodyYawOff * 0.5f + headYawOffset);

            const float bootH = 2.8f * S;
            const float bodyH = 5.5f * S;
            float bodyBot = bootH;
            float bodyTop = bodyBot + bodyH;
            float headBot = bodyTop - 0.3f * S;

            float activeBob = (Mode == SwordMode.Attacking) ? impactShake : bob;

            BuildVoxelFoot(-2.2f * S - feetSpreadExtra, 0f,  swing, bodyRot, feetPos, activeBob);
            BuildVoxelFoot( 2.2f * S + feetSpreadExtra, 0f, -swing, bodyRot, feetPos, activeBob);
            BuildVoxelBody(bodyBot, bodyTop, bodyRot, feetPos, activeBob);
            BuildVoxelHead(headBot, headRot, feetPos, activeBob);
            BuildArmsAndSword(bodyTop, swing, bodyRot, feetPos, activeBob, time);

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

        // ─────────────────────────────────────────────────────────────
        //  Máquina de estados
        // ─────────────────────────────────────────────────────────────
        private void UpdateSwordState(float dt)
        {
            if (HitStopTimer > 0f)
            {
                HitStopTimer -= dt;
                return;
            }

            _stateTimer += dt;

            switch (Mode)
            {
                case SwordMode.Drawing:
                    if (_stateTimer >= DrawDuration)
                    {
                        Mode        = SwordMode.Holding;
                        _stateTimer = 0f;
                        _comboStep  = 0;
                        if (_pendingAttack)
                        {
                            _pendingAttack = false;
                            Mode        = SwordMode.Attacking;
                            _stateTimer = 0f;
                        }
                    }
                    break;

                case SwordMode.Sheathing:
                    if (_stateTimer >= SheathDuration)
                    {
                        Mode        = SwordMode.Sheathed;
                        _stateTimer = 0f;
                    }
                    break;

                case SwordMode.Holding:
                    if (_stateTimer >= HoldAutoSheathe)
                        RequestSheathe();
                    break;

                case SwordMode.Attacking:
                    float hitPoint = AttackDuration * 0.62f;
                    if (!_inHitStop && _stateTimer >= hitPoint)
                    {
                        _inHitStop   = true;
                        HitStopTimer = HitStopDuration;
                        return;
                    }
                    if (_stateTimer >= AttackDuration)
                    {
                        _inHitStop = false;
                        _comboStep++;
                        if (_pendingAttack && _comboStep < ComboLength)
                        {
                            _pendingAttack = false;
                            _stateTimer    = 0f;
                            _inHitStop     = false;
                        }
                        else
                        {
                            Mode        = SwordMode.Holding;
                            _stateTimer = 0f;
                            _comboStep  = 0;
                        }
                    }
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Brazos + espada — despacha según estado
        // ─────────────────────────────────────────────────────────────
        private void BuildArmsAndSword(float bodyTop, float walkSwing,
                                       Matrix bodyRot, Vector3 feet, float bob,
                                       float time)
        {
            float t         = MathHelper.Clamp(_stateTimer, 0f, 10f);
            float shoulderY = bodyTop - 1.2f * S;

            switch (Mode)
            {
                case SwordMode.Sheathed:
                    BuildVoxelArm(-5.0f * S, shoulderY,  walkSwing, bodyRot, feet, bob);
                    BuildVoxelArm( 5.0f * S, shoulderY, -walkSwing, bodyRot, feet, bob);
                    BuildSheathedSword(bodyRot, feet, bob);
                    break;

                case SwordMode.Drawing:
                {
                    float p        = EaseOutElastic(t / DrawDuration, 0.4f);
                    float armPitch = MathHelper.Lerp(-walkSwing, MathHelper.Pi * 0.55f, p);
                    BuildVoxelArm(-5.0f * S, shoulderY,  walkSwing, bodyRot, feet, bob);
                    BuildVoxelArm( 5.0f * S, shoulderY,  armPitch,  bodyRot, feet, bob);
                    BuildDrawingSword(MathHelper.Clamp(p, 0f, 1f), bodyRot, feet, bob);
                    break;
                }

                case SwordMode.Holding:
                {
                    float idleBob = (float)Math.Sin(time * 1.8f) * 0.008f;
                    float armIdle = -0.35f + idleBob;
                    BuildVoxelArm(-5.0f * S, shoulderY,  walkSwing, bodyRot, feet, bob);
                    BuildVoxelArm( 5.0f * S, shoulderY,  armIdle,   bodyRot, feet, bob);
                    BuildSwordAtArmEnd(5.0f * S, shoulderY, armIdle,
                                       MathHelper.ToRadians(-15f), 0f,
                                       bodyRot, feet, bob);
                    break;
                }

                case SwordMode.Sheathing:
                {
                    float p        = EaseOut(t / SheathDuration);
                    float armPitch = MathHelper.Lerp(MathHelper.Pi * 0.55f, -walkSwing, p);
                    BuildVoxelArm(-5.0f * S, shoulderY,  walkSwing, bodyRot, feet, bob);
                    BuildVoxelArm( 5.0f * S, shoulderY,  armPitch,  bodyRot, feet, bob);
                    BuildDrawingSword(1f - p, bodyRot, feet, bob);
                    break;
                }

                case SwordMode.Attacking:
                {
                    float rawP = MathHelper.Clamp(t / AttackDuration, 0f, 1f);
                    BuildAttackFrame(_comboStep, rawP, bodyTop, walkSwing, bodyRot, feet, bob);
                    break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Partes del cuerpo
        // ─────────────────────────────────────────────────────────────

        private void BuildVoxelFoot(float cx, float botY, float swing,
                                     Matrix bodyRot, Vector3 feet, float bob)
        {
            float v  = S * 0.95f;
            float rX = 2.3f, rY = 1.4f, rZ = 3.0f;
            float centreY = botY + rY * v;
            float centreZ = -0.6f * v;
            var   pivot   = new Vector3(cx, botY + rY * 2f * v, 0f);
            var   swM     = Matrix.CreateRotationX(swing * 0.85f);

            int iX = (int)Math.Ceiling(rX);
            int iY = (int)Math.Ceiling(rY);
            int iZ = (int)Math.Ceiling(rZ);

            for (int xi = -iX; xi <= iX; xi++)
            for (int yi = -iY; yi <= iY; yi++)
            for (int zi = -iZ; zi <= iZ; zi++)
            {
                float nx = xi/rX, ny = yi/rY, nz = zi/rZ;
                if (nx*nx + ny*ny + nz*nz > 1.0f) continue;

                float wx0 = cx+xi*v, wy0 = centreY+yi*v, wz0 = centreZ+zi*v;
                float yFade = (yi+iY)/(float)(iY*2);
                Color light = Lerp(CBootDark, CBoot,    yFade);
                Color dark  = Lerp(CBootSole, CBootMid, yFade * 0.6f);
                float zExt  = (zi == -iZ) ? v * 0.2f : 0f;

                AddBox(wx0, wy0, wz0-zExt, wx0+v, wy0+v, wz0+v,
                       pivot, swM, bodyRot, feet, bob, light, dark);
            }
        }

        private void BuildVoxelBody(float botY, float topY,
                                     Matrix bodyRot, Vector3 feet, float bob)
        {
            float v  = S;
            float rX = 3.5f;
            float rY = (topY - botY) / (2f * v);
            float rZ = 2.3f;
            float cx = 0f, cy = botY + rY * v, cz = 0f;

            int iX = (int)Math.Ceiling(rX);
            int iY = (int)Math.Ceiling(rY);
            int iZ = (int)Math.Ceiling(rZ);

            int collarYMin = (int)(iY * 0.58f);
            int collarXMax = 2;
            int buckleYMin = (int)(iY * -0.08f);
            int buckleYMax = (int)(iY *  0.25f);

            for (int xi = -iX; xi <= iX; xi++)
            for (int yi = -iY; yi <= iY; yi++)
            for (int zi = -iZ; zi <= iZ; zi++)
            {
                float nx = xi/rX, ny = yi/rY, nz = zi/rZ;
                if (nx*nx + ny*ny + nz*nz > 1.0f) continue;

                float wx0 = cx+xi*v, wy0 = cy+yi*v, wz0 = cz+zi*v;
                float yFade = (yi+iY)/(float)(iY*2);
                float zFade = 1f-(zi+iZ)/(float)(iZ*2);
                float blend = yFade*0.6f + zFade*0.4f;

                Color baseL = Lerp(CTunicDark, CTunic,    blend);
                Color baseD = Lerp(CTunicDark, CTunicMid, blend * 0.55f);

                bool isFront  = (zi == -iZ);
                bool isCollar = isFront && yi >= collarYMin && Math.Abs(xi) <= collarXMax;
                bool isBuckle = isFront && yi >= buckleYMin && yi <= buckleYMax && Math.Abs(xi) <= 1;

                Color light = isCollar ? CCollar   : isBuckle ? CBuckle   : baseL;
                Color dark  = isCollar ? CCollarDk : isBuckle ? CBuckleDk : baseD;

                AddBox(wx0, wy0, wz0, wx0+v, wy0+v, wz0+v,
                       Vector3.Zero, Matrix.Identity, bodyRot, feet, bob, light, dark);
            }
        }

        private void BuildVoxelArm(float cx, float shoulderY, float swing,
                                    Matrix bodyRot, Vector3 feet, float bob)
        {
            float v   = S * 0.9f;
            float r   = 2.2f * S;
            var pivot = new Vector3(cx, shoulderY, 0f);
            var swM   = Matrix.CreateRotationX(swing * 1.1f);

            for (int iy = -2; iy <= 2; iy++)
            for (int ix = -2; ix <= 2; ix++)
            for (int iz = -2; iz <= 2; iz++)
            {
                float px = ix*v, py = iy*v, pz = iz*v;
                if (px*px + py*py + pz*pz > r*r*1.05f) continue;

                float shade = (iy+2)/4f;
                Color light = Lerp(CSkinDark, CSkin,      shade);
                Color dark  = Lerp(CSkinDark, CSkinShade, shade * 0.6f);

                AddBox(cx+px, shoulderY-r+py+v*0.5f, -v*0.5f+pz,
                       cx+px+v, shoulderY-r+py+v*0.5f+v, v*0.5f+pz,
                       pivot, swM, bodyRot, feet, bob, light, dark);
            }
        }

        private void BuildVoxelHead(float botY, Matrix headRot, Vector3 feet, float bob)
        {
            float v  = S;
            float cx = 0f, cz = 0f;
            float rX = 5.0f, rY = 5.5f, rZ = 4.2f;
            float cy = botY + rY * v;

            int xH = (int)Math.Ceiling(rX);
            int yR = (int)Math.Ceiling(rY * 2f);
            int zH = (int)Math.Ceiling(rZ);

            // ── Cara ─────────────────────────────────────────────────
            for (int row = 0; row <= yR; row++)
            {
                float wy0 = botY + row * v;
                float ny  = (wy0 + v*0.5f - cy) / (rY * v);
                for (int xi = -xH; xi <= xH; xi++)
                for (int zi = -zH; zi <= zH; zi++)
                {
                    float nx = xi/rX, nz = zi/rZ;
                    if (nx*nx + ny*ny + nz*nz > 1.0f) continue;
                    float yFade = (float)row / yR;
                    float xFade = (float)Math.Abs(xi) / (rX + 0.5f);
                    Color light = Lerp(CSkinShade, Lighten(CSkin, 20), yFade * (1f - xFade*0.35f));
                    Color dark  = Lerp(CSkinDark, CSkinShade, yFade * 0.5f);
                    AddBox(cx+xi*v, wy0, cz+zi*v, cx+xi*v+v, wy0+v, cz+zi*v+v,
                           Vector3.Zero, Matrix.Identity, headRot, feet, bob, light, dark);
                }
            }

            // ── Pelo ─────────────────────────────────────────────────
            float hrX = rX+1.1f, hrY = rY+0.9f, hrZ = rZ+0.7f;
            int   hxH = (int)Math.Ceiling(hrX);
            int   hyR = (int)Math.Ceiling(hrY * 2f);
            int   hzH = (int)Math.Ceiling(hrZ);

            for (int row = 0; row <= hyR; row++)
            {
                float wy0 = botY + row * v;
                float ny  = (wy0 + v*0.5f - cy) / (hrY * v);
                bool crownRow = row >= (int)(yR * 0.35f);
                bool sideRow  = row >= (int)(yR * 0.20f);
                for (int xi = -hxH; xi <= hxH; xi++)
                for (int zi = -hzH; zi <= hzH; zi++)
                {
                    float nx = xi/hrX, nz = zi/hrZ;
                    if (nx*nx + ny*ny + nz*nz > 1.0f) continue;
                    float nxS = xi/rX, nyS = (wy0+v*0.5f-cy)/(rY*v), nzS = zi/rZ;
                    if (nxS*nxS + nyS*nyS + nzS*nzS < 0.91f) continue;
                    bool isSide = Math.Abs(xi) >= (int)(hrX * 0.45f);
                    if (!crownRow && !isSide) continue;
                    if (!sideRow) continue;
                    float yExtra = (row >= yR) ? v * 0.5f : 0f;
                    float pad    = v * 0.06f;
                    AddBox(cx+xi*v-pad, wy0, cz+zi*v-pad,
                           cx+xi*v+v+pad, wy0+v+yExtra, cz+zi*v+v+pad,
                           Vector3.Zero, Matrix.Identity, headRot, feet, bob,
                           CHair, CHairDark, CHairMid);
                }
            }

            // ── Flequillo ────────────────────────────────────────────
            {
                float fringeBaseY = botY + (int)(yR * 0.40f) * v;
                float frontZ      = cz - rZ * v;
                int   fW          = (int)(rX * 0.75f);
                for (int xi = -fW; xi <= fW; xi++)
                {
                    float t2      = (float)Math.Abs(xi) / fW;
                    float fringeH = v * (1.7f + (1f - t2*t2) * 1.0f);
                    float wx0     = cx + xi * v;
                    AddBox(wx0, fringeBaseY-v*0.4f, frontZ-v*1.2f,
                           wx0+v, fringeBaseY+fringeH, frontZ,
                           Vector3.Zero, Matrix.Identity, headRot, feet, bob,
                           CHair, CHairDark, CHairMid);
                    AddBox(wx0, fringeBaseY, frontZ,
                           wx0+v, fringeBaseY+fringeH*0.7f, frontZ+v*0.7f,
                           Vector3.Zero, Matrix.Identity, headRot, feet, bob,
                           CHairMid, CHairDark);
                }
            }

            // ── Ojos ─────────────────────────────────────────────────
            float faceZ  = cz - rZ * v;
            float eyeBot = botY + (int)(yR * 0.35f) * v;

            int[][] eyeRanges = { new[] { -4, -2 }, new[] { 1, 3 } };
            foreach (int[] range in eyeRanges)
            {
                for (int xi = range[0]; xi <= range[1]; xi++)
                for (int yi = 0; yi < 4; yi++)
                {
                    float wx0      = cx + xi * v;
                    float wy0      = eyeBot + yi * v;
                    bool  isIris   = (xi == range[0] + 1);
                    bool  isSclera = !isIris && (yi == 0 || yi == 3);
                    Color faceLight = isIris    ? CEyeBlue
                                    : isSclera  ? CEyeWhite
                                                : CEyeBlack;
                    Color faceDark  = isIris    ? Lerp(CEyeBlue, CEyeBlack, 0.5f)
                                    : isSclera  ? Lerp(CEyeWhite, CEyeBlack, 0.35f)
                                                : CEyeBlack;
                    AddBox(wx0, wy0, faceZ-v*0.6f, wx0+v, wy0+v, faceZ,
                           Vector3.Zero, Matrix.Identity, headRot, feet, bob,
                           faceLight, faceDark);
                }
            }

            // ── Boca ─────────────────────────────────────────────────
            float mouthY = eyeBot - v * 1.4f;
            for (int xi = -1; xi <= 1; xi++)
                AddBox(cx+xi*v, mouthY, faceZ-v*0.4f, cx+xi*v+v, mouthY+v*0.6f, faceZ,
                       Vector3.Zero, Matrix.Identity, headRot, feet, bob,
                       CEyeBlack, CEyeBlack);
        }

        // ─────────────────────────────────────────────────────────────
        //  Primitivas de geometría
        // ─────────────────────────────────────────────────────────────

        private void AddBox(float x0, float y0, float z0,
                            float x1, float y1, float z1,
                            Vector3 pivotLocal, Matrix swingMat,
                            Matrix rot, Vector3 worldOrigin, float bobY,
                            Color colLight, Color colDark, Color? colSide = null)
        {
            Color side = colSide ?? Lerp(colLight, colDark, 0.18f);

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
            EmitQuad(c[5], c[4], c[7], c[6], Lerp(colDark, Color.Black, 0.30f));
            EmitQuad(c[4], c[0], c[3], c[7], Lerp(side, colDark, 0.25f));
            EmitQuad(c[1], c[5], c[6], c[2], Lerp(side, colLight, 0.10f));
            EmitQuad(c[3], c[2], c[6], c[7], Lighten(colLight, 35));
            EmitQuad(c[4], c[5], c[1], c[0], Lerp(colDark, Color.Black, 0.18f));
        }

        private void EmitQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            if (_vc + 4 > _verts.Length || _ic + 6 > _idx.Length) return;
            short b0 = (short)_vc;
            _verts[_vc++] = new VertexPositionColor(a, col);
            _verts[_vc++] = new VertexPositionColor(b, col);
            _verts[_vc++] = new VertexPositionColor(c, col);
            _verts[_vc++] = new VertexPositionColor(d, col);
            _idx[_ic++] = b0;             _idx[_ic++] = (short)(b0+1); _idx[_ic++] = (short)(b0+2);
            _idx[_ic++] = b0;             _idx[_ic++] = (short)(b0+2); _idx[_ic++] = (short)(b0+3);
        }

        // ─────────────────────────────────────────────────────────────
        //  Easing
        // ─────────────────────────────────────────────────────────────
        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        private static float EaseOutImpact(float t)
        {
            if (t < 0.75f)
                return EaseOutCubic(t / 0.75f) * 0.92f;
            float u = (t - 0.75f) / 0.25f;
            return 0.92f + (float)Math.Sin(u * Math.PI) * 0.12f;
        }

        private static float EaseOutCubic(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        private static float EaseOutElastic(float t, float amplitude)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float decay = (float)Math.Exp(-6f * t);
            float osc   = (float)Math.Sin(t * Math.PI * 3.5f);
            return 1f - decay * osc * amplitude;
        }

        // ── Utilidades de color ───────────────────────────────────────
        private static Color Lighten(Color c, int a) =>
            new Color(Math.Min(255, c.R + a), Math.Min(255, c.G + a), Math.Min(255, c.B + a));

        private static Color Lerp(Color a, Color b, float t) =>
            new Color((int)(a.R + (b.R - a.R) * t),
                      (int)(a.G + (b.G - a.G) * t),
                      (int)(a.B + (b.B - a.B) * t));

        public void Dispose() => _effect?.Dispose();
    }
}