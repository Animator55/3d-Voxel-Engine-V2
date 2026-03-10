using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
namespace game
{
    public class Camera
    {
        private Vector3 _position;
        private float _yaw;
        private float _pitch; private float _moveSpeed = 30f;
        private float _mouseSensitivity = 0.002f; private const float MaxPitch = MathHelper.PiOver2 - 0.1f;
        private const float MinPitch = -MathHelper.PiOver2 + 0.1f; private int _lastMouseX;
        private int _lastMouseY;
        private bool _firstUpdate = true;
        private GraphicsDevice _graphicsDevice; private Matrix _viewMatrix;
        private Matrix _projectionMatrix;
        public Camera(Vector3 startPosition, float aspectRatio, GraphicsDevice graphicsDevice, float fov = MathHelper.PiOver4,
            float nearPlane = 0.1f, float farPlane = 10000f)
        {
            _position = startPosition;
            _yaw = 0f;
            _pitch = 0f;
            _graphicsDevice = graphicsDevice; _projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                fov,
                aspectRatio,
                nearPlane,
                farPlane);
            _viewMatrix = Matrix.Identity;
        }
        public void Update(GameTime gameTime)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds; UpdateMovement(deltaTime); UpdateRotation(); UpdateViewMatrix();
        }
        private void UpdateMovement(float deltaTime)
        {
            var keyState = Keyboard.GetState();
            Vector3 moveDirection = Vector3.Zero; if (keyState.IsKeyDown(Keys.LeftShift))
                moveDirection -= GetUpVector();
            if (keyState.IsKeyDown(Keys.Space))
                moveDirection += GetUpVector();
            if (keyState.IsKeyDown(Keys.W))
                moveDirection += GetForwardVector();
            if (keyState.IsKeyDown(Keys.S))
                moveDirection -= GetForwardVector();
            if (keyState.IsKeyDown(Keys.A))
                moveDirection -= GetRightVector();
            if (keyState.IsKeyDown(Keys.D))
                moveDirection += GetRightVector(); if (moveDirection != Vector3.Zero)
            {
                moveDirection.Normalize();
                _position += moveDirection * _moveSpeed * deltaTime;
            }
        }
        public void SetFov(float fovRadians, float aspectRatio,
                           float nearPlane = 0.1f, float farPlane = 100000f)
        {
            _projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                fovRadians,
                aspectRatio,
                nearPlane,
                farPlane);
        }
        private void UpdateRotation()
        {
            var mouseState = Mouse.GetState();
            var windowCenter = new Vector2(
                _graphicsDevice.Viewport.Width / 2f,
                _graphicsDevice.Viewport.Height / 2f);
            if (_firstUpdate)
            {
                _lastMouseX = (int)windowCenter.X;
                _lastMouseY = (int)windowCenter.Y;
                _firstUpdate = false;
                Mouse.SetPosition((int)windowCenter.X, (int)windowCenter.Y);
                return;
            }
            float deltaX = mouseState.X - _lastMouseX;
            float deltaY = mouseState.Y - _lastMouseY; const int deadzone = 5;
            if (Math.Abs(mouseState.X - (int)windowCenter.X) > deadzone ||
                Math.Abs(mouseState.Y - (int)windowCenter.Y) > deadzone)
            {
                _yaw += deltaX * _mouseSensitivity;
                _pitch -= deltaY * _mouseSensitivity;
            }
            _lastMouseX = mouseState.X;
            _lastMouseY = mouseState.Y; _pitch = MathHelper.Clamp(_pitch, MinPitch, MaxPitch); while (_yaw > MathHelper.TwoPi)
                _yaw -= MathHelper.TwoPi;
            while (_yaw < -MathHelper.TwoPi)
                _yaw += MathHelper.TwoPi; Mouse.SetPosition((int)windowCenter.X, (int)windowCenter.Y); _lastMouseX = (int)windowCenter.X;
            _lastMouseY = (int)windowCenter.Y;
        }
        private void UpdateViewMatrix()
        {
            Vector3 forward = GetForwardVector();
            Vector3 right = GetRightVector();
            Vector3 up = Vector3.Up;
            _viewMatrix = Matrix.CreateLookAt(
                _position,
                _position + forward,
                up);
        }
        private Vector3 GetForwardVector()
        {
            float cosYaw = (float)System.Math.Cos(_yaw);
            float sinYaw = (float)System.Math.Sin(_yaw);
            float cosPitch = (float)System.Math.Cos(_pitch);
            float sinPitch = (float)System.Math.Sin(_pitch);
            return new Vector3(
                cosYaw * cosPitch,
                sinPitch,
                sinYaw * cosPitch);
        }
        private Vector3 GetUpVector()
        {
            Vector3 forward = GetForwardVector();
            Vector3 right = GetRightVector();
            return Vector3.Cross(right, forward);
        }
        private Vector3 GetRightVector()
        {
            Vector3 forward = GetForwardVector();
            Vector3 up = Vector3.Up;
            return Vector3.Cross(forward, up);
        }
        public Vector3 Position
        {
            get => _position;
            set => _position = value;
        }
        public Matrix ViewMatrix => _viewMatrix;
        public Matrix ProjectionMatrix => _projectionMatrix;
        public float MoveSpeed
        {
            get => _moveSpeed;
            set => _moveSpeed = value;
        }
        public float MouseSensitivity
        {
            get => _mouseSensitivity;
            set => _mouseSensitivity = value;
        }
        public float Yaw
        {
            get => _yaw;
            set => _yaw = value;
        }
        public float Pitch
        {
            get => _pitch;
            set => _pitch = MathHelper.Clamp(value, MinPitch, MaxPitch);
        }
        public BoundingFrustum GetFrustum()
        {
            return new BoundingFrustum(_viewMatrix * _projectionMatrix);
        }
    }
}
