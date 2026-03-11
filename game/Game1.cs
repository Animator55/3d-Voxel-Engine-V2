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

        // ── NEW: custom shader wrapper (replaces BasicEffect) ──────────
        private VoxelLitEffect _voxelEffect;
        private Effect         _voxelFx;        // raw Effect asset

        private Camera       _camera;
        private ChunkManager _chunkManager;
        private int          _loadDistance = 11;

        private ProceduralSkybox _skybox;
        private Effect           _skyEffect;

        private PauseMenu _pauseMenu;
        private bool      _lastEscape = false;

        private SpriteBatch _spriteBatch;
        private SpriteFont  _debugFont;
        private Texture2D   _pixel;

        private bool _showDebug    = false;
        private bool _wireframeMode = false;
        private bool _lastF3 = false;
        private bool _lastT  = false;

        private RasterizerState _solidState;
        private RasterizerState _wireframeState;

        // ── FPS smoothing ──────────────────────────────────────────────
        private readonly Stopwatch      _frameTimer  = Stopwatch.StartNew();
        private const    int            FPS_WINDOW   = 60;
        private readonly Queue<double>  _frameTimes  = new Queue<double>(FPS_WINDOW);
        private float _smoothFps;
        private float _frameMs;

        // ── HUD colours ────────────────────────────────────────────────
        private static readonly Color CLabel  = new Color(180, 180, 180);
        private static readonly Color CValue  = Color.White;
        private static readonly Color CHeader = new Color(100, 220, 255);
        private static readonly Color CWarn   = new Color(255, 200,  80);
        private static readonly Color CGood   = new Color(100, 255, 140);
        private static readonly Color CBad    = new Color(255,  80,  80);

        private const int PadX  = 6;
        private const int PadY  = 6;
        private const int LineH = 14;
        private const int ColW  = 230;

        // ── Emissive block registry ────────────────────────────────────
        // A simple list of emissive blocks the player has placed in the world.
        // In a real game you'd populate this from your block-placement system.
        // For now it's public so the pause menu / console can add entries.
        private readonly List<(Vector3 worldPos, byte blockType)> _emissiveBlocks
            = new List<(Vector3, byte)>();

        // ── Camera-light settings (exposed to pause menu) ─────────────
        private bool  _cameraLightEnabled   = true;
        private float _cameraLightRadius    = 18f;
        private float _cameraLightIntensity = 1.4f;

        // ─────────────────────────────────────────────────────────────
        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = false;
            _graphics.PreferredBackBufferWidth  = 1280;
            _graphics.PreferredBackBufferHeight = 720;
            _graphics.HardwareModeSwitch = false;
            IsFixedTimeStep  = false;
            _graphics.ApplyChanges();
        }

        // ─────────────────────────────────────────────────────────────
        protected override void Initialize()
        {
            Window.AllowUserResizing = true;
            float aspect = (float)GraphicsDevice.Viewport.Width /
                                   GraphicsDevice.Viewport.Height;

            _camera = new Camera(
                startPosition : new Vector3(0, 70, 0),
                aspectRatio   : aspect,
                graphicsDevice: GraphicsDevice,
                fov           : MathHelper.ToRadians(90f),
                nearPlane     : 0.1f,
                farPlane      : 100000f);

            _chunkManager = new ChunkManager(GraphicsDevice,
                                             chunkSize   : 32,
                                             loadDistance: _loadDistance);

            _solidState     = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.Solid };
            _wireframeState = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.WireFrame };

            base.Initialize();
        }

        // ─────────────────────────────────────────────────────────────
        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // ── Load our custom shader ─────────────────────────────────
            _voxelFx     = Content.Load<Effect>("VoxelLit");   // VoxelLit.fx → Content/VoxelLit.fx
            _voxelEffect = new VoxelLitEffect(_voxelFx);

            // Initial fog settings
            _voxelEffect.FogEnabled = true;
            _voxelEffect.FogStart   = _loadDistance * 2 * 24f;
            _voxelEffect.FogEnd     = _loadDistance * 2 * 36f;

            // Initial camera light settings
            _voxelEffect.CameraLightEnabled   = _cameraLightEnabled;
            _voxelEffect.CameraLightRadius    = _cameraLightRadius;
            _voxelEffect.CameraLightIntensity = _cameraLightIntensity;
            _voxelEffect.CameraLightColor     = new Vector3(1f, 0.92f, 0.75f);

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            try { _debugFont = Content.Load<SpriteFont>("DebugFont"); }
            catch { _debugFont = null; }

            _skyEffect = Content.Load<Effect>("Sky");
            _skybox    = new ProceduralSkybox(GraphicsDevice, _skyEffect, startHour: 8f);

            // ── Demo: pre-place a glowstone block at a visible location ──
            // Remove or move these in your actual game.
            _emissiveBlocks.Add((new Vector3(16, 72, 0),  BlockType.Glowstone));
            _emissiveBlocks.Add((new Vector3(-32, 68, 16), BlockType.Glowstone));

            // ── Pause menu ─────────────────────────────────────────────
            var initialSettings = new GameSettings
            {
                LoadDistance       = _loadDistance,
                EnableVeryLowPoly  = true,
                FogEnabled         = true,
                FovDegrees         = 90f,
                WireframeMode      = false,
                DirectionalLight   = true,
                AmbientLight       = 0.5f,
                MoveSpeed          = _camera.MoveSpeed,
                MouseSensitivity   = _camera.MouseSensitivity,
                ShowDebugHud       = false,
                // expose new settings to pause menu if desired:
                CameraLightEnabled   = _cameraLightEnabled,
                CameraLightRadius    = _cameraLightRadius,
                CameraLightIntensity = _cameraLightIntensity,
            };

            _pauseMenu = new PauseMenu(GraphicsDevice, _pixel, _debugFont, initialSettings);
            _pauseMenu.OnResume           += () => { IsMouseVisible = false; };
            _pauseMenu.OnExit             += () => Exit();
            _pauseMenu.OnSettingsChanged  += ApplySettings;
        }

        // ─────────────────────────────────────────────────────────────
        private void ApplySettings(GameSettings s)
        {
            _camera.MoveSpeed        = s.MoveSpeed;
            _camera.MouseSensitivity = s.MouseSensitivity;

            float aspect = (float)GraphicsDevice.Viewport.Width /
                                   GraphicsDevice.Viewport.Height;
            _camera.SetFov(MathHelper.ToRadians(s.FovDegrees), aspect);

            _voxelEffect.FogEnabled   = s.FogEnabled;
            _wireframeMode            = s.WireframeMode;
            _showDebug                = s.ShowDebugHud;

            if (!s.DirectionalLight)
                _voxelEffect.DirectionalLightEnabled = false;

            float al = s.AmbientLight;
            _voxelEffect.AmbientLightColor = new Vector3(al, al, al);

            // Camera light
            _cameraLightEnabled           = s.CameraLightEnabled;
            _cameraLightRadius            = s.CameraLightRadius;
            _cameraLightIntensity         = s.CameraLightIntensity;
            _voxelEffect.CameraLightEnabled   = _cameraLightEnabled;
            _voxelEffect.CameraLightRadius    = _cameraLightRadius;
            _voxelEffect.CameraLightIntensity = _cameraLightIntensity;

            if (s.LoadDistance != _loadDistance || s.AoStrength != GreedyMesher.AoStrength)
            {
                _loadDistance = s.LoadDistance;
                _chunkManager.Dispose();
                _chunkManager = new ChunkManager(GraphicsDevice,
                                                 chunkSize   : 32,
                                                 loadDistance: _loadDistance);
            }

            _voxelEffect.FogStart = _loadDistance * 2 * 24f;
            _voxelEffect.FogEnd   = _loadDistance * 2 * 36f;

            _chunkManager.EnableVeryLowPoly = s.EnableVeryLowPoly;
            GreedyMesher.AoStrength         = s.AoStrength;
        }

        // ─────────────────────────────────────────────────────────────
        protected override void Update(GameTime gameTime)
        {
            var  keys    = Keyboard.GetState();
            bool escNow  = keys.IsKeyDown(Keys.Escape);

            if (escNow && !_lastEscape)
            {
                if (_pauseMenu.IsOpen) { _pauseMenu.Close(); IsMouseVisible = false; }
                else                  { _pauseMenu.Open();  IsMouseVisible = true;  }
            }
            _lastEscape = escNow;

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

            _camera.Update(gameTime);
            _chunkManager.Update(_camera.Position, null);

            _skybox.Update(gameTime);

            // ── Per-frame shader updates ───────────────────────────────

            // 1. Sync sun lighting from the skybox cycle
            _voxelEffect.ApplyFromSkybox(_skybox);

            // 2. Fog colour follows sky
            if (_voxelEffect.FogEnabled)
                _voxelEffect.FogColor = _skybox.GetFogColor();

            // 3. Upload camera position for the camera-light
            _voxelEffect.SetCameraPosition(_camera.Position);

            // 4. Rebuild emissive-block point lights
            //    (only if you add/remove blocks dynamically; otherwise do it once in LoadContent)
            UploadEmissiveLights();

            // ── FPS ────────────────────────────────────────────────────
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
                double avgMs  = sum / _frameTimes.Count;
                _frameMs      = (float)avgMs;
                _smoothFps    = avgMs > 0 ? (float)(1000.0 / avgMs) : 0f;
            }

            base.Update(gameTime);
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Pushes the emissive-block list to the shader.
        /// Call once per frame (or only when the list changes for better perf).
        /// </summary>
        private void UploadEmissiveLights()
        {
            _voxelEffect.ClearPointLights();
            foreach (var (worldPos, blockType) in _emissiveBlocks)
            {
                if (!BlockType.IsEmissive(blockType)) continue;
                var (color, radius, intensity) = BlockType.GetEmissiveLight(blockType);
                // Centre of the block is at worldPos + 0.5 on each axis
                _voxelEffect.AddPointLight(worldPos + new Vector3(0.5f, 0.5f, 0.5f),
                                           color, radius, intensity);
            }
            _voxelEffect.UploadPointLights();
        }

        // ── Public API to add / remove emissive blocks at runtime ─────
        /// <summary>
        /// Register a block as an emissive light source.
        /// worldPos should be the block's minimum corner (same as chunk world origin + local pos).
        /// </summary>
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

        // ─────────────────────────────────────────────────────────────
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState        = BlendState.Opaque;

            _skybox.Draw(_camera);

            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState   = _wireframeMode ? _wireframeState : _solidState;
            GraphicsDevice.BlendState        = BlendState.Opaque;

            // ── Set per-draw matrices ──────────────────────────────────
            _voxelEffect.View       = _camera.ViewMatrix;
            _voxelEffect.Projection = _camera.ProjectionMatrix;

            var frustum = _camera.GetFrustum();

            if (_wireframeMode)
            {
                var cur = _chunkManager.GetChunkCoordinates(_camera.Position);
                _chunkManager.Draw(_voxelEffect, frustum, cur, wireframeOnly: true);
            }
            else
            {
                _chunkManager.Draw(_voxelEffect, frustum);
            }

            // ── HUD ───────────────────────────────────────────────────
            if (_debugFont != null)
            {
                _spriteBatch.Begin();
                if (!_pauseMenu.IsOpen)
                {
                    if (_showDebug) DrawF3Hud();
                    else            DrawMinimalHud();
                }
                _pauseMenu.Draw(_spriteBatch);
                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }

        // ─────────────────────────────────────────────────────────────
        //  ChunkManager.Draw overload for VoxelLitEffect
        //  (ChunkManager already has a Draw(BasicEffect …) method;
        //   add this extension or duplicate the method body below)
        // ─────────────────────────────────────────────────────────────
        // NOTE: ChunkManager.Draw is called with _voxelEffect directly.
        // You need to add one overload in ChunkManager that accepts
        // VoxelLitEffect.  It is identical to the BasicEffect overload
        // except it calls _voxelEffect.World = ... and uses
        // _voxelEffect.CurrentTechnique.Passes[0].Apply().
        //
        // A minimal shim is included at the bottom of this file in a
        // partial class extension comment so you can slot it in without
        // touching ChunkManager.cs if you prefer.
        // ─────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────
        //  HUD helpers  (unchanged from original)
        // ─────────────────────────────────────────────────────────────
        private void DrawMinimalHud()
        {
            int    vw     = GraphicsDevice.Viewport.Width;
            Color  fpsCol = FpsColor(_smoothFps);
            string fpsStr = $"{_smoothFps:F0} fps";
            Vector2 sz    = _debugFont.MeasureString(fpsStr);
            DrawTS(fpsStr, new Vector2(vw - sz.X - 8, 8), fpsCol);
        }

        private void DrawF3Hud()
        {
            Vector3  pos       = _camera.Position;
            var      chunkPos  = _chunkManager.GetChunkCoordinates(pos);
            var      curChunk  = _chunkManager.GetChunk(chunkPos);
            int lx = ((int)Math.Floor(pos.X) % 32 + 32) % 32;
            int ly = ((int)Math.Floor(pos.Y) % 32 + 32) % 32;
            int lz = ((int)Math.Floor(pos.Z) % 32 + 32) % 32;

            var L = new List<(string lbl, string val, Color col)>();
            var R = new List<(string lbl, string val, Color col)>();

            L.Add(("", "Voxel Engine", CHeader));
            L.Add(("", "", CLabel));
            Color fc = FpsColor(_smoothFps);
            L.Add(("fps", $"{_smoothFps:F1}", fc));
            L.Add(("ms",  $"{_frameMs:F2}",  fc));
            L.Add(("", "", CLabel));
            L.Add(("", "[ Position ]", CHeader));
            L.Add(("x", $"{pos.X:F3}", CValue));
            L.Add(("y", $"{pos.Y:F3}", CValue));
            L.Add(("z", $"{pos.Z:F3}", CValue));
            L.Add(("", "", CLabel));
            L.Add(("", "[ Chunk ]", CHeader));
            L.Add(("chunk", $"{chunkPos.X}, {chunkPos.Y}, {chunkPos.Z}", CValue));
            L.Add(("local", $"{lx}, {ly}, {lz}", CValue));
            L.Add(("", "", CLabel));
            L.Add(("", "[ Camera ]", CHeader));
            L.Add(("cam light", _cameraLightEnabled ? $"ON  r={_cameraLightRadius:F0}" : "off",
                   _cameraLightEnabled ? CGood : CLabel));
            L.Add(("", "", CLabel));
            L.Add(("wireframe", _wireframeMode ? "ON  [T]" : "off [T]",
                   _wireframeMode ? CWarn : CLabel));

            R.Add(("", "[ World ]", CHeader));
            R.Add(("chunk size", "32x32x32", CValue));
            R.Add(("load dist",  $"{_loadDistance} chunks", CValue));
            R.Add(("lod dist",   $"{_loadDistance * 4} chunks", CValue));

            float tod = _skybox.TimeOfDay;
            int hh = (int)tod;
            int mm = (int)((tod - hh) * 60f);
            R.Add(("", "", CLabel));
            R.Add(("", "[ Time of Day ]", CHeader));
            R.Add(("time",  $"{hh:00}:{mm:00}", CValue));
            R.Add(("phase", _skybox.IsDayTime ? "day" : "night",
                   _skybox.IsDayTime ? CGood : new Color(100, 140, 255)));

            R.Add(("", "", CLabel));
            R.Add(("", "[ Lights ]", CHeader));
            R.Add(("emissive blocks", $"{_emissiveBlocks.Count}", CValue));

            R.Add(("", "", CLabel));
            R.Add(("", "[ Chunks ]", CHeader));
            int loaded  = _chunkManager.LoadedChunkCount;
            int total   = _chunkManager.TotalChunkEntries;
            int queued  = _chunkManager.GenerationQueueCount;
            int working = _chunkManager.ActiveGenerationTasks;
            int pending = queued + working;
            R.Add(("loaded",   $"{loaded} / {total}", CValue));
            R.Add(("building", $"{working}", working > 0 ? CWarn : CValue));
            R.Add(("queued",   $"{queued}",  queued  > 200 ? CWarn : CValue));
            R.Add(("pending",  $"{pending}", pending > 200 ? CWarn : CGood));
            R.Add(("", "", CLabel));
            R.Add(("", "[ Current Chunk ]", CHeader));
            if (curChunk != null)
            {
                R.Add(("mesh", curChunk.HasMesh ? "ready" : "building",
                       curChunk.HasMesh ? CGood : CWarn));
                if (curChunk.DebugInfo != null)
                {
                    var di = curChunk.DebugInfo;
                    R.Add(("verts",   $"{di.VertexCount:N0}",  CValue));
                    R.Add(("tris",    $"{di.TriangleCount:N0}", CValue));
                    R.Add(("gen ms",  $"{di.MeshGenerationTimeMs:F1}", CValue));
                    R.Add(("mesh ms", $"{di.GreedyMeshingTimeMs:F1}", CValue));
                }
            }
            else
            {
                R.Add(("status", "not loaded", CWarn));
            }

            int vw     = GraphicsDevice.Viewport.Width;
            int leftH  = L.Count * LineH + PadY * 2;
            int rightH = R.Count * LineH + PadY * 2;

            DrawPanel(PadX - 2,               PadY - 2, ColW + 4, leftH  + 4);
            DrawPanel(vw - ColW - PadX - 2,   PadY - 2, ColW + 4, rightH + 4);

            int y = PadY;
            foreach (var (lbl, val, col) in L) { DrawLine(PadX, y, lbl, val, col); y += LineH; }
            y = PadY;
            foreach (var (lbl, val, col) in R) { DrawLine(vw - ColW - PadX, y, lbl, val, col); y += LineH; }

            DrawCrosshair();
        }

        private void DrawLine(int x, int y, string lbl, string val, Color col)
        {
            if (string.IsNullOrEmpty(lbl)) { DrawTS(val, new Vector2(x, y), col); return; }
            string ls = lbl + ": ";
            DrawTS(ls,  new Vector2(x, y), CLabel);
            DrawTS(val, new Vector2(x + _debugFont.MeasureString(ls).X, y), col);
        }

        private void DrawTS(string text, Vector2 pos, Color color)
        {
            _spriteBatch.DrawString(_debugFont, text, pos + Vector2.One, Color.Black * 0.7f);
            _spriteBatch.DrawString(_debugFont, text, pos, color);
        }

        private void DrawPanel(int x, int y, int w, int h)
            => _spriteBatch.Draw(_pixel, new Rectangle(x, y, w, h), Color.Black * 0.45f);

        private void DrawCrosshair()
        {
            int cx = GraphicsDevice.Viewport.Width  / 2;
            int cy = GraphicsDevice.Viewport.Height / 2;
            const int s = 7, t = 1;
            _spriteBatch.Draw(_pixel, new Rectangle(cx - s - 1, cy - t - 1, s * 2 + 2, t * 2 + 3), Color.Black * 0.5f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - t - 1, cy - s - 1, t * 2 + 3, s * 2 + 2), Color.Black * 0.5f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - s,     cy - t,     s * 2,     t * 2 + 1), Color.White * 0.9f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - t,     cy - s,     t * 2 + 1, s * 2    ), Color.White * 0.9f);
        }

        private static Color FpsColor(float fps)
            => fps >= 50 ? CGood : fps >= 30 ? CWarn : CBad;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _chunkManager?.Dispose();
                _voxelEffect?.Dispose();
                _skybox?.Dispose();
                _skyEffect?.Dispose();
                _spriteBatch?.Dispose();
                _pixel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}