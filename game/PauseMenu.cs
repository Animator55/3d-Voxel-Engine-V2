using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Menu de pausa estilo Minecraft con soporte completo de mouse.
    /// - Hover resalta botones
    /// - Click izquierdo activa / cambia valores
    /// - Botones < > laterales para sliders
    /// - Scroll con rueda del mouse en Options
    /// - Teclado sigue funcionando (Esc para cerrar/volver)
    ///
    /// USO en Game1:
    ///   1. Crear en LoadContent:
    ///        _pauseMenu = new PauseMenu(GraphicsDevice, _pixel, _debugFont, initialSettings);
    ///        _pauseMenu.OnResume += () => { IsMouseVisible = false; };
    ///        _pauseMenu.OnExit   += Exit;
    ///        _pauseMenu.OnSettingsChanged += ApplySettings;
    ///
    ///   2. En Update — pasar tambien MouseState:
    ///        bool escNow = keys.IsKeyDown(Keys.Escape);
    ///        if (escNow && !_lastEscape) _pauseMenu.Toggle();
    ///        _lastEscape = escNow;
    ///        if (_pauseMenu.IsOpen)
    ///        {
    ///            IsMouseVisible = true;
    ///            _pauseMenu.Update(gameTime, keys, Mouse.GetState());
    ///            base.Update(gameTime); return;
    ///        }
    ///
    ///   3. En Draw dentro del SpriteBatch.Begin/End existente:
    ///        _pauseMenu.Draw(_spriteBatch);
    /// </summary>
    public class PauseMenu
    {
        public bool IsOpen { get; private set; }

        private enum Screen { Main, Options }
        private Screen _screen = Screen.Main;

        public GameSettings Settings { get; private set; }

        public event Action OnResume;
        public event Action OnExit;
        public event Action<GameSettings> OnSettingsChanged;

        private readonly GraphicsDevice _gd;
        private readonly Texture2D      _pixel;
        private readonly SpriteFont     _font;

        // ── Input state ──────────────────────────────────────────────
        private KeyboardState  _prevKeys;
        private MouseState     _prevMouse;
        private bool           _inputReady = false;

        // ── Scroll opciones ──────────────────────────────────────────
        private int _scrollY = 0;   // pixels de scroll acumulado

        // ── Layout ───────────────────────────────────────────────────
        // Todo el menu se calcula relativo al centro de pantalla.
        // Ancho del "panel" de opciones (igual que MC: 400px para 1280)
        private const int MENU_W   = 408;   // ancho total del area de menu
        private const int BTN_H    = 22;    // altura de boton
        private const int BTN_GAP  = 4;     // espacio vertical entre botones
        private const int ARROW_W  = 22;    // ancho de boton < >
        private const int BEV      = 1;     // grosor del bisel
        private const int SEC_H    = 20;    // altura de separador de seccion
        private const int MARGIN_T = 16;    // margen superior del contenido

        // ── Paleta Minecraft ─────────────────────────────────────────
        private static readonly Color BTN_FACE     = new Color(98,  98,  98);
        private static readonly Color BTN_HOVER    = new Color(160, 160, 255);
        private static readonly Color BTN_LIGHT    = new Color(255, 255, 255);
        private static readonly Color BTN_DARK     = new Color(0,   0,   0);
        private static readonly Color TXT_WHITE    = Color.White;
        private static readonly Color TXT_SHADOW   = new Color(62,  62,  62);
        private static readonly Color TXT_SHADOW_H = new Color(40,  40,  100);
        private static readonly Color TXT_TITLE    = new Color(255, 255, 85);
        private static readonly Color TXT_TITLE_SH = new Color(63,  63,  21);
        private static readonly Color SEC_COLOR    = new Color(160, 160, 160);
        private static readonly Color SEC_SHADOW   = new Color(40,  40,  40);

        // ── Rects de botones (calculados en Draw, usados en Update) ──
        // Main screen
        private Rectangle _rResume, _rOptions, _rQuitL, _rQuitR;
        // Options screen — se recalculan cada frame con scroll
        private readonly List<(Rectangle rect, int optIdx, int side)> _optRects
            = new List<(Rectangle, int, int)>();
        // side: 0 = boton entero (bool/seccion), -1 = flecha izq, +1 = flecha der
        private Rectangle _rDone;

        // ── Main items ───────────────────────────────────────────────
        private readonly string[] _mainItems = { "Back to Game", "Options", "Save and Quit" };
        private List<OptionEntry> _options;

        // ============================================================
        public PauseMenu(GraphicsDevice graphicsDevice, Texture2D pixel, SpriteFont font,
                         GameSettings initialSettings = null)
        {
            _gd      = graphicsDevice;
            _pixel   = pixel;
            _font    = font;
            Settings = initialSettings ?? new GameSettings();
            BuildOptions();
        }

        // ============================================================
        //  OPCIONES
        // ============================================================

        private void BuildOptions()
        {
            _options = new List<OptionEntry>
            {
                new SectionEntry("World"),
                new IntSlider  ("Load Distance",    () => Settings.LoadDistance,
                                                    v  => { Settings.LoadDistance      = v; Fire(); }, 3, 20),
                new BoolToggle ("Very Low Poly",    () => Settings.EnableVeryLowPoly,
                                                    v  => { Settings.EnableVeryLowPoly = v; Fire(); }),

                new SectionEntry("Render"),
                new BoolToggle ("Fog",              () => Settings.FogEnabled,
                                                    v  => { Settings.FogEnabled        = v; Fire(); }),
                new BoolToggle ("Wireframe",        () => Settings.WireframeMode,
                                                    v  => { Settings.WireframeMode     = v; Fire(); }),
                new BoolToggle ("Directional Light",() => Settings.DirectionalLight,
                                                    v  => { Settings.DirectionalLight  = v; Fire(); }),
                new FloatSlider("Ambient Light",    () => Settings.AmbientLight,
                                                    v  => { Settings.AmbientLight      = v; Fire(); },
                                                    0f, 1f, 0.05f, v => $"{v:F2}"),
                new FloatSlider("AO Strength",      () => Settings.AoStrength,
                                                    v  => { Settings.AoStrength        = v; Fire(); },
                                                    0f, 1f, 0.05f, v => $"{v:F2}"),
                new FloatSlider("FOV",              () => Settings.FovDegrees,
                                                    v  => { Settings.FovDegrees        = v; Fire(); },
                                                    50f, 120f, 5f, v => $"{(int)v}"),

                new SectionEntry("Camera"),
                new FloatSlider("Move Speed",       () => Settings.MoveSpeed,
                                                    v  => { Settings.MoveSpeed         = v; Fire(); },
                                                    5f, 200f, 5f, v => $"{(int)v}"),
                new FloatSlider("Mouse Sensitivity",() => Settings.MouseSensitivity * 10000f,
                                                    v  => { Settings.MouseSensitivity  = v / 10000f; Fire(); },
                                                    1f, 50f, 1f, v => $"{(int)v}"),

                new SectionEntry("Debug"),
                new BoolToggle ("Debug HUD",        () => Settings.ShowDebugHud,
                                                    v  => { Settings.ShowDebugHud      = v; Fire(); }),
            };
        }

        private void Fire() => OnSettingsChanged?.Invoke(Settings);

        // ============================================================
        //  OPEN / CLOSE / TOGGLE
        // ============================================================

        public void Open()
        {
            IsOpen      = true;
            _screen     = Screen.Main;
            _scrollY    = 0;
            _inputReady = false;
        }

        public void Close() => IsOpen = false;
        public void Toggle() { if (IsOpen) Close(); else Open(); }

        // ============================================================
        //  UPDATE  (acepta MouseState ademas de KeyboardState)
        // ============================================================

        public void Update(GameTime gameTime, KeyboardState keys, MouseState mouse)
        {
            if (!IsOpen) return;

            // Primer frame: solo guardar estado para evitar clicks fantasma
            if (!_inputReady)
            {
                _prevKeys  = keys;
                _prevMouse = mouse;
                _inputReady = true;
                return;
            }

            bool escDown  = JP(keys, _prevKeys, Keys.Escape);
            bool clicked  = WasClicked(mouse, _prevMouse);
            Point mpos    = new Point(mouse.X, mouse.Y);

            switch (_screen)
            {
                case Screen.Main:
                    HandleMainInput(escDown, clicked, mpos);
                    break;

                case Screen.Options:
                    // Scroll con rueda
                    int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
                    if (wheel != 0) _scrollY = Math.Max(0, _scrollY - wheel / 4);

                    HandleOptionsInput(escDown, clicked, mpos);
                    break;
            }

            _prevKeys  = keys;
            _prevMouse = mouse;
        }

        // Sobrecarga sin mouse para compatibilidad
        public void Update(GameTime gameTime, KeyboardState keys)
            => Update(gameTime, keys, Mouse.GetState());

        private void HandleMainInput(bool esc, bool clicked, Point mpos)
        {
            if (esc) { Close(); OnResume?.Invoke(); return; }
            if (!clicked) return;

            if (_rResume.Contains(mpos))  { Close(); OnResume?.Invoke(); }
            else if (_rOptions.Contains(mpos)) { _screen = Screen.Options; _scrollY = 0; }
            else if (_rQuitL.Contains(mpos) || _rQuitR.Contains(mpos))
                OnExit?.Invoke();
        }

        private void HandleOptionsInput(bool esc, bool clicked, Point mpos)
        {
            if (esc) { _screen = Screen.Main; return; }
            if (_rDone.Contains(mpos) && clicked) { _screen = Screen.Main; return; }

            if (!clicked) return;

            foreach (var (rect, idx, side) in _optRects)
            {
                if (!rect.Contains(mpos)) continue;
                var o = _options[idx];
                if (o is BoolToggle bt && side == 0) bt.DoToggle();
                else if (o is IInteractable inter)
                {
                    if (side == -1) inter.Decrease();
                    else if (side == 1) inter.Increase();
                    else if (side == 0 && o is BoolToggle bt2) bt2.DoToggle();
                }
                break;
            }
        }

        private static bool JP(KeyboardState c, KeyboardState p, Keys k)
            => c.IsKeyDown(k) && !p.IsKeyDown(k);

        private static bool WasClicked(MouseState cur, MouseState prev)
            => cur.LeftButton == ButtonState.Released && prev.LeftButton == ButtonState.Pressed;

        // ============================================================
        //  DRAW
        // ============================================================

        public void Draw(SpriteBatch sb)
        {
            if (!IsOpen || _font == null) return;

            int vw = _gd.Viewport.Width;
            int vh = _gd.Viewport.Height;

            FillRect(sb, 0, 0, vw, vh, Color.Black * 0.5f);

            if (_screen == Screen.Main) DrawMain(sb, vw, vh);
            else                        DrawOpts(sb, vw, vh);
        }

        // ── MAIN ─────────────────────────────────────────────────────

        private void DrawMain(SpriteBatch sb, int vw, int vh)
        {
            int cx    = vw / 2;
            Point mp  = IsOpen ? Mouse.GetState().Position : new Point(-1, -1);

            // Titulo centrado verticalmente un poco arriba del centro
            int totalH = 14 + BTN_GAP          // titulo + espacio
                       + BTN_H + BTN_GAP        // Back to Game
                       + BTN_H;                 // Options + Quit (misma fila)
            int startY = (vh - totalH) / 2 - 20;

            // Titulo
            DrawTextCentered(sb, "Game Menu", cx, startY, TXT_TITLE, TXT_TITLE_SH);
            int y = startY + 14 + BTN_GAP * 3;

            // Back to Game — ancho completo
            _rResume = new Rectangle(cx - MENU_W/2, y, MENU_W, BTN_H);
            DrawButton(sb, _rResume, _mainItems[0], _rResume.Contains(mp));
            y += BTN_H + BTN_GAP;

            // Options | Save and Quit — mitad cada uno
            int halfW = (MENU_W - BTN_GAP) / 2;
            _rOptions = new Rectangle(cx - MENU_W/2,            y, halfW, BTN_H);
            _rQuitL   = new Rectangle(cx - MENU_W/2 + halfW + BTN_GAP, y, halfW, BTN_H);
            _rQuitR   = _rQuitL; // mismo rect, alias

            DrawButton(sb, _rOptions, _mainItems[1], _rOptions.Contains(mp));
            DrawButton(sb, _rQuitL,   _mainItems[2], _rQuitL.Contains(mp));
        }

        // ── OPTIONS ──────────────────────────────────────────────────

        private void DrawOpts(SpriteBatch sb, int vw, int vh)
        {
            int cx   = vw / 2;
            Point mp = IsOpen ? Mouse.GetState().Position : new Point(-1, -1);

            _optRects.Clear();

            // Titulo
            int titleY = MARGIN_T;
            DrawTextCentered(sb, "Options", cx, titleY, TXT_TITLE, TXT_TITLE_SH);

            // Area de scroll: entre titulo y boton Done
            int doneH   = BTN_H + BTN_GAP * 2;
            int areaTop = titleY + 14 + BTN_GAP * 2;
            int areaBot = vh - doneH;
            int areaH   = areaBot - areaTop;

            // Construir layout de todas las filas (sin scroll aun)
            // MC pone las opciones en dos columnas cuando son sliders/toggles,
            // una por fila cuando son secciones.
            var rows = BuildOptRows();

            // Altura total del contenido para limitar scroll
            int contentH = 0;
            foreach (var r in rows) contentH += r.height + BTN_GAP;
            int maxScroll = Math.Max(0, contentH - areaH);
            _scrollY = Math.Min(_scrollY, maxScroll);

            // Dibujar filas con offset de scroll
            int ry = areaTop - _scrollY;
            foreach (var row in rows)
            {
                int rowBot = ry + row.height;
                // Solo dibujar si esta dentro del area visible
                if (rowBot > areaTop && ry < areaBot)
                    DrawOptRow(sb, cx, ry, row, mp);
                ry += row.height + BTN_GAP;
            }

            // Done — fijo abajo
            _rDone = new Rectangle(cx - MENU_W/2, vh - BTN_H - BTN_GAP * 2, MENU_W, BTN_H);
            DrawButton(sb, _rDone, "Done", _rDone.Contains(mp));

            // Separador sobre Done
            FillRect(sb, cx - MENU_W/2, _rDone.Y - 3, MENU_W, 1,
                     new Color(80, 80, 80) * 0.6f);
        }

        // ── CONSTRUCCION DE FILAS DE OPCIONES ────────────────────────

        private struct OptRow
        {
            public bool   isSection;
            public string sectionLabel;
            public int    height;
            // Para filas de opciones: hasta 2 items por fila (columna izq/der)
            public int    idxL;   // -1 = vacio
            public int    idxR;   // -1 = vacio (fila de 1 solo)
        }

        private List<OptRow> BuildOptRows()
        {
            var rows = new List<OptRow>();
            int i = 0;
            while (i < _options.Count)
            {
                var o = _options[i];

                if (o is SectionEntry se)
                {
                    rows.Add(new OptRow
                    {
                        isSection    = true,
                        sectionLabel = se.Label,
                        height       = SEC_H,
                        idxL = -1, idxR = -1
                    });
                    i++;
                    continue;
                }

                // Opciones interactuables: agrupar de a dos por fila
                int idxL = i;
                int idxR = -1;
                i++;

                // Solo ponemos dos por fila si la siguiente tambien es interactuable
                if (i < _options.Count && _options[i] is not SectionEntry)
                {
                    idxR = i;
                    i++;
                }

                rows.Add(new OptRow
                {
                    isSection = false,
                    height    = BTN_H,
                    idxL      = idxL,
                    idxR      = idxR
                });
            }
            return rows;
        }

        private void DrawOptRow(SpriteBatch sb, int cx, int ry, OptRow row, Point mp)
        {
            if (row.isSection)
            {
                // Label de seccion centrado, sin boton
                DrawTextCentered(sb, row.sectionLabel, cx, ry + (SEC_H - 12) / 2,
                                 SEC_COLOR, SEC_SHADOW);
                // Linea horizontal tenue a cada lado
                int tw  = (int)_font.MeasureString(row.sectionLabel).X;
                int lx1 = cx - MENU_W/2;
                int lx2 = cx + tw/2 + 8;
                int lw1 = cx - tw/2 - 8 - lx1;
                int lw2 = cx + MENU_W/2 - lx2;
                int ly  = ry + SEC_H / 2;
                FillRect(sb, lx1, ly, lw1, 1, new Color(80, 80, 80) * 0.5f);
                FillRect(sb, lx2, ly, lw2, 1, new Color(80, 80, 80) * 0.5f);
                return;
            }

            int colW  = (MENU_W - BTN_GAP) / 2;
            bool two  = row.idxR != -1;
            int  btnW = two ? colW : MENU_W;

            // Columna izquierda
            if (row.idxL >= 0)
            {
                int bx = cx - MENU_W/2;
                DrawOptBtn(sb, bx, ry, btnW, row.idxL, mp);
            }

            // Columna derecha
            if (row.idxR >= 0)
            {
                int bx = cx - MENU_W/2 + colW + BTN_GAP;
                DrawOptBtn(sb, bx, ry, colW, row.idxR, mp);
            }
        }

        private void DrawOptBtn(SpriteBatch sb, int bx, int by, int bw, int optIdx, Point mp)
        {
            var o = _options[optIdx];

            if (o is BoolToggle bt)
            {
                // Boton unico: "Label: ON/OFF"
                string txt = o.Label + ": " + o.ValueString;
                var r = new Rectangle(bx, by, bw, BTN_H);
                bool hov = r.Contains(mp);
                DrawButton(sb, r, txt, hov);
                _optRects.Add((r, optIdx, 0));
            }
            else
            {
                // Slider: [<] [    Label: valor    ] [>]
                // < y > son botones separados
                var rL   = new Rectangle(bx,                by, ARROW_W, BTN_H);
                var rMid = new Rectangle(bx + ARROW_W + 1,  by, bw - ARROW_W * 2 - 2, BTN_H);
                var rR   = new Rectangle(bx + bw - ARROW_W, by, ARROW_W, BTN_H);

                bool hovL   = rL.Contains(mp);
                bool hovMid = rMid.Contains(mp);
                bool hovR   = rR.Contains(mp);

                DrawButton(sb, rL,   "<", hovL);
                DrawButton(sb, rMid, o.Label + ": " + o.ValueString, hovMid);
                DrawButton(sb, rR,   ">", hovR);

                _optRects.Add((rL,   optIdx, -1));
                _optRects.Add((rMid, optIdx,  0));
                _optRects.Add((rR,   optIdx,  1));
            }
        }

        // ============================================================
        //  PRIMITIVAS
        // ============================================================

        private void DrawButton(SpriteBatch sb, Rectangle r, string text, bool hovered)
        {
            Color face   = hovered ? BTN_HOVER : BTN_FACE;
            Color txSh   = hovered ? TXT_SHADOW_H : TXT_SHADOW;

            // Fondo
            FillRect(sb, r.X, r.Y, r.Width, r.Height, face);

            // Bisel
            FillRect(sb, r.X,             r.Y,              r.Width, BEV,     BTN_LIGHT * 0.5f);
            FillRect(sb, r.X,             r.Y,              BEV,     r.Height, BTN_LIGHT * 0.5f);
            FillRect(sb, r.X,             r.Y + r.Height-1, r.Width, BEV,     BTN_DARK  * 0.6f);
            FillRect(sb, r.X + r.Width-1, r.Y,              BEV,     r.Height, BTN_DARK  * 0.6f);

            // Texto centrado
            int tw = (int)_font.MeasureString(text).X;
            int th = (int)_font.MeasureString(text).Y;
            int tx = r.X + (r.Width  - tw) / 2;
            int ty = r.Y + (r.Height - th) / 2;
            sb.DrawString(_font, text, new Vector2(tx + 1, ty + 1), txSh);
            sb.DrawString(_font, text, new Vector2(tx,     ty),     TXT_WHITE);
        }

        private void DrawTextCentered(SpriteBatch sb, string text, int cx, int y,
                                      Color color, Color shadow)
        {
            int tw = (int)_font.MeasureString(text).X;
            int tx = cx - tw / 2;
            sb.DrawString(_font, text, new Vector2(tx + 1, y + 1), shadow);
            sb.DrawString(_font, text, new Vector2(tx,     y),     color);
        }

        private void FillRect(SpriteBatch sb, int x, int y, int w, int h, Color c)
            => sb.Draw(_pixel, new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h)), c);
    }

    // ================================================================
    //  AJUSTES GLOBALES
    // ================================================================

    public class GameSettings
    {
        public int   LoadDistance       { get; set; } = 11;
        public bool  EnableVeryLowPoly  { get; set; } = true;
        public bool  FogEnabled         { get; set; } = false;
        public float FovDegrees         { get; set; } = 80f;
        public bool  WireframeMode      { get; set; } = false;
        public bool  DirectionalLight   { get; set; } = true;
        public float AmbientLight       { get; set; } = 0.5f;
        public float AoStrength         { get; set; } = 1.0f;
        public float MoveSpeed          { get; set; } = 30f;
        public float MouseSensitivity   { get; set; } = 0.002f;
        public bool  ShowDebugHud       { get; set; } = false;
    }

    // ================================================================
    //  TIPOS DE ENTRADA
    // ================================================================

    internal interface IInteractable { void Increase(); void Decrease(); }

    internal abstract class OptionEntry
    {
        public string Label                   { get; protected set; }
        public virtual string ValueString     => "";
        public virtual float  NormalizedValue => 0f;
    }

    internal class SectionEntry : OptionEntry
    {
        public SectionEntry(string label) { Label = label; }
    }

    internal class BoolToggle : OptionEntry, IInteractable
    {
        private readonly Func<bool>   _get;
        private readonly Action<bool> _set;
        public bool Value                     => _get();
        public override string ValueString    => _get() ? "ON" : "OFF";
        public override float  NormalizedValue => _get() ? 1f : 0f;
        public BoolToggle(string label, Func<bool> get, Action<bool> set)
            { Label = label; _get = get; _set = set; }
        public void DoToggle() => _set(!_get());
        public void Increase() => _set(true);
        public void Decrease() => _set(false);
    }

    internal class FloatSlider : OptionEntry, IInteractable
    {
        private readonly Func<float>        _get;
        private readonly Action<float>      _set;
        private readonly float              _min, _max, _step;
        private readonly Func<float,string> _fmt;
        public override string ValueString    => _fmt(_get());
        public override float  NormalizedValue => Math.Clamp((_get()-_min)/(_max-_min+0.0001f), 0f, 1f);
        public FloatSlider(string label, Func<float> get, Action<float> set,
                           float min, float max, float step, Func<float,string> fmt = null)
            { Label=label; _get=get; _set=set; _min=min; _max=max; _step=step; _fmt=fmt??(v=>$"{v:F1}"); }
        public void Increase() => _set(Math.Clamp(_get()+_step, _min, _max));
        public void Decrease() => _set(Math.Clamp(_get()-_step, _min, _max));
    }

    internal class IntSlider : OptionEntry, IInteractable
    {
        private readonly Func<int>   _get;
        private readonly Action<int> _set;
        private readonly int         _min, _max, _step;
        public override string ValueString    => _get().ToString();
        public override float  NormalizedValue => Math.Clamp((_get()-_min)/(float)(_max-_min+1), 0f, 1f);
        public IntSlider(string label, Func<int> get, Action<int> set, int min, int max, int step=1)
            { Label=label; _get=get; _set=set; _min=min; _max=max; _step=step; }
        public void Increase() => _set(Math.Clamp(_get()+_step, _min, _max));
        public void Decrease() => _set(Math.Clamp(_get()-_step, _min, _max));
    }
}