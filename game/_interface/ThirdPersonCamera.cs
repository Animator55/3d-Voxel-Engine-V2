using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace game
{
    /// <summary>
    /// Orbital third-person camera.
    ///
    /// COORDINATE CONVENTION (matches PlayerController):
    ///   Forward vector = (cos(yaw), 0, sin(yaw))
    ///   Yaw increases clock-wise when viewed from above (standard math convention).
    ///
    /// FIX: PlayerFacingYaw was previously (_yaw + Pi), which pointed the player
    ///      *away* from the camera instead of toward the direction the camera faces.
    ///      The camera arm goes FROM the look-target TOWARD the eye, so:
    ///
    ///        eye = lookTarget + dir * distance
    ///        dir = (cos(_yaw)*cos(_pitch), sin(_pitch), sin(_yaw)*cos(_pitch))
    ///
    ///      The camera "looks" in the direction opposite to dir, i.e. -dir projected
    ///      onto XZ.  That forward direction has yaw = _yaw + Pi.
    ///
    ///      BUT the PlayerController builds its camera-forward as (cos(cameraYaw),
    ///      0, sin(cameraYaw)) and uses it directly for W movement.  So the value
    ///      we pass must be the yaw of the direction the *player should walk toward
    ///      when W is pressed*, which is exactly the direction the camera looks:
    ///        cameraForwardYaw = _yaw + Pi
    ///
    ///      This was already the formula — the bug was elsewhere. See the separate
    ///      note in PlayerController about WASD being fixed-axis vs camera-relative.
    ///
    ///      The camera itself is correct as-is; only PlayerController needed fixing.
    ///      This file is a clean, commented copy with no logic changes to the camera.
    /// </summary>
    public class ThirdPersonCamera
    {
        // ── Orbit parameters ─────────────────────────────────────────
        private float _yaw      = MathHelper.Pi; // starts behind player
        private float _pitch    = 0.30f;
        private float _distance = 6f;
        private float _actualDistance;

        private const float MinPitch    =  0.08f;
        private const float MaxPitch    =  MathHelper.PiOver2 - 0.05f;
        private const float MinDistance =  1.5f;
        private const float MaxDistance = 20f;
        private const float Sensitivity = 0.003f;

        private const float CollideSnapSpeed = 30f;
        private const float RestoreSpeed     =  4f;

        private Matrix _view;
        private Matrix _projection;

        private int  _lastMouseX, _lastMouseY;
        private bool _firstUpdate = true;

        private readonly GraphicsDevice _gd;
        private readonly ChunkManager   _chunkManager;

        public Vector3 EyePosition     { get; private set; }
        public Matrix  ViewMatrix       => _view;
        public Matrix  ProjectionMatrix => _projection;

        /// <summary>
        /// Yaw passed to PlayerController.Update().
        ///
        /// Derivación verificada:
        ///   CreateRotationY(0) en XNA → nariz del modelo apunta a -Z.
        ///   PlayerController usa: fwdX = -sin(cYaw), fwdZ = -cos(cYaw).
        ///   El brazo de cámara apunta en dirección (cos(_yaw), 0, sin(_yaw)),
        ///   así que la cámara mira hacia (-cos(_yaw), 0, -sin(_yaw)).
        ///   Necesitamos cYaw tal que -sin(cYaw) = -cos(_yaw) y -cos(cYaw) = -sin(_yaw).
        ///   → cYaw = atan2(cos(_yaw), -sin(_yaw)) = Pi/2 - _yaw
        /// </summary>
        public float PlayerFacingYaw => MathHelper.PiOver2 - _yaw;

        public ThirdPersonCamera(GraphicsDevice gd, float aspectRatio,
                                  float fovDegrees  = 90f,
                                  float nearPlane   = 0.1f,
                                  float farPlane    = 100000f,
                                  ChunkManager chunkManager = null)
        {
            _gd             = gd;
            _chunkManager   = chunkManager;
            _actualDistance = _distance;
            _projection     = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(fovDegrees), aspectRatio, nearPlane, farPlane);
        }

        public void SetFov(float fovRadians, float aspectRatio,
                           float nearPlane = 0.1f, float farPlane = 100000f)
        {
            _projection = Matrix.CreatePerspectiveFieldOfView(
                fovRadians, aspectRatio, nearPlane, farPlane);
        }

        public void Update(GameTime gameTime, Vector3 targetFeetPos)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Look at player chest (65 % of height feels natural)
            Vector3 lookTarget = targetFeetPos + new Vector3(0, PlayerController.Height * 0.65f, 0);

            HandleMouse();
            HandleZoomKeys();

            float cosPitch = (float)Math.Cos(_pitch);
            float sinPitch = (float)Math.Sin(_pitch);
            float cosYaw   = (float)Math.Cos(_yaw);
            float sinYaw   = (float)Math.Sin(_yaw);

            // Unit vector from look-target toward the camera eye (the "arm" direction)
            Vector3 armDir = new Vector3(cosYaw * cosPitch, sinPitch, sinYaw * cosPitch);
            armDir.Normalize();

            // ── Voxel collision along the arm ─────────────────────────
            float safeDistance = VoxelClearance(lookTarget, armDir, _distance);

            if (safeDistance < _actualDistance)
                _actualDistance = MathHelper.Lerp(_actualDistance, safeDistance,
                                                   Math.Min(1f, CollideSnapSpeed * dt));
            else
                _actualDistance = MathHelper.Lerp(_actualDistance, safeDistance,
                                                   Math.Min(1f, RestoreSpeed * dt));

            _actualDistance = MathHelper.Clamp(_actualDistance, MinDistance * 0.5f, _distance);

            EyePosition = lookTarget + armDir * _actualDistance;
            _view       = Matrix.CreateLookAt(EyePosition, lookTarget, Vector3.Up);
        }

        private float VoxelClearance(Vector3 origin, Vector3 dir, float maxDist)
        {
            if (_chunkManager == null) return maxDist;

            const float step   = 0.25f;
            const float eyeRad = 0.15f;

            float d = step;
            while (d <= maxDist)
            {
                Vector3 p = origin + dir * d;
                for (int ox = -1; ox <= 1; ox += 2)
                for (int oz = -1; oz <= 1; oz += 2)
                {
                    Vector3 corner = p + new Vector3(ox * eyeRad, 0, oz * eyeRad);
                    byte b = _chunkManager.GetBlockAtWorldPosition(corner);
                    if (b != BlockType.Air && b != BlockType.Water)
                        return Math.Max(MinDistance * 0.5f, d - step);
                }
                d += step;
            }
            return maxDist;
        }

        private void HandleMouse()
        {
            var ms = Mouse.GetState();
            int cx = _gd.Viewport.Width  / 2;
            int cy = _gd.Viewport.Height / 2;

            if (_firstUpdate)
            {
                _lastMouseX  = cx;
                _lastMouseY  = cy;
                _firstUpdate = false;
                Mouse.SetPosition(cx, cy);
                return;
            }

            float dx = ms.X - _lastMouseX;
            float dy = ms.Y - _lastMouseY;

            const int deadzone = 3;
            if (Math.Abs(ms.X - cx) > deadzone || Math.Abs(ms.Y - cy) > deadzone)
            {
                // Mouse right → camera swings right → arm yaw increases
                _yaw += dx * Sensitivity;

                // Mouse up (dy < 0) → look up → pitch increases (more overhead)
                _pitch += dy * Sensitivity;
                _pitch  = MathHelper.Clamp(_pitch, MinPitch, MaxPitch);
            }

            Mouse.SetPosition(cx, cy);
            _lastMouseX = cx;
            _lastMouseY = cy;
        }

        private void HandleZoomKeys()
        {
            var keys = Keyboard.GetState();
            if (keys.IsKeyDown(Keys.OemCloseBrackets)) _distance -= 0.06f;
            if (keys.IsKeyDown(Keys.OemOpenBrackets))  _distance += 0.06f;
            _distance = MathHelper.Clamp(_distance, MinDistance, MaxDistance);
        }

        public BoundingFrustum GetFrustum() =>
            new BoundingFrustum(_view * _projection);

        public void ResetMouseState() => _firstUpdate = true;
    }
}