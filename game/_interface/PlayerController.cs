using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace game
{
    public class PlayerController
    {
        private const float Gravity          = -28f;
        private const float JumpVelocity     = 15f;
        private const float MoveSpeed        = 10f;
        private const float SprintMultiplier = 1.8f;
        private const float MaxFallSpeed     = 50f;

        public const float Width  = 0.6f;
        public const float Height = 1.8f;

        private const float MaxHeadTurn   = MathHelper.PiOver2;
        private const float MaxStepSize   = 0.4f;
        private const float MaxStepHeight = 1.0f;

        private const float StepSmoothSpeed = 12f;

        // ── Posición física (colisiones, lógica) ──────────────────────
        public Vector3 Position;
        public Vector3 Velocity;
        public bool    IsGrounded;

        /// <summary>
        /// True si hay input horizontal activo en este frame.
        /// Lo usa PlayerRenderer para saber si congelar la pose de zancada en el aire.
        /// </summary>
        public bool IsMoving { get; private set; }

        // ── Posición visual (cámara, renderer) ────────────────────────
        public Vector3 VisualPosition { get; private set; }

        private float _visualYDebt;

        public float BodyYaw       { get; private set; }
        public float HeadYawOffset { get; private set; }

        private readonly ChunkManager _chunkManager;
        private bool _prevJump;

        public PlayerController(Vector3 startPosition, ChunkManager chunkManager)
        {
            Position       = startPosition;
            VisualPosition = startPosition;
            _chunkManager  = chunkManager;
        }

        public void Update(GameTime gameTime, float cameraYaw)
        {
            float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.05f);

            var keys = Keyboard.GetState();

            // ── Input ─────────────────────────────────────────────────
            float fwdX = -(float)Math.Sin(cameraYaw);
            float fwdZ = -(float)Math.Cos(cameraYaw);
            float rgtX =  (float)Math.Cos(cameraYaw);
            float rgtZ = -(float)Math.Sin(cameraYaw);

            Vector3 inputDir = Vector3.Zero;
            if (keys.IsKeyDown(Keys.W)) { inputDir.X += fwdX; inputDir.Z += fwdZ; }
            if (keys.IsKeyDown(Keys.S)) { inputDir.X -= fwdX; inputDir.Z -= fwdZ; }
            if (keys.IsKeyDown(Keys.D)) { inputDir.X += rgtX; inputDir.Z += rgtZ; }
            if (keys.IsKeyDown(Keys.A)) { inputDir.X -= rgtX; inputDir.Z -= rgtZ; }

            bool  sprint  = keys.IsKeyDown(Keys.LeftShift);
            float speed   = MoveSpeed * (sprint ? SprintMultiplier : 1f);
            bool  moving  = inputDir.LengthSquared() > 0.001f;

            // ← NUEVO: exponer si hay input activo para el renderer
            IsMoving = moving;

            if (moving)
            {
                inputDir.Normalize();
                Velocity.X = inputDir.X * speed;
                Velocity.Z = inputDir.Z * speed;

                float targetYaw = (float)Math.Atan2(-inputDir.X, -inputDir.Z);
                float diff      = WrapAngle(targetYaw - BodyYaw);
                BodyYaw += diff * Math.Min(1f, (sprint ? 20f : 14f) * dt);
            }
            else
            {
                Velocity.X = MathHelper.Lerp(Velocity.X, 0f, Math.Min(1f, dt * 20f));
                Velocity.Z = MathHelper.Lerp(Velocity.Z, 0f, Math.Min(1f, dt * 20f));
                HeadYawOffset = MathHelper.Lerp(HeadYawOffset, 0f, Math.Min(1f, 3f * dt));
            }

            float rawOffset     = WrapAngle(cameraYaw - BodyYaw);
            float clampedOffset = MathHelper.Clamp(rawOffset, -MaxHeadTurn, MaxHeadTurn);
            HeadYawOffset = MathHelper.Lerp(HeadYawOffset, clampedOffset, Math.Min(1f, 20f * dt));

            // ── Salto ─────────────────────────────────────────────────
            bool jumpNow = keys.IsKeyDown(Keys.Space);
            if (jumpNow && !_prevJump && IsGrounded) Velocity.Y = JumpVelocity;
            _prevJump = jumpNow;

            // ── Gravedad ──────────────────────────────────────────────
            Velocity.Y = Math.Max(Velocity.Y + Gravity * dt, -MaxFallSpeed);

            // ── Colisión swept AABB con sub-stepping ──────────────────
            float totalDx = Velocity.X * dt;
            float totalDy = Velocity.Y * dt;
            float totalDz = Velocity.Z * dt;

            float maxMove = Math.Max(Math.Abs(totalDx),
                            Math.Max(Math.Abs(totalDy), Math.Abs(totalDz)));
            int steps = Math.Max(1, (int)Math.Ceiling(maxMove / MaxStepSize));

            float sdx = totalDx / steps;
            float sdy = totalDy / steps;
            float sdz = totalDz / steps;

            for (int i = 0; i < steps; i++)
            {
                // Eje X
                if (!CollidesWithWorld(Position + new Vector3(sdx, 0f, 0f)))
                {
                    Position.X += sdx;
                }
                else
                {
                    if (IsGrounded && Velocity.Y <= 0f &&
                        TryStepUp(ref Position, new Vector3(sdx, 0f, 0f), out float riseX))
                    {
                        _visualYDebt += riseX;
                    }
                    else
                    {
                        Velocity.X = 0f;
                        sdx = 0f;
                    }
                }

                // Eje Z
                if (!CollidesWithWorld(Position + new Vector3(0f, 0f, sdz)))
                {
                    Position.Z += sdz;
                }
                else
                {
                    if (IsGrounded && Velocity.Y <= 0f &&
                        TryStepUp(ref Position, new Vector3(0f, 0f, sdz), out float riseZ))
                    {
                        _visualYDebt += riseZ;
                    }
                    else
                    {
                        Velocity.Z = 0f;
                        sdz = 0f;
                    }
                }

                // Eje Y
                if (!CollidesWithWorld(Position + new Vector3(0f, sdy, 0f)))
                {
                    Position.Y += sdy;
                    if (sdy < 0f) IsGrounded = false;
                }
                else
                {
                    IsGrounded = sdy < 0f;
                    Velocity.Y = 0f;
                    sdy = 0f;
                }
            }

            if (IsGrounded && !CollidesWithWorld(Position + new Vector3(0f, -0.05f, 0f)))
                IsGrounded = false;

            if (Position.Y < -200f) { Position.Y = 200f; Velocity = Vector3.Zero; }

            // ── Actualizar VisualPosition ─────────────────────────────
            if (_visualYDebt > 0f)
            {
                float recover = Math.Min(_visualYDebt, StepSmoothSpeed * dt);
                _visualYDebt  = Math.Max(0f, _visualYDebt - recover);
                VisualPosition = new Vector3(
                    Position.X,
                    Position.Y - _visualYDebt,
                    Position.Z);
            }
            else
            {
                _visualYDebt   = 0f;
                VisualPosition = Position;
            }
        }

        private bool TryStepUp(ref Vector3 pos, Vector3 horizontalDelta,
                                out float riseAmount)
        {
            const float stepIncrement = 0.2f;
            riseAmount = 0f;

            for (float rise = stepIncrement; rise <= MaxStepHeight; rise += stepIncrement)
            {
                Vector3 elevated   = pos + new Vector3(0f, rise, 0f);
                Vector3 stepTarget = elevated + horizontalDelta;

                if (CollidesWithWorld(elevated))   continue;
                if (CollidesWithWorld(stepTarget)) continue;
                if (!CollidesWithWorld(stepTarget + new Vector3(0f, -stepIncrement, 0f)))
                    continue;

                riseAmount = rise;
                pos = stepTarget;
                return true;
            }

            return false;
        }

        private bool CollidesWithWorld(Vector3 feetPos)
        {
            float hw = Width * 0.5f;
            const float eps = 0.001f;

            int x0 = (int)Math.Floor(feetPos.X - hw);
            int x1 = (int)Math.Floor(feetPos.X + hw - eps);
            int y0 = (int)Math.Floor(feetPos.Y);
            int y1 = (int)Math.Floor(feetPos.Y + Height - eps);
            int z0 = (int)Math.Floor(feetPos.Z - hw);
            int z1 = (int)Math.Floor(feetPos.Z + hw - eps);

            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                byte b = _chunkManager.GetBlockAtWorldPosition(
                    new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
                if (b != BlockType.Air && b != BlockType.AirCave && b != BlockType.Water)
                    return true;
            }
            return false;
        }

        private static float WrapAngle(float a)
        {
            while (a >  MathHelper.Pi) a -= MathHelper.TwoPi;
            while (a < -MathHelper.Pi) a += MathHelper.TwoPi;
            return a;
        }
    }
}