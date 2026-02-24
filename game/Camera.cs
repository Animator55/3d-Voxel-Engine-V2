using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace game
{
    /// <summary>
    /// Cámara tipo Free-Look FPS.
    /// 
    /// CARACTERÍSTICAS:
    /// - Movimiento: WASD (respeta delta time)
    /// - Rotación: Ratón (pitch clamped para evitar flip)
    /// - View Matrix correcta
    /// - Sensibilidad configurable del ratón
    /// - Velocidad de movimiento configurable
    /// </summary>
    public class Camera
    {
        // Posición y orientación
        private Vector3 _position;
        private float _yaw;      // Rotación en Y (izquierda/derecha)
        private float _pitch;    // Rotación en X (arriba/abajo)

        // Configuración de movimiento
        private float _moveSpeed = 50f;      // Unidades por segundo
        private float _mouseSensitivity = 0.002f;  // Radianes por píxel

        // Límites
        private const float MaxPitch = MathHelper.PiOver2 - 0.1f;  // ~89 grados
        private const float MinPitch = -MathHelper.PiOver2 + 0.1f;  // ~-89 grados

        // Última posición del ratón (para delta)
        private int _lastMouseX;
        private int _lastMouseY;
        private bool _firstUpdate = true;
        private GraphicsDevice _graphicsDevice;

        // Matriz de cámara (actualizada cada frame)
        private Matrix _viewMatrix;
        private Matrix _projectionMatrix;

        public Camera(Vector3 startPosition, float aspectRatio, GraphicsDevice graphicsDevice, float fov = MathHelper.PiOver4,
            float nearPlane = 0.1f, float farPlane = 1000f)
        {
            _position = startPosition;
            _yaw = 0f;
            _pitch = 0f;
            _graphicsDevice = graphicsDevice;

            // Crear matriz de proyección (perpspectiva)
            _projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                fov,
                aspectRatio,
                nearPlane,
                farPlane);

            _viewMatrix = Matrix.Identity;
        }

        /// <summary>
        /// Actualiza la cámara según input y tiempo.
        /// Debe llamarse cada frame.
        /// </summary>
        public void Update(GameTime gameTime)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Obtener input de teclado
            UpdateMovement(deltaTime);

            // Obtener input de ratón
            UpdateRotation();

            // Actualizar View Matrix
            UpdateViewMatrix();
        }

        /// <summary>
        /// Maneja el movimiento WASD.
        /// Respeta delta time para movimiento frame-rate independiente.
        /// </summary>
        private void UpdateMovement(float deltaTime)
        {
            var keyState = Keyboard.GetState();
            Vector3 moveDirection = Vector3.Zero;

            // WASD movement
            if (keyState.IsKeyDown(Keys.LeftShift))
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
                moveDirection += GetRightVector();

            // Normalizar a velocidad consistente
            if (moveDirection != Vector3.Zero)
            {
                moveDirection.Normalize();
                _position += moveDirection * _moveSpeed * deltaTime;
            }
        }

        /// <summary>
        /// Maneja la rotación con el ratón.
        /// Yaw (izquierda-derecha) es ilimitado.
        /// Pitch (arriba-abajo) está limitado para evitar gimbal lock.
        /// </summary>
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
                return;  // No procesar input en el primer frame
            }

            // Delta desde último frame (ANTES de recentrar)
            float deltaX = mouseState.X - _lastMouseX;
            float deltaY = mouseState.Y - _lastMouseY;

            // Deadzone para evitar que el recentrado afecte el siguiente frame
            const int deadzone = 5;
            if (Math.Abs(mouseState.X - (int)windowCenter.X) > deadzone || 
                Math.Abs(mouseState.Y - (int)windowCenter.Y) > deadzone)
            {
                // Solo aplicar delta si el mouse NO está en el centro (zona muerta del recentrado)
                _yaw += deltaX * _mouseSensitivity;
                _pitch -= deltaY * _mouseSensitivity;
            }

            // Actualizar última posición
            _lastMouseX = mouseState.X;
            _lastMouseY = mouseState.Y;

            // Clamp pitch para evitar flip
            _pitch = MathHelper.Clamp(_pitch, MinPitch, MaxPitch);

            // Keep yaw en rango [0, 2π] para evitar overflow de float
            while (_yaw > MathHelper.TwoPi)
                _yaw -= MathHelper.TwoPi;
            while (_yaw < -MathHelper.TwoPi)
                _yaw += MathHelper.TwoPi;

            // Recentrar el mouse (DESPUÉS de calcular delta)
            Mouse.SetPosition((int)windowCenter.X, (int)windowCenter.Y);
            
            // Actualizar última posición (ya que recentramos)
            _lastMouseX = (int)windowCenter.X;
            _lastMouseY = (int)windowCenter.Y;
        }

        /// <summary>
        /// Actualiza la matriz View según la posición y orientación actual.
        /// Uses Yaw-Pitch para calcular vectores Forward/Up/Right.
        /// </summary>
        private void UpdateViewMatrix()
        {
            // Calcular vectores de cámara desde yaw/pitch
            Vector3 forward = GetForwardVector();
            Vector3 right = GetRightVector();
            Vector3 up = Vector3.Up;

            // La view matrix es la inversa de la matriz que representa la cámara
            // LookAt crea automáticamente la matriz correcta
            _viewMatrix = Matrix.CreateLookAt(
                _position,
                _position + forward,
                up);
        }

        /// <summary>
        /// Calcula el vector Forward en base a yaw/pitch.
        /// Forward = (cos(yaw)cos(pitch), sin(pitch), sin(yaw)cos(pitch))
        /// </summary>
        private Vector3 GetForwardVector()
        {
            float cosYaw = (float)System.Math.Cos(_yaw);
            float sinYaw = (float)System.Math.Sin(_yaw);
            float cosPitch = (float)System.Math.Cos(_pitch);
            float sinPitch = (float)System.Math.Sin(_pitch);

            return new Vector3(
                cosYaw * cosPitch,
                sinPitch,
                sinYaw * cosPitch);  // Asegurar vector unitario
        }

        /// <summary>
        /// Calcula el vector Up (perpendicular a Forward y Right).
        /// Up = normalize(Right × Forward)
        /// </summary>
        private Vector3 GetUpVector()
        {
            Vector3 forward = GetForwardVector();
            Vector3 right = GetRightVector();
            return Vector3.Cross(right, forward);
        }

        /// <summary>
        /// Calcula el vector Right (perpendicular a Forward, en el plano horizontal).
        /// Right = normalize(Forward × Up)
        /// </summary>
        private Vector3 GetRightVector()
        {
            Vector3 forward = GetForwardVector();
            Vector3 up = Vector3.Up;
            return Vector3.Cross(forward, up);
        }

        // ============ Propiedades Públicas ============

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

        /// <summary>
        /// Calcula frustum de la cámara para frustum culling.
        /// </summary>
        public BoundingFrustum GetFrustum()
        {
            return new BoundingFrustum(_viewMatrix * _projectionMatrix);
        }
    }
}
