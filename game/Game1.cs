using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace game
{
    /// <summary>
    /// Clase principal del Voxel Engine.
    /// 
    /// ARQUITECTURA:
    /// 1. Camera: Renderizado de vista FPS
    /// 2. ChunkManager: Manejo de chunks, generación, mesh building
    /// 3. WorldGenerator: Generación procedural del terreno
    /// 4. GreedyMesher: Optimización de mesh (en threads)
    /// 5. BasicEffect: Renderizado 3D simple
    /// 
    /// FLUJO CADA FRAME:
    /// 1. Update(): Actualiza cámara y chunk loading
    /// 2. ChunkManager.Update(): Encola chunks para generación
    /// 3. ThreadPool: Genera bloques + meshes en background
    /// 4. Draw(): Renderiza chunks visibles con frustum culling
    /// </summary>
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private BasicEffect _effect;
        
        // Componentes del engine
        private Camera _camera;
        private ChunkManager _chunkManager;

        // Configuración
        private const int ChunkSize = 16;
        private const int LoadDistance = 16;  // 8 chunks en cada dirección = 256 chunks

        // UI Debug
        private SpriteBatch _spriteBatch;
        private SpriteFont _debugFont;
        private bool _wireframeMode = false;
        private bool _lastTKeyPressed = false;

        // Rasterizer states
        private RasterizerState _solidMode;
        private RasterizerState _wireframeMode_State;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = false;

            // Configuración de ventana
            _graphics.PreferredBackBufferWidth = 1280;
            _graphics.PreferredBackBufferHeight = 720;
            _graphics.HardwareModeSwitch = false;  // Windowed
            _graphics.ApplyChanges();
        }

        protected override void Initialize()
        {
            // Crear cámara (empieza a altura 70, que es en el aire)
            float aspectRatio = (float)GraphicsDevice.Viewport.Width / GraphicsDevice.Viewport.Height;
            _camera = new Camera(
                startPosition: new Vector3(0, 70, 0),
                aspectRatio: aspectRatio,
                graphicsDevice: GraphicsDevice,
                fov: MathHelper.PiOver2 - 0.1f,  // FOV amplio para ver más chunks
                nearPlane: 0.1f,
                farPlane: 1000f);

            // Crear chunk manager
            _chunkManager = new ChunkManager(GraphicsDevice, ChunkSize, LoadDistance);
            // Crear estados de rasterización
            _solidMode = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.Solid };
            _wireframeMode_State = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.WireFrame };
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // Crear BasicEffect para renderizado 3D
            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = true,
                AmbientLightColor = new Vector3(0.5f, 0.5f, 0.5f),
            };
            
            _effect.AmbientLightColor = new Vector3(0.5f, 0.5f, 0.5f);
            _effect.DirectionalLight0.Enabled = true;
            _effect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(1, -1, 0.5f));
            _effect.DirectionalLight0.DiffuseColor = new Vector3(0.8f, 0.8f, 0.8f);
            // Cargar fuente para debug (crear una básica si no existe)
            try
            {
                _debugFont = Content.Load<SpriteFont>("DebugFont");
            }
            catch
            {
                // Si no hay fuente, no mostrar debug
                _debugFont = null;
            }
        }

        protected override void Update(GameTime gameTime)
        {
            var keyState = Keyboard.GetState();
            
            // Input para salir
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || 
                keyState.IsKeyDown(Keys.Escape))
                Exit();

            // Toggle wireframe con T
            if (keyState.IsKeyDown(Keys.T) && !_lastTKeyPressed)
            {
                _wireframeMode = !_wireframeMode;
            }
            _lastTKeyPressed = keyState.IsKeyDown(Keys.T);

            // Actualizar cámara (WASD + ratón)
            _camera.Update(gameTime);

            // Actualizar chunks (loading/unloading y encolar generación)
            _chunkManager.Update(_camera.Position, null);

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // Limpiar pantalla con color de cielo
            GraphicsDevice.Clear(new Color(135, 206, 235));  // Sky blue

            // ============ SETUP 3D RENDERING ============
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = _wireframeMode ? _wireframeMode_State : _solidMode;
            GraphicsDevice.BlendState = BlendState.Opaque;

            // Configurar matrices (nunca cambian en este chunk render)
            _effect.View = _camera.ViewMatrix;
            _effect.Projection = _camera.ProjectionMatrix;

            // ============ RENDERIZAR CHUNKS ============
            var frustum = _camera.GetFrustum();
            
            // Si estamos en modo wireframe, solo mostrar el chunk actual
            if (_wireframeMode)
            {
                var currentChunkPos = _chunkManager.GetChunkCoordinates(_camera.Position);
                _chunkManager.Draw(_effect, frustum, currentChunkPos, wireframeOnly: true);
            }
            else
            {
                _chunkManager.Draw(_effect, frustum);
            }

            // ============ RENDERIZAR DEBUG UI ============
            if (_debugFont != null)
            {
                _spriteBatch.Begin();

                string debugText = $"Camera Pos: {_camera.Position.X:F1}, {_camera.Position.Y:F1}, {_camera.Position.Z:F1}\n" +
                                   $"Chunks Loaded: {_chunkManager.LoadedChunkCount}\n" +
                                   $"Generation Queue: {_chunkManager.GenerationQueueCount}\n" +
                                   $"FPS: {(1f / gameTime.ElapsedGameTime.TotalSeconds):F0}";

                // Agregar información del chunk actual si estamos en wireframe mode
                if (_wireframeMode)
                {
                    var currentChunkPos = _chunkManager.GetChunkCoordinates(_camera.Position);
                    var currentChunk = _chunkManager.GetChunk(currentChunkPos);
                    
                    if (currentChunk != null && currentChunk.DebugInfo != null)
                    {
                        debugText += $"\n\nCHUNK ACTUAL:\n" +
                                    $"Position: ({currentChunk.X}, {currentChunk.Y}, {currentChunk.Z})\n" +
                                    $"Gen Time: {currentChunk.DebugInfo.MeshGenerationTimeMs}ms\n" +
                                    $"Mesh Time: {currentChunk.DebugInfo.GreedyMeshingTimeMs}ms\n" +
                                    $"Vertices: {currentChunk.DebugInfo.VertexCount}\n" +
                                    $"Triangles: {currentChunk.DebugInfo.TriangleCount}";
                    }
                }

                _spriteBatch.DrawString(_debugFont, debugText, new Vector2(10, 10), Color.White);

                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _chunkManager?.Dispose();
                _effect?.Dispose();
                _spriteBatch?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
