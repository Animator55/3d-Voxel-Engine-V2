using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace game
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;

        private VoxelLitEffect _voxelEffect;
        private Effect _voxelFx;

        // ── Water shader ──────────────────────────────────────────────
        private WaterEffect _waterEffect;
        private Effect _waterFx;
        private float _waterTime;

        private Camera _camera;
        private ChunkManager _chunkManager;
        private int _loadDistance = 1;

        // ── Third-person mode ─────────────────────────────────────────
        private bool _thirdPerson = false;
        private bool _lastF5 = false;
        private ThirdPersonCamera _tpCamera;
        private PlayerController _player;
        private PlayerRenderer _playerRenderer;

        private ProceduralSkybox _skybox;
        private Effect _skyEffect;

        private PauseMenu _pauseMenu;
        private bool _lastEscape = false;
        private ParticleSystem _particleSystem;
        private EntityManager _entityManager;


        private SpriteBatch _spriteBatch;
        private SpriteFont _debugFont;
        private Texture2D _pixel;

        private bool _showDebug = false;
        private bool _wireframeMode = false;
        private bool _fancyWater = true;
        private bool _lastF3 = false;
        private bool _lastT = false;

        private bool _lastLMB = false;
        private bool _lastRMB = false;

        private RasterizerState _solidState;
        private RasterizerState _wireframeState;
        private readonly WorldGenerator _worldGenerator;

        // ── FPS ───────────────────────────────────────────────────────
        private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
        private const int FPS_WINDOW = 60;
        private readonly Queue<double> _frameTimes = new Queue<double>(FPS_WINDOW);
        private float _smoothFps;
        private float _frameMs;

        // ── HUD colours ───────────────────────────────────────────────
        private static readonly Color CLabel = new Color(180, 180, 180);
        private static readonly Color CValue = Color.White;
        private static readonly Color CHeader = new Color(100, 220, 255);
        private static readonly Color CWarn = new Color(255, 200, 80);
        private static readonly Color CGood = new Color(100, 255, 140);
        private static readonly Color CBad = new Color(255, 80, 80);

        private const int PadX = 6;
        private const int PadY = 6;
        private const int LineH = 14;
        private const int ColW = 230;

        private readonly List<(Vector3 worldPos, byte blockType)> _emissiveBlocks
            = new List<(Vector3, byte)>();

        private bool _cameraLightEnabled = true;
        private float _cameraLightRadius = 18f;
        private float _cameraLightIntensity = 1.4f;

        // ─────────────────────────────────────────────────────────────
        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = false;
            _graphics.PreferredBackBufferWidth = 1280;
            _graphics.PreferredBackBufferHeight = 720;
            _graphics.HardwareModeSwitch = false;
            IsFixedTimeStep = false;
            _graphics.ApplyChanges();
            _worldGenerator = new WorldGenerator(seed: 412);
        }

        protected override void Initialize()
        {
            Window.AllowUserResizing = true;
            float aspect = (float)GraphicsDevice.Viewport.Width /
                                   GraphicsDevice.Viewport.Height;

            _camera = new Camera(
                startPosition: new Vector3(0, 60, 0),
                aspectRatio: aspect,
                graphicsDevice: GraphicsDevice,
                fov: MathHelper.ToRadians(90f),
                nearPlane: 0.1f,
                farPlane: 100000f);

            _chunkManager = new ChunkManager(GraphicsDevice,
                                             chunkSize: 32,
                                             loadDistance: _loadDistance);

            _tpCamera = new ThirdPersonCamera(GraphicsDevice, aspect,
                fovDegrees: 90f,
                nearPlane: 0.1f,
                farPlane: 100000f,
                chunkManager: _chunkManager);

            _player = new PlayerController(
                startPosition: new Vector3(0, 62, 0),
                chunkManager: _chunkManager);

            _solidState = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.Solid };
            _wireframeState = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.WireFrame };

            EntityDefinitions.RegisterAll();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _voxelFx = Content.Load<Effect>("VoxelLit");
            _voxelEffect = new VoxelLitEffect(_voxelFx);
            _voxelEffect.FogEnabled = true;
            _voxelEffect.FogStart = _loadDistance * 2 * 24f;
            _voxelEffect.FogEnd = _loadDistance * 2 * 36f;
            _voxelEffect.CameraLightEnabled = _cameraLightEnabled;
            _voxelEffect.CameraLightRadius = _cameraLightRadius;
            _voxelEffect.CameraLightIntensity = _cameraLightIntensity;
            _voxelEffect.CameraLightColor = new Vector3(1f, 0.92f, 0.75f);

            _waterFx = Content.Load<Effect>("Water");
            _waterEffect = new WaterEffect(_waterFx);
            _waterEffect.WaterAlpha = 0.3f;
            _waterEffect.FogEnabled = true;
            _waterEffect.FogStart = _loadDistance * 2 * 24f;
            _waterEffect.FogEnd = _loadDistance * 2 * 36f;
            _waterEffect.CameraLightEnabled = _cameraLightEnabled;
            _waterEffect.CameraLightRadius = _cameraLightRadius;
            _waterEffect.CameraLightIntensity = _cameraLightIntensity;
            _waterEffect.CameraLightColor = new Vector3(1f, 0.92f, 0.75f);

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            try { _debugFont = Content.Load<SpriteFont>("DebugFont"); }
            catch { _debugFont = null; }

            _skyEffect = Content.Load<Effect>("Sky");
            _skybox = new ProceduralSkybox(GraphicsDevice, _skyEffect, startHour: 8f);

            _emissiveBlocks.Add((new Vector3(20, 22, 0), BlockType.Glowstone));
            _emissiveBlocks.Add((new Vector3(-32, 68, 16), BlockType.Glowstone));

            var initialSettings = new GameSettings
            {
                LoadDistance = _loadDistance,
                EnableVeryLowPoly = true,
                FogEnabled = true,
                FovDegrees = 90f,
                WireframeMode = false,
                DirectionalLight = true,
                AmbientLight = 0.5f,
                MoveSpeed = _camera.MoveSpeed,
                MouseSensitivity = _camera.MouseSensitivity,
                ShowDebugHud = false,
                CameraLightEnabled = _cameraLightEnabled,
                CameraLightRadius = _cameraLightRadius,
                CameraLightIntensity = _cameraLightIntensity,
            };

            _pauseMenu = new PauseMenu(GraphicsDevice, _pixel, _debugFont, initialSettings);
            _pauseMenu.OnResume += () => { IsMouseVisible = false; };
            _pauseMenu.OnExit += () => Exit();
            _pauseMenu.OnSettingsChanged += ApplySettings;

            _playerRenderer = new PlayerRenderer(GraphicsDevice);

            _particleSystem = new ParticleSystem(GraphicsDevice, maxParticles: 4096);

            _entityManager = new EntityManager(GraphicsDevice, _particleSystem);

            // Opcional: suscribir loot
            _entityManager.OnLootDropped += (pos, drops) =>
            {
                foreach (var drop in drops)
                    Debug.WriteLine($"Loot: itemId={drop.ItemId} x{drop.MinCount} at {pos}");
            };

            // Spawn de ejemplo
            _entityManager.Spawn(EntityIds.Pig, new Vector3(5, 65, 5));
            _entityManager.Spawn(EntityIds.Sheep, new Vector3(10, 65, 8));
            // _entityManager.Spawn(EntityIds.Fern, new Vector3(3, 64, 3));
            // _entityManager.Spawn(EntityIds.Mushroom, new Vector3(-4, 64, 6));
            // _entityManager.Spawn(EntityIds.Zombie, new Vector3(15, 65, 0));
        }

        private void ApplySettings(GameSettings s)
        {
            _camera.MoveSpeed = s.MoveSpeed;
            _camera.MouseSensitivity = s.MouseSensitivity;

            float aspect = (float)GraphicsDevice.Viewport.Width /
                                   GraphicsDevice.Viewport.Height;
            _camera.SetFov(MathHelper.ToRadians(s.FovDegrees), aspect);
            _tpCamera?.SetFov(MathHelper.ToRadians(s.FovDegrees), aspect);

            _voxelEffect.FogEnabled = s.FogEnabled;
            _waterEffect.FogEnabled = s.FogEnabled;
            _waterEffect.CameraLightEnabled = _cameraLightEnabled;
            _waterEffect.WaterAlpha = 0.3f;
            _waterEffect.CameraLightRadius = _cameraLightRadius;
            _waterEffect.CameraLightIntensity = _cameraLightIntensity;
            _waterEffect.CameraLightColor = new Vector3(1f, 0.92f, 0.75f);
            _wireframeMode = s.WireframeMode;
            _fancyWater = s.FancyWater;
            _showDebug = s.ShowDebugHud;

            if (!s.DirectionalLight)
                _voxelEffect.DirectionalLightEnabled = false;

            float al = s.AmbientLight;
            _voxelEffect.AmbientLightColor = new Vector3(al, al, al);

            _cameraLightEnabled = s.CameraLightEnabled;
            _cameraLightRadius = s.CameraLightRadius;
            _cameraLightIntensity = s.CameraLightIntensity;
            _voxelEffect.CameraLightEnabled = _cameraLightEnabled;
            _voxelEffect.CameraLightRadius = _cameraLightRadius;
            _voxelEffect.CameraLightIntensity = _cameraLightIntensity;

            if (s.LoadDistance != _loadDistance
             || s.AoStrength != GreedyMesher.AoStrength
             || s.FancyWater != _chunkManager.FancyWater)
            {
                _loadDistance = s.LoadDistance;
                _chunkManager.Dispose();
                _chunkManager = new ChunkManager(GraphicsDevice,
                                                 chunkSize: 32,
                                                 loadDistance: _loadDistance);
                _player = new PlayerController(_player.Position, _chunkManager);
                _tpCamera = new ThirdPersonCamera(GraphicsDevice, aspect,
                    fovDegrees: s.FovDegrees,
                    nearPlane: 0.1f,
                    farPlane: 100000f,
                    chunkManager: _chunkManager);
            }

            float fs = _loadDistance * 2 * 24f;
            float fe = _loadDistance * 2 * 36f;
            _voxelEffect.FogStart = fs; _voxelEffect.FogEnd = fe;
            _waterEffect.FogStart = fs; _waterEffect.FogEnd = fe;

            _chunkManager.EnableVeryLowPoly = s.EnableVeryLowPoly;
            _chunkManager.FancyWater = s.FancyWater;
            _fancyWater = s.FancyWater;
            GreedyMesher.AoStrength = s.AoStrength;
        }

        protected override void Update(GameTime gameTime)
        {
            var keys = Keyboard.GetState();
            bool escNow = keys.IsKeyDown(Keys.Escape);

            if (escNow && !_lastEscape)
            {
                if (_pauseMenu.IsOpen) { _pauseMenu.Close(); IsMouseVisible = false; }
                else { _pauseMenu.Open(); IsMouseVisible = true; }
            }
            _lastEscape = escNow;

            Vector3 worldPos = _thirdPerson ? _player.VisualPosition
                                           : _camera.Position;
            _chunkManager.Update(worldPos, _worldGenerator, null);

            if (_pauseMenu.IsOpen)
            {
                IsMouseVisible = true;
                _pauseMenu.Update(gameTime, keys, Mouse.GetState());
                base.Update(gameTime);
                return;
            }

            bool f3 = keys.IsKeyDown(Keys.F3);
            if (f3 && !_lastF3) _showDebug = !_showDebug;
            _lastF3 = f3;

            bool t = keys.IsKeyDown(Keys.T);
            if (t && !_lastT) _wireframeMode = !_wireframeMode;
            _lastT = t;

            // ── F5 – toggle camera mode ───────────────────────────────
            bool f5 = keys.IsKeyDown(Keys.F5);
            if (f5 && !_lastF5)
            {
                _thirdPerson = !_thirdPerson;
                if (_thirdPerson)
                {
                    _player.Position = _camera.Position - new Vector3(0, PlayerController.Height, 0);
                    _player.Velocity = Vector3.Zero;
                    _tpCamera.ResetMouseState();
                }
                else
                {
                    _camera.Position = _player.VisualPosition + new Vector3(0, PlayerController.Height * 0.9f, 0);
                }
            }
            _lastF5 = f5;

            var mouse = Mouse.GetState();

            bool lmbNow = (mouse.LeftButton == ButtonState.Pressed);
            bool rmbNow = (mouse.RightButton == ButtonState.Pressed);

            if (_thirdPerson)
            {
                if (lmbNow && !_lastLMB)
                    _playerRenderer.RequestAttack();

                if (rmbNow && !_lastRMB)
                    _playerRenderer.RequestSheathe();
            }

            _lastLMB = lmbNow;
            _lastRMB = rmbNow;

            // ── Update camera / player ────────────────────────────────
            if (_thirdPerson)
            {
                _tpCamera.Update(gameTime, _player.VisualPosition);
                _player.Update(gameTime, _tpCamera.PlayerFacingYaw);
            }
            else
            {
                _camera.Update(gameTime);
            }

            _skybox.Update(gameTime);
            _waterTime += (float)gameTime.ElapsedGameTime.TotalSeconds;
            _waterEffect.Time = _waterTime;

            _voxelEffect.ApplyFromSkybox(_skybox);
            _waterEffect.ApplyFromSkybox(_skybox);

            if (_voxelEffect.FogEnabled)
            {
                Vector3 fogCol = _skybox.GetFogColor();
                _voxelEffect.FogColor = fogCol;
                _waterEffect.FogColor = fogCol;
            }

            Vector3 shaderCamPos = _thirdPerson ? _tpCamera.EyePosition : _camera.Position;
            _voxelEffect.SetCameraPosition(shaderCamPos);
            _waterEffect.SetCameraPosition(shaderCamPos);

            UploadEmissiveLights();

            double elapsed = _frameTimer.Elapsed.TotalMilliseconds;
            _frameTimer.Restart();
            if (elapsed > 0 && elapsed < 2000)
            {
                _frameTimes.Enqueue(elapsed);
                if (_frameTimes.Count > FPS_WINDOW) _frameTimes.Dequeue();
            }
            if (_frameTimes.Count > 0)
            {
                double sum = 0;
                foreach (var ft in _frameTimes) sum += ft;
                double avgMs = sum / _frameTimes.Count;
                _frameMs = (float)avgMs;
                _smoothFps = avgMs > 0 ? (float)(1000.0 / avgMs) : 0f;
            }

            Vector3 updatePlayerPos = _thirdPerson ? _player.Position : _camera.Position;
            _entityManager.Update(gameTime, updatePlayerPos, _chunkManager, _loadDistance, _worldGenerator);

            // Conectar ataque del player con el hit de entidades:
            // Cuando el PlayerRenderer entra en HitStopTimer y estamos en 3rd person:
            if (_thirdPerson && _playerRenderer.HitStopTimer > 0f && _lastLMB)
            {
                const float AttackReach = 4f;
                const float AttackDamage = 4f;
                _entityManager.TryHitNearest(
                    _player.Position + new Vector3(0f, PlayerController.Height * 0.6f, 0f),
                    AttackReach,
                    AttackDamage);
            }

            base.Update(gameTime);
        }

        private void UploadEmissiveLights()
        {
            _voxelEffect.ClearPointLights();
            _waterEffect.ClearPointLights();

            foreach (var (worldPos, blockType) in _emissiveBlocks)
            {
                if (!BlockType.IsEmissive(blockType)) continue;
                var (color, radius, intensity) = BlockType.GetEmissiveLight(blockType);
                Vector3 centre = worldPos + new Vector3(0.5f, 0.5f, 0.5f);
                _voxelEffect.AddPointLight(centre, color, radius, intensity);
                _waterEffect.AddPointLight(centre, color, radius, intensity);
            }

            _voxelEffect.UploadPointLights();
            _waterEffect.UploadPointLights();
        }

        public void AddEmissiveBlock(Vector3 worldPos, byte blockType)
        {
            if (BlockType.IsEmissive(blockType))
                _emissiveBlocks.Add((worldPos, blockType));
        }

        public void RemoveEmissiveBlock(Vector3 worldPos)
        {
            _emissiveBlocks.RemoveAll(e =>
                (int)e.worldPos.X == (int)worldPos.X &&
                (int)e.worldPos.Y == (int)worldPos.Y &&
                (int)e.worldPos.Z == (int)worldPos.Z);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;

            // ── Resolve active camera ─────────────────────────────────
            Matrix activeView = _thirdPerson ? _tpCamera.ViewMatrix : _camera.ViewMatrix;
            Matrix activeProj = _thirdPerson ? _tpCamera.ProjectionMatrix : _camera.ProjectionMatrix;
            Vector3 activeCamPos = _thirdPerson ? _tpCamera.EyePosition : _camera.Position;
            BoundingFrustum activeFrustum = _thirdPerson ? _tpCamera.GetFrustum() : _camera.GetFrustum();
            _skybox.Draw(activeView, activeProj, activeCamPos);

            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;

            // ─────────────────────────────────────────────────────────
            //  PASS 1 – Opaque geometry
            // ─────────────────────────────────────────────────────────
            GraphicsDevice.RasterizerState = _wireframeMode ? _wireframeState : _solidState;

            _voxelEffect.View = activeView;
            _voxelEffect.Projection = activeProj;

            if (_wireframeMode)
            {
                var cur = _chunkManager.GetChunkCoordinates(activeCamPos);
                _chunkManager.Draw(_voxelEffect, activeFrustum, cur, wireframeOnly: true);
            }
            else
            {
                _chunkManager.Draw(_voxelEffect, activeFrustum);
            }

            // ─────────────────────────────────────────────────────────
            //  PASS 2/3 – Water
            // ─────────────────────────────────────────────────────────
            if (_fancyWater)
            {
                _waterEffect.View = activeView;
                _waterEffect.Projection = activeProj;

                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.BlendState = BlendState.Opaque;
                GraphicsDevice.RasterizerState = new RasterizerState
                { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.Solid };
                _chunkManager.DrawWater(_waterEffect, activeFrustum);

                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                GraphicsDevice.RasterizerState = new RasterizerState
                { CullMode = CullMode.None, FillMode = FillMode.Solid };
                _chunkManager.DrawRiver(_waterEffect, activeFrustum);

                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.BlendState = BlendState.Opaque;
                GraphicsDevice.RasterizerState = _solidState;
            }

            // ── Player mesh (third-person only) ───────────────────────
            if (_thirdPerson)
            {
                _playerRenderer.Draw(
                    feetPos: _player.VisualPosition,
                    bodyYaw: _player.BodyYaw,
                    headYawOffset: _player.HeadYawOffset,
                    view: activeView,
                    proj: activeProj,
                    isGrounded: _player.IsGrounded,
                    velocity: _player.Velocity,
                    gameTime: gameTime,
                    isMoving: _player.IsMoving);   // ← NUEVO
            }

            _entityManager.Draw(gameTime, activeView, activeProj);

            // ─────────────────────────────────────────────────────────
            //  HUD
            // ─────────────────────────────────────────────────────────
            if (_debugFont != null)
            {
                _spriteBatch.Begin();
                if (!_pauseMenu.IsOpen)
                {
                    if (_showDebug) DrawF3Hud();
                    else DrawMinimalHud();
                }
                _pauseMenu.Draw(_spriteBatch);
                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }

        // ─── HUD helpers ──────────────────────────────────────────────
        private void DrawMinimalHud()
        {
            int vw = GraphicsDevice.Viewport.Width;
            Color fpsCol = FpsColor(_smoothFps);
            string fpsStr = $"{_smoothFps:F0} fps";
            Vector2 sz = _debugFont.MeasureString(fpsStr);
            DrawTS(fpsStr, new Vector2(vw - sz.X - 8, 8), fpsCol);

            string modeStr = _thirdPerson ? "[3P]" : "[FC]";
            DrawTS(modeStr, new Vector2(vw - sz.X - 8, 8 + LineH),
                   _thirdPerson ? CWarn : CGood);
        }

        private void DrawF3Hud()
        {
            Vector3 pos = _thirdPerson ? _player.Position : _camera.Position;
            var chunkPos = _chunkManager.GetChunkCoordinates(pos);
            var curChunk = _chunkManager.GetChunk(chunkPos);
            int lx = ((int)Math.Floor(pos.X) % 32 + 32) % 32;
            int ly = ((int)Math.Floor(pos.Y) % 32 + 32) % 32;
            int lz = ((int)Math.Floor(pos.Z) % 32 + 32) % 32;

            var L = new List<(string lbl, string val, Color col)>();
            var R = new List<(string lbl, string val, Color col)>();

            L.Add(("", "Voxel Engine", CHeader));
            L.Add(("mode", _thirdPerson ? "3rd person [F5]" : "free cam   [F5]",
                   _thirdPerson ? CWarn : CGood));
            L.Add(("", "", CLabel));
            Color fc = FpsColor(_smoothFps);
            L.Add(("fps", $"{_smoothFps:F1}", fc));
            L.Add(("ms", $"{_frameMs:F2}", fc));
            L.Add(("", "", CLabel));
            L.Add(("", "[ Position ]", CHeader));
            L.Add(("x", $"{pos.X:F3}", CValue));
            L.Add(("y", $"{pos.Y:F3}", CValue));
            L.Add(("z", $"{pos.Z:F3}", CValue));

            if (_thirdPerson)
            {
                L.Add(("", "", CLabel));
                L.Add(("", "[ Player ]", CHeader));
                L.Add(("grounded", _player.IsGrounded ? "yes" : "no",
                       _player.IsGrounded ? CGood : CWarn));
                L.Add(("moving", _player.IsMoving ? "yes" : "no",
                       _player.IsMoving ? CGood : CLabel));
                L.Add(("sword", _playerRenderer.Mode.ToString(), ModeColor(_playerRenderer.Mode)));
                L.Add(("vel y", $"{_player.Velocity.Y:F2}", CValue));
            }

            L.Add(("", "", CLabel));
            L.Add(("", "[ Chunk ]", CHeader));
            L.Add(("chunk", $"{chunkPos.X}, {chunkPos.Y}, {chunkPos.Z}", CValue));
            L.Add(("local", $"{lx}, {ly}, {lz}", CValue));
            L.Add(("", "", CLabel));
            L.Add(("cam light", _cameraLightEnabled ? $"ON  r={_cameraLightRadius:F0}" : "off",
                   _cameraLightEnabled ? CGood : CLabel));
            L.Add(("water time", $"{_waterTime:F1}s", CValue));
            L.Add(("wireframe", _wireframeMode ? "ON  [T]" : "off [T]",
                   _wireframeMode ? CWarn : CLabel));

            R.Add(("", "[ World ]", CHeader));
            R.Add(("chunk size", "32x32x32", CValue));
            R.Add(("HQ dist", $"{_loadDistance} chunks", CValue));
            R.Add(("LP dist", $"{_loadDistance * 4} chunks", CValue));
            if (_pauseMenu.Settings.EnableVeryLowPoly)
                R.Add(("VLP dist", $"{_loadDistance * 4 + 4} chunks", CValue));

            float tod = _skybox.TimeOfDay;
            int hh = (int)tod, mm = (int)((tod - hh) * 60f);
            R.Add(("", "", CLabel));
            R.Add(("", "[ Time of Day ]", CHeader));
            R.Add(("time", $"{hh:00}:{mm:00}", CValue));
            R.Add(("phase", _skybox.IsDayTime ? "day" : "night",
                   _skybox.IsDayTime ? CGood : new Color(100, 140, 255)));

            R.Add(("", "", CLabel));
            R.Add(("", "[ Lights ]", CHeader));
            R.Add(("emissive", $"{_emissiveBlocks.Count}", CValue));

            R.Add(("", "", CLabel));
            R.Add(("", "[ Chunks ]", CHeader));
            int loaded = _chunkManager.LoadedChunkCount;
            int total = _chunkManager.TotalChunkEntries;
            int queued = _chunkManager.GenerationQueueCount;
            int working = _chunkManager.ActiveGenerationTasks;
            int pending = queued + working;
            R.Add(("loaded", $"{loaded} / {total}", CValue));
            R.Add(("building", $"{working}", working > 0 ? CWarn : CValue));
            R.Add(("queued", $"{queued}", queued > 200 ? CWarn : CValue));
            R.Add(("pending", $"{pending}", pending > 200 ? CWarn : CGood));
            R.Add(("", "", CLabel));
            R.Add(("", "[ Current Chunk ]", CHeader));
            if (curChunk != null)
            {
                R.Add(("mesh", curChunk.HasMesh ? "ready" : "building",
                        curChunk.HasMesh ? CGood : CWarn));
                R.Add(("water mesh", curChunk.HasWaterMesh ? "ready" : "none",
                        curChunk.HasWaterMesh ? new Color(80, 160, 255) : CLabel));
                if (curChunk.DebugInfo != null)
                {
                    var di = curChunk.DebugInfo;
                    R.Add(("verts", $"{di.VertexCount:N0}", CValue));
                    R.Add(("tris", $"{di.TriangleCount:N0}", CValue));
                    R.Add(("gen ms", $"{di.MeshGenerationTimeMs:F1}", CValue));
                }
            }
            else R.Add(("status", "not loaded", CWarn));


            R.Add(("", "", CLabel));
            R.Add(("", "[ Entities ]", CHeader));
            R.Add(("alive", $"{_entityManager.EntityCount}", CValue));
            R.Add(("particles", $"{_entityManager.ParticleCount}", CValue));

            int vw = GraphicsDevice.Viewport.Width;
            int leftH = L.Count * LineH + PadY * 2;
            int rightH = R.Count * LineH + PadY * 2;

            DrawPanel(PadX - 2, PadY - 2, ColW + 4, leftH + 4);
            DrawPanel(vw - ColW - PadX - 2, PadY - 2, ColW + 4, rightH + 4);

            int y = PadY;
            foreach (var (lbl, val, col) in L) { DrawLine(PadX, y, lbl, val, col); y += LineH; }
            y = PadY;
            foreach (var (lbl, val, col) in R) { DrawLine(vw - ColW - PadX, y, lbl, val, col); y += LineH; }

            if (!_thirdPerson) DrawCrosshair();
        }

        private void DrawLine(int x, int y, string lbl, string val, Color col)
        {
            if (string.IsNullOrEmpty(lbl)) { DrawTS(val, new Vector2(x, y), col); return; }
            string ls = lbl + ": ";
            DrawTS(ls, new Vector2(x, y), CLabel);
            DrawTS(val, new Vector2(x + _debugFont.MeasureString(ls).X, y), col);
        }

        private void DrawTS(string text, Vector2 pos, Color color)
        {
            _spriteBatch.DrawString(_debugFont, text, pos + Vector2.One, Color.Black * 0.7f);
            _spriteBatch.DrawString(_debugFont, text, pos, color);
        }

        private void DrawPanel(int x, int y, int w, int h)
            => _spriteBatch.Draw(_pixel, new Rectangle(x, y, w, h), Color.Black * 0.45f);

        private static Color ModeColor(SwordMode m) => m switch
        {
            SwordMode.Sheathed => new Color(150, 150, 155),
            SwordMode.Drawing => new Color(255, 200, 50),
            SwordMode.Holding => new Color(100, 220, 255),
            SwordMode.Attacking => new Color(255, 80, 80),
            SwordMode.Sheathing => new Color(180, 150, 255),
            _ => Color.White,
        };

        private void DrawCrosshair()
        {
            int cx = GraphicsDevice.Viewport.Width / 2;
            int cy = GraphicsDevice.Viewport.Height / 2;
            const int s = 7, th = 1;
            _spriteBatch.Draw(_pixel, new Rectangle(cx - s - 1, cy - th - 1, s * 2 + 2, th * 2 + 3), Color.Black * 0.5f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - th - 1, cy - s - 1, th * 2 + 3, s * 2 + 2), Color.Black * 0.5f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - s, cy - th, s * 2, th * 2 + 1), Color.White * 0.9f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - th, cy - s, th * 2 + 1, s * 2), Color.White * 0.9f);
        }

        private static Color FpsColor(float fps)
            => fps >= 50 ? CGood : fps >= 30 ? CWarn : CBad;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _chunkManager?.Dispose();
                _voxelEffect?.Dispose();
                _waterEffect?.Dispose();
                _skybox?.Dispose();
                _skyEffect?.Dispose();
                _waterFx?.Dispose();
                _playerRenderer?.Dispose();
                _spriteBatch?.Dispose();
                _pixel?.Dispose();
                _entityManager?.Dispose();
                _particleSystem?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}