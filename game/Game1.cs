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
        private BasicEffect _effect;

        private Camera _camera;
        private ChunkManager _chunkManager;

        private const int ChunkSize = 32;
        private const int LoadDistance = 4;

        // ── UI ──────────────────────────────────────────────────────
        private SpriteBatch _spriteBatch;
        private SpriteFont  _debugFont;
        private Texture2D   _pixel;

        private bool _showDebug     = false;
        private bool _wireframeMode = false;
        private bool _lastF3        = false;
        private bool _lastT         = false;

        private RasterizerState _solidState;
        private RasterizerState _wireframeState;

        // ── FPS real (Stopwatch, no ElapsedGameTime) ─────────────────
        private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
        private const int FPS_WINDOW = 60;
        private readonly Queue<double> _frameTimes = new Queue<double>(FPS_WINDOW);
        private float _smoothFps;
        private float _frameMs;

        // ── Colores ──────────────────────────────────────────────────
        private static readonly Color CLabel  = new Color(180, 180, 180);
        private static readonly Color CValue  = Color.White;
        private static readonly Color CHeader = new Color(100, 220, 255);
        private static readonly Color CWarn   = new Color(255, 200, 80);
        private static readonly Color CGood   = new Color(100, 255, 140);
        private static readonly Color CBad    = new Color(255, 80,  80);

        // ── Layout ───────────────────────────────────────────────────
        private const int PadX     = 6;
        private const int PadY     = 6;
        private const int LineH    = 14;
        private const int ColW     = 230;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = false;

            _graphics.PreferredBackBufferWidth  = 1280;
            _graphics.PreferredBackBufferHeight = 720;
            _graphics.HardwareModeSwitch = false;

            IsFixedTimeStep = false;

            _graphics.ApplyChanges();
        }

        protected override void Initialize()
        {
            float aspect = (float)GraphicsDevice.Viewport.Width / GraphicsDevice.Viewport.Height;
            _camera = new Camera(
                startPosition:  new Vector3(0, 70, 0),
                aspectRatio:    aspect,
                graphicsDevice: GraphicsDevice,
                fov:            MathHelper.PiOver2 - 0.1f,
                nearPlane:      0.1f,
                farPlane:       1000f);

            _chunkManager = new ChunkManager(GraphicsDevice, ChunkSize, LoadDistance);

            _solidState     = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.Solid };
            _wireframeState = new RasterizerState { CullMode = CullMode.CullClockwiseFace, FillMode = FillMode.WireFrame };

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled    = true,
                AmbientLightColor  = new Vector3(0.5f, 0.5f, 0.5f),
                FogEnabled         = false,
                FogStart           = LoadDistance * 2 * 24f,
                FogColor           = new Vector3(135f / 255f, 206f / 255f, 235f / 255f),
                FogEnd             = LoadDistance * 2 * 35f,
            };
            _effect.DirectionalLight0.Enabled      = true;
            _effect.DirectionalLight0.Direction    = Vector3.Normalize(new Vector3(1, -1, 0.5f));
            _effect.DirectionalLight0.DiffuseColor = new Vector3(0.8f, 0.8f, 0.8f);

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            try   { _debugFont = Content.Load<SpriteFont>("DebugFont"); }
            catch { _debugFont = null; }
        }

        // ============================================================
        //  UPDATE
        // ============================================================

        protected override void Update(GameTime gameTime)
        {
            var keys = Keyboard.GetState();

            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
                keys.IsKeyDown(Keys.Escape))
                Exit();

            bool f3 = keys.IsKeyDown(Keys.F3);
            if (f3 && !_lastF3) _showDebug = !_showDebug;
            _lastF3 = f3;

            bool t = keys.IsKeyDown(Keys.T);
            if (t && !_lastT) _wireframeMode = !_wireframeMode;
            _lastT = t;

            _camera.Update(gameTime);
            _chunkManager.Update(_camera.Position, null);

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
                _frameMs   = (float)avgMs;
                _smoothFps = avgMs > 0 ? (float)(1000.0 / avgMs) : 0f;
            }

            base.Update(gameTime);
        }

        // ============================================================
        //  DRAW
        // ============================================================

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(135, 206, 235));
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState   = _wireframeMode ? _wireframeState : _solidState;
            GraphicsDevice.BlendState        = BlendState.Opaque;

            _effect.View       = _camera.ViewMatrix;
            _effect.Projection = _camera.ProjectionMatrix;

            var frustum = _camera.GetFrustum();

            if (_wireframeMode)
            {
                var cur = _chunkManager.GetChunkCoordinates(_camera.Position);
                _chunkManager.Draw(_effect, frustum, cur, wireframeOnly: true);
            }
            else
            {
                _chunkManager.Draw(_effect, frustum);
            }

            if (_debugFont != null)
            {
                _spriteBatch.Begin();
                if (_showDebug) DrawF3Hud();
                else            DrawMinimalHud();
                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }

        // ============================================================
        //  HUD MÍNIMO
        // ============================================================

        private void DrawMinimalHud()
        {
            int vw = GraphicsDevice.Viewport.Width;

            Color fpsCol = FpsColor(_smoothFps);
            string fpsStr = $"{_smoothFps:F0} fps";
            Vector2 sz = _debugFont.MeasureString(fpsStr);
            DrawTS(fpsStr, new Vector2(vw - sz.X - 8, 8), fpsCol);
            DrawTS("[F3] debug", new Vector2(vw - 72, 8 + LineH), CLabel);

            DrawCrosshair();
        }

        // ============================================================
        //  HUD F3
        // ============================================================

        private void DrawF3Hud()
        {
            Vector3 pos      = _camera.Position;
            var chunkPos     = _chunkManager.GetChunkCoordinates(pos);
            var curChunk     = _chunkManager.GetChunk(chunkPos);

            int lx = ((int)Math.Floor(pos.X) % ChunkSize + ChunkSize) % ChunkSize;
            int ly = ((int)Math.Floor(pos.Y) % ChunkSize + ChunkSize) % ChunkSize;
            int lz = ((int)Math.Floor(pos.Z) % ChunkSize + ChunkSize) % ChunkSize;

            var L = new List<(string lbl, string val, Color col)>();
            var R = new List<(string lbl, string val, Color col)>();

            // ── Columna izquierda ────────────────────────────────────
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

            L.Add(("", "", CLabel));
            L.Add(("wireframe", _wireframeMode ? "ON  [T]" : "off [T]",
                   _wireframeMode ? CWarn : CLabel));

            // ── Columna derecha ──────────────────────────────────────
            R.Add(("", "[ World ]", CHeader));
            R.Add(("chunk size", $"{ChunkSize}x{ChunkSize}x{ChunkSize}", CValue));
            R.Add(("load dist",  $"{LoadDistance} chunks",               CValue));
            R.Add(("lod dist",   $"{LoadDistance * 4} chunks",           CValue));

            R.Add(("", "", CLabel));
            R.Add(("", "[ Chunks ]", CHeader));

            int loaded  = _chunkManager.LoadedChunkCount;   // solo chunks con geometría real
            int total   = _chunkManager.TotalChunkEntries;  // todos en diccionario (incluye aire)
            int queued  = _chunkManager.GenerationQueueCount;
            int working = _chunkManager.ActiveGenerationTasks;
            int pending = queued + working;

            // "loaded" = chunks con mesh real / total en memoria (incluye chunks de aire puro)
            R.Add(("loaded",   $"{loaded} / {total}",                    CValue));
            R.Add(("building", $"{working}",                             working > 0 ? CWarn : CValue));
            R.Add(("queued",   $"{queued}",                              queued  > 200 ? CWarn : CValue));
            R.Add(("pending",  $"{pending}",                             pending > 200 ? CWarn : CGood));

            R.Add(("", "", CLabel));
            R.Add(("", "[ Current Chunk ]", CHeader));

            if (curChunk != null)
            {
                R.Add(("mesh", curChunk.HasMesh ? "ready" : "building",
                       curChunk.HasMesh ? CGood : CWarn));

                if (curChunk.DebugInfo != null)
                {
                    var di = curChunk.DebugInfo;
                    R.Add(("verts",   $"{di.VertexCount:N0}",   CValue));
                    R.Add(("tris",    $"{di.TriangleCount:N0}", CValue));
                    R.Add(("gen ms",  $"{di.MeshGenerationTimeMs:F1}", CValue));
                    R.Add(("mesh ms", $"{di.GreedyMeshingTimeMs:F1}", CValue));
                }
            }
            else
            {
                R.Add(("status", "not loaded", CWarn));
            }

            // ── Fondos ───────────────────────────────────────────────
            int vw    = GraphicsDevice.Viewport.Width;
            int leftH = L.Count * LineH + PadY * 2;
            int rightH = R.Count * LineH + PadY * 2;

            DrawPanel(PadX - 2,                  PadY - 2, ColW + 4, leftH  + 4);
            DrawPanel(vw - ColW - PadX - 2,      PadY - 2, ColW + 4, rightH + 4);

            // ── Líneas ───────────────────────────────────────────────
            int y = PadY;
            foreach (var (lbl, val, col) in L) { DrawLine(PadX, y, lbl, val, col); y += LineH; }

            y = PadY;
            foreach (var (lbl, val, col) in R) { DrawLine(vw - ColW - PadX, y, lbl, val, col); y += LineH; }

            DrawCrosshair();
        }

        // ============================================================
        //  HELPERS
        // ============================================================

        private void DrawLine(int x, int y, string lbl, string val, Color col)
        {
            if (string.IsNullOrEmpty(lbl))
            {
                DrawTS(val, new Vector2(x, y), col);
                return;
            }
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
        {
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, w, h), Color.Black * 0.45f);
        }

        private void DrawCrosshair()
        {
            int cx = GraphicsDevice.Viewport.Width  / 2;
            int cy = GraphicsDevice.Viewport.Height / 2;
            const int s = 7, t = 1;
            _spriteBatch.Draw(_pixel, new Rectangle(cx - s - 1, cy - t - 1, s*2+2, t*2+3), Color.Black * 0.5f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - t - 1, cy - s - 1, t*2+3, s*2+2), Color.Black * 0.5f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - s, cy - t, s*2, t*2+1), Color.White * 0.9f);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - t, cy - s, t*2+1, s*2), Color.White * 0.9f);
        }

        private static Color FpsColor(float fps)
            => fps >= 55 ? CGood : fps >= 30 ? CWarn : CBad;

        private static string CardinalFacing(Vector3 fwd)
        {
            float deg = MathHelper.ToDegrees((float)Math.Atan2(fwd.X, fwd.Z));
            if (deg < 0) deg += 360f;
            return deg switch
            {
                < 22.5f  => "South",
                < 67.5f  => "SW",
                < 112.5f => "West",
                < 157.5f => "NW",
                < 202.5f => "North",
                < 247.5f => "NE",
                < 292.5f => "East",
                < 337.5f => "SE",
                _        => "South"
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _chunkManager?.Dispose();
                _effect?.Dispose();
                _spriteBatch?.Dispose();
                _pixel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}