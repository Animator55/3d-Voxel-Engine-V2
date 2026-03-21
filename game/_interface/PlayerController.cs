using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace game
{
    public class PlayerController
    {
        private const float Gravity          = -28f;
        private const float JumpVelocity     = 10f;
        private const float MoveSpeed        = 6f;
        private const float SprintMultiplier = 1.8f;
        private const float MaxFallSpeed     = 50f;

        public const float Width  = 0.6f;
        public const float Height = 1.8f;

        private const float MaxHeadTurn = MathHelper.PiOver2;

        public Vector3 Position;
        public Vector3 Velocity;
        public bool    IsGrounded;

        public float BodyYaw      { get; private set; }
        public float HeadYawOffset { get; private set; }

        private readonly ChunkManager _chunkManager;
        private bool _prevJump;

        public PlayerController(Vector3 startPosition, ChunkManager chunkManager)
        {
            Position      = startPosition;
            _chunkManager = chunkManager;
        }

        public void Update(GameTime gameTime, float cameraYaw)
        {
            float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.05f);

            var keys = Keyboard.GetState();

            // ── WASD relativo a la cámara ─────────────────────────────
            //
            // Convención del renderer (PlayerRenderer/CreateRotationY):
            //   yaw = 0  →  nariz del modelo apunta a  -Z
            //   yaw = Pi/2 →  nariz apunta a  +X
            //
            // Entonces "adelante del renderer" = -Z, no +X.
            // Para que el jugador camine hacia donde la cámara mira usamos:
            //   forward = (-sin(cameraYaw), 0, -cos(cameraYaw))
            //   right   = ( cos(cameraYaw), 0, -sin(cameraYaw))
            //
            // Esto también corrige A/D que estaban invertidas.

            float fwdX = -(float)Math.Sin(cameraYaw);
            float fwdZ = -(float)Math.Cos(cameraYaw);
            float rgtX =  (float)Math.Cos(cameraYaw);
            float rgtZ = -(float)Math.Sin(cameraYaw);

            Vector3 inputDir = Vector3.Zero;
            if (keys.IsKeyDown(Keys.W)) { inputDir.X += fwdX; inputDir.Z += fwdZ; }
            if (keys.IsKeyDown(Keys.S)) { inputDir.X -= fwdX; inputDir.Z -= fwdZ; }
            if (keys.IsKeyDown(Keys.D)) { inputDir.X += rgtX; inputDir.Z += rgtZ; }
            if (keys.IsKeyDown(Keys.A)) { inputDir.X -= rgtX; inputDir.Z -= rgtZ; }

            bool  sprint = keys.IsKeyDown(Keys.LeftShift);
            float speed  = MoveSpeed * (sprint ? SprintMultiplier : 1f);
            bool  moving = inputDir.LengthSquared() > 0.001f;

            if (moving)
            {
                inputDir.Normalize();
                Velocity.X = inputDir.X * speed;
                Velocity.Z = inputDir.Z * speed;

                // CreateRotationY(yaw) → nariz = (-sin(yaw), 0, -cos(yaw))
                // Para que nariz coincida con la dirección de movimiento (mX, mZ):
                //   -sin(yaw) = mX  y  -cos(yaw) = mZ
                //   → yaw = atan2(-mX, -mZ)
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

            // ── Cabeza sigue la cámara ────────────────────────────────
            // cameraYaw (PlayerFacingYaw) está en el mismo espacio que BodyYaw
            // porque ambos usan la convención atan2(X, -Z).
            // El WrapAngle da el offset correcto directamente.
            float rawOffset     = WrapAngle(cameraYaw - BodyYaw);
            float clampedOffset = MathHelper.Clamp(rawOffset, -MaxHeadTurn, MaxHeadTurn);
            HeadYawOffset = MathHelper.Lerp(HeadYawOffset, clampedOffset, Math.Min(1f, 20f * dt));

            // ── Salto ─────────────────────────────────────────────────
            bool jumpNow = keys.IsKeyDown(Keys.Space);
            if (jumpNow && !_prevJump && IsGrounded) Velocity.Y = JumpVelocity;
            _prevJump = jumpNow;

            // ── Gravedad ──────────────────────────────────────────────
            Velocity.Y = Math.Max(Velocity.Y + Gravity * dt, -MaxFallSpeed);

            // ── Colisión swept AABB ───────────────────────────────────
            float dx = Velocity.X * dt;
            float dy = Velocity.Y * dt;
            float dz = Velocity.Z * dt;

            if (!CollidesWithWorld(Position + new Vector3(dx, 0f, 0f)))
                Position.X += dx;
            else
                Velocity.X = 0f;

            if (!CollidesWithWorld(Position + new Vector3(0f, 0f, dz)))
                Position.Z += dz;
            else
                Velocity.Z = 0f;

            if (!CollidesWithWorld(Position + new Vector3(0f, dy, 0f)))
            {
                Position.Y += dy;
                if (dy < 0f) IsGrounded = false;
            }
            else
            {
                IsGrounded = dy < 0f;
                Velocity.Y = 0f;
            }

            if (IsGrounded && !CollidesWithWorld(Position + new Vector3(0f, -0.05f, 0f)))
                IsGrounded = false;

            if (Position.Y < -200f) { Position.Y = 200f; Velocity = Vector3.Zero; }
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
                if (b != BlockType.Air && b != BlockType.Water)
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