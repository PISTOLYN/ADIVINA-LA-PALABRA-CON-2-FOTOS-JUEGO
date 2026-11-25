using NAudio.Wave;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdivinaLaPalabra;

public sealed class MainForm : Form
{
    sealed class Scene { public string Answer { get; set; } = ""; public string Image1 { get; set; } = ""; public string Image2 { get; set; } = ""; public string Background { get; set; } = ""; }
    sealed class SavedConfig { public string[] Audios { get; set; } = new string[4]; public string Logo { get; set; } = ""; public List<Scene> Scenes { get; set; } = new(); public string RedBuzzerKey { get; set; } = "Q"; public string BlueBuzzerKey { get; set; } = "P"; public string ResetKey { get; set; } = "R"; }
    sealed class BufferedPanel : Panel { public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); } }

    readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesafiosLocos", "AdivinaLaPalabra");
    readonly List<Scene> scenes = new();
    readonly string?[] audios = new string?[4];
    readonly Color orange = Color.FromArgb(239, 57, 8);
    const string DefaultTitle = "ADIVINA LA PALABRA CON 2 FOTOS";
    readonly BufferedPanel stage = new();
    readonly Panel startScreen = new(), winnerScreen = new();
    readonly System.Windows.Forms.Timer ledAnimation = new() { Interval = 80 };
    string ledMessage = DefaultTitle; string? activeTeam; int ledPhase;
    readonly System.Windows.Forms.Timer countdown = new() { Interval = 100 };
    readonly SpeechSynthesizer speech = new();
    WaveOutEvent? player;
    AudioFileReader? audio;
    Bitmap? sceneBackground, defaultBackground, ledArt, barArt, counterRedArt, counterBlueArt, frameRedArt, frameBlueArt, plusArt, logoArt, photoA, photoB;
    string answerText = "";
    string? winner, logoPath;
    Keys redBuzzerKey = Keys.Q, blueBuzzerKey = Keys.P, resetKey = Keys.R;
    int seconds = 10, redPoints, bluePoints, currentScene, timerDeciseconds;
    bool roundActive, buzzLocked = true, answerRevealed, settingsOpen, started, isFullscreen, capturingBuzzerKey;
    FormBorderStyle oldBorder;
    Rectangle oldBounds;

    public MainForm()
    {
        Directory.CreateDirectory(dataDir);
        Text = "Adivina la Palabra con 2 Fotos - Desafíos Locos";
        BackColor = orange; ForeColor = Color.White; WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 680); KeyPreview = true; DoubleBuffered = true;
        LoadConfig();
        // Si la configuración pertenece a una versión anterior y no contiene fotos accesibles,
        // vuelve a cargar las parejas incluidas con el juego. Conserva las escenas personalizadas
        // cuando sus dos fotografías siguen disponibles.
        if (!scenes.Any(x => File.Exists(ResolveAssetPath(x.Image1)) && File.Exists(ResolveAssetPath(x.Image2))))
        {
            scenes.Clear();
            LoadBundledScenes();
        }
        if (scenes.Count == 0) scenes.Add(new Scene { Answer = "AGUAFIESTAS" });
        BuildInterface(); ShowScene(0, false);
        ledAnimation.Tick += (_, _) => { ledPhase = (ledPhase + 1) % 24; stage.Invalidate(); }; ledAnimation.Start();
        countdown.Tick += (_, _) => { timerDeciseconds = Math.Min(100, timerDeciseconds + 1); seconds = Math.Max(0, 10 - timerDeciseconds / 10); if (roundActive && activeTeam == null) ledMessage = "TIEMPO " + seconds.ToString("00") + "  ·  " + redBuzzerKey + " ROJO | " + blueBuzzerKey + " AZUL"; UpdateClock(); if (timerDeciseconds >= 100) { countdown.Stop(); roundActive = false; buzzLocked = true; SetStatus("¡TIEMPO! SE ACABÓ LA RONDA", null); } };
        ShowStartScreen();
    }


    // ============ PANTALLA DE JUEGO ============
    // Todo se dibuja sobre un lienzo de diseño de 1427 x 923 px que se escala a cualquier tamaño de ventana,
    // para que se vea siempre igual que la maqueta (marcador, respuesta, marcos con focos y barra de botones).
    const float DW = 1427f, DH = 923f;
    static readonly RectangleF LedRect = new(28, 18, 1377, 74);
    static readonly RectangleF RedCounterRect = new(48, 95, 265, 158);
    static readonly RectangleF BlueCounterRect = new(1126, 94, 260, 153);
    static readonly RectangleF AnswerBand = new(455, 132, 548, 90);
    static readonly RectangleF LeftLogoRect = new(332, 110, 130, 138);
    static readonly RectangleF RightLogoRect = new(997, 104, 130, 138);
    static readonly RectangleF RedFrameRect = new(25, 255, 650, 580);
    static readonly RectangleF BlueFrameRect = new(765, 255, 650, 580);
    static readonly RectangleF PlusRect = new(680, 474, 88, 88);
    static readonly RectangleF BarRect = new(28, 833, 1377, 84);
    // Ventana transparente interior de los marcos (proporción dentro de la imagen recortada).
    const float WinX = 0.1019f, WinY = 0.1668f, WinW = 0.7962f, WinH = 0.6714f;

    sealed record Hit(float L, float R, Action Act);
    Hit[] barHits = Array.Empty<Hit>();

    void BuildInterface()
    {
        Bitmap? A(string f) => LoadBitmap(Path.Combine(AppContext.BaseDirectory, f));
        defaultBackground = A("FODNO 4.jpg") ?? A("FONDO 4.jpg");
        ledArt = A("PANTALLA LEDS.jpg"); barArt = A("BARRA DE BOTONES.jpg");
        counterRedArt = A("CONTADOR ROJO.png"); counterBlueArt = A("CONTADOR AZUL.png");
        frameRedArt = A("MARCO ROJO PNG.png"); frameBlueArt = A("MARCO AZUL PNG.png"); plusArt = A("SIGNO MAS PNG.png");
        SetImageLogo();

        stage.Dock = DockStyle.Fill; stage.BackColor = orange;
        stage.Paint += (_, e) => PaintStage(e.Graphics);
        stage.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) ClickStage(e.Location); };
        stage.MouseMove += (_, e) => stage.Cursor = HitAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
        Controls.Add(stage);

        // Zonas de clic sobre cada botón dibujado en "BARRA DE BOTONES.jpg" (fracciones del ancho de la barra).
        barHits = new[]
        {
            new Hit(.020f, .082f, ExitGame),
            new Hit(.088f, .172f, Instructions),
            new Hit(.178f, .237f, () => ShowWinner("red")),
            new Hit(.243f, .302f, () => ShowWinner("blue")),
            new Hit(.308f, .373f, ResetBuzzers),
            new Hit(.380f, .447f, () => PlayCelebration(0)),
            new Hit(.453f, .520f, () => PlayCelebration(1)),
            new Hit(.527f, .592f, () => PlayCelebration(2)),
            new Hit(.598f, .665f, () => PlayCelebration(3)),
            new Hit(.672f, .727f, StopAudio),
            new Hit(.733f, .789f, RevealAnswer),
            new Hit(.796f, .851f, () => AddPoint("red")),
            new Hit(.857f, .910f, () => AddPoint("blue")),
            new Hit(.915f, .950f, PreviousScene),
            new Hit(.953f, .985f, NextScene),
        };

        var fullscreen = new Button { Text = "⛶", Width = 54, Height = 32, BackColor = Color.LimeGreen, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font("Arial Black", 15) };
        fullscreen.Location = new Point(ClientSize.Width - 68, 3); fullscreen.Click += (_, _) => ToggleFullscreen(); Controls.Add(fullscreen); fullscreen.BringToFront();
    }

    PointF ToDesign(Point p)
    {
        var cr = stage.ClientRectangle; float s = Math.Min(cr.Width / DW, cr.Height / DH);
        return new PointF((p.X - (cr.Width - DW * s) / 2f) / s, (p.Y - (cr.Height - DH * s) / 2f) / s);
    }
    Hit? HitAt(Point p)
    {
        var d = ToDesign(p);
        if (!BarRect.Contains(d)) return null;
        float f = (d.X - BarRect.X) / BarRect.Width;
        return barHits.FirstOrDefault(h => f >= h.L && f <= h.R);
    }
    void ClickStage(Point p) => HitAt(p)?.Act();

    void PaintStage(Graphics g)
    {
        var cr = stage.ClientRectangle; if (cr.Width <= 0 || cr.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var bg = sceneBackground ?? defaultBackground;
        if (bg != null) g.DrawImage(bg, cr); else g.Clear(orange);
        float s = Math.Min(cr.Width / DW, cr.Height / DH);
        g.TranslateTransform((cr.Width - DW * s) / 2f, (cr.Height - DH * s) / 2f); g.ScaleTransform(s, s);

        // Marcador luminoso superior
        if (ledArt != null) g.DrawImage(ledArt, LedRect);
        var st = g.Save(); g.TranslateTransform(LedRect.X, LedRect.Y); DrawMarquee(g, new Rectangle(0, 0, (int)LedRect.Width, (int)LedRect.Height)); g.Restore(st);

        // Contadores de equipo
        if (counterRedArt != null) g.DrawImage(counterRedArt, RedCounterRect);
        if (counterBlueArt != null) g.DrawImage(counterBlueArt, BlueCounterRect);
        DrawScore(g, RedCounterRect, redPoints); DrawScore(g, BlueCounterRect, bluePoints);

        // Respuesta, con el logotipo a cada lado
        DrawOutlined(g, "RESPUESTA", 31, new RectangleF(AnswerBand.X, 96, AnswerBand.Width, 38), Brushes.White, Color.Black, 5f);
        using (var band = new SolidBrush(Color.FromArgb(244, 244, 244))) g.FillRectangle(band, AnswerBand);
        DrawAnswer(g);
        DrawZoom(g, logoArt, LeftLogoRect); DrawZoom(g, logoArt, RightLogoRect);

        // Fotos (debajo) y marcos con focos (encima)
        DrawCover(g, photoA, WindowOf(RedFrameRect)); DrawCover(g, photoB, WindowOf(BlueFrameRect));
        if (frameRedArt != null) g.DrawImage(frameRedArt, RedFrameRect);
        if (frameBlueArt != null) g.DrawImage(frameBlueArt, BlueFrameRect);
        if (plusArt != null) g.DrawImage(plusArt, PlusRect); else DrawOutlined(g, "+", 80, PlusRect, Brushes.Gold, Color.Black, 4f);

        // Resplandor del equipo que ganó el pulsador
        if (activeTeam != null)
        {
            int pulse = 110 + (int)(Math.Abs(Math.Sin(ledPhase * Math.PI / 12)) * 140);
            bool red = activeTeam == "red";
            using var pen = new Pen(red ? Color.FromArgb(pulse, 255, 40, 20) : Color.FromArgb(pulse, 25, 140, 255), 8) { LineJoin = LineJoin.Round };
            var fr = red ? RedFrameRect : BlueFrameRect; fr.Inflate(-4, -4); g.DrawRectangle(pen, fr.X, fr.Y, fr.Width, fr.Height);
            var cn = red ? RedCounterRect : BlueCounterRect; cn.Inflate(-2, -2); g.DrawRectangle(pen, cn.X, cn.Y, cn.Width, cn.Height);
        }

        // Barra de botones
        if (barArt != null) g.DrawImage(barArt, BarRect);
    }

    static RectangleF WindowOf(RectangleF f)
    {
        var r = new RectangleF(f.X + f.Width * WinX, f.Y + f.Height * WinY, f.Width * WinW, f.Height * WinH);
        r.Inflate(3, 3); return r;   // un poco más grande para que el marco lo tape sin dejar rendijas
    }
    static void DrawCover(Graphics g, Image? img, RectangleF dest)
    {
        if (img == null) return;
        float sc = Math.Max(dest.Width / img.Width, dest.Height / img.Height);
        float sw = dest.Width / sc, sh = dest.Height / sc;
        var src = new RectangleF((img.Width - sw) / 2f, (img.Height - sh) / 2f, sw, sh);
        using var ia = new ImageAttributes(); ia.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(img, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia);
    }
    static void DrawZoom(Graphics g, Image? img, RectangleF box)
    {
        if (img == null) return;
        float sc = Math.Min(box.Width / img.Width, box.Height / img.Height);
        float w = img.Width * sc, h = img.Height * sc;
        g.DrawImage(img, box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);
    }
    static void DrawOutlined(Graphics g, string text, float px, RectangleF r, Brush fill, Color outline, float width)
    {
        using var font = new Font("Arial Black", px, FontStyle.Regular, GraphicsUnit.Pixel);
        using var path = new GraphicsPath(); using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        path.AddString(text, font.FontFamily, (int)font.Style, px, r, sf);
        using var pen = new Pen(outline, width) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path); g.FillPath(fill, path);
    }
    void DrawScore(Graphics g, RectangleF counter, int points)
    {
        var box = new RectangleF(counter.X + counter.Width * 0.56f, counter.Y + counter.Height * 0.36f, counter.Width * 0.38f, counter.Height * 0.50f);
        using var font = new Font("Arial Black", 66, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.DrawString(points.ToString(), font, shadow, new RectangleF(box.X + 3, box.Y + 3, box.Width, box.Height), sf);
        g.DrawString(points.ToString(), font, Brushes.WhiteSmoke, box, sf);
    }
    void DrawAnswer(Graphics g)
    {
        string text = answerRevealed ? answerText : "¿ ? ¿ ?";
        float px = 84;
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        Font font = new("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel);
        while (px > 20 && g.MeasureString(text, font, 10000, sf).Width > AnswerBand.Width - 24) { font.Dispose(); px -= 4; font = new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel); }
        using (font) using (var red = new SolidBrush(Color.FromArgb(240, 0, 0))) g.DrawString(text, font, red, AnswerBand, sf);
    }

    void SetStatus(string text, string? team) { activeTeam = team; ledMessage = team == "red" ? "TURNO PARA EL EQUIPO ROJO" : team == "blue" ? "TURNO PARA EL EQUIPO AZUL" : text; ledPhase = 0; stage.Invalidate(); }
    void Scores() { stage.Invalidate(); }
    void UpdateClock() { stage.Invalidate(); }

    void DrawMarquee(Graphics g, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (activeTeam != null)
        {
            // Focos recorriendo el contorno completo, en el color del equipo que ganó el pulsador.
            int n = 64, inset = 13;
            for (int i = 0; i < n; i++)
            {
                double t = ((i - ledPhase + n) % n) / (double)n * 4.0;
                float x, y;
                if (t < 1) { x = inset + (bounds.Width - inset * 2) * (float)t; y = inset; }
                else if (t < 2) { x = bounds.Width - inset; y = inset + (bounds.Height - inset * 2) * (float)(t - 1); }
                else if (t < 3) { x = bounds.Width - inset - (bounds.Width - inset * 2) * (float)(t - 2); y = bounds.Height - inset; }
                else { x = inset; y = bounds.Height - inset - (bounds.Height - inset * 2) * (float)(t - 3); }
                int d = (i - ledPhase + n) % n; int alpha = d < 8 ? 245 - d * 25 : 55;
                using var bulb = new SolidBrush(activeTeam == "red" ? Color.FromArgb(alpha, 255, 35, 20) : Color.FromArgb(alpha, 20, 145, 255));
                g.FillEllipse(bulb, x - 5, y - 5, 10, 10);
            }
        }
        using var font = new Font("Consolas", bounds.Height * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var rect = new RectangleF(10, 6, bounds.Width - 20, bounds.Height - 12);
        using (var glow = new SolidBrush(Color.FromArgb(60, 255, 90, 40)))
            foreach (var (dx, dy) in new[] { (-2f, 0f), (2f, 0f), (0f, -2f), (0f, 2f) })
                g.DrawString(ledMessage, font, glow, new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height), sf);
        using var brush = new SolidBrush(Color.FromArgb(255, 255, 80, 40));
        g.DrawString(ledMessage, font, brush, rect, sf);
    }

    // ---------- Carga de imágenes ----------
    static Bitmap? LoadBitmap(string path)
    {
        try { if (!File.Exists(path)) return null; using var img = Image.FromFile(path); return new Bitmap(img); } catch { return null; }
    }
    // Carga una foto de escena: recorta los márgenes transparentes y la reduce para dibujarla rápido.
    static Bitmap? LoadPhoto(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var src = Image.FromFile(path);
            using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g0 = Graphics.FromImage(bmp)) g0.DrawImage(src, 0, 0, src.Width, src.Height);
            var box = OpaqueBounds(bmp);
            float k = Math.Min(1f, 1200f / Math.Max(box.Width, box.Height));
            var result = new Bitmap(Math.Max(1, (int)(box.Width * k)), Math.Max(1, (int)(box.Height * k)), PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(result); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, new Rectangle(0, 0, result.Width, result.Height), box, GraphicsUnit.Pixel);
            return result;
        }
        catch { return null; }
    }
    static Rectangle OpaqueBounds(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride; var buf = new byte[Math.Abs(stride) * h]; Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            bool RowHas(int y) { int o = y * stride; for (int x = 0; x < w; x++) if (buf[o + x * 4 + 3] > 24) return true; return false; }
            bool ColHas(int x, int t, int b) { for (int y = t; y <= b; y++) if (buf[y * stride + x * 4 + 3] > 24) return true; return false; }
            int top = 0; while (top < h && !RowHas(top)) top++;
            if (top >= h) return new Rectangle(0, 0, w, h);
            int bot = h - 1; while (bot > top && !RowHas(bot)) bot--;
            int left = 0; while (left < w - 1 && !ColHas(left, top, bot)) left++;
            int right = w - 1; while (right > left && !ColHas(right, top, bot)) right--;
            return new Rectangle(left, top, right - left + 1, bot - top + 1);
        }
        finally { bmp.UnlockBits(data); }
    }

    void StartRound() { if (!started) return; countdown.Stop(); seconds = 10; timerDeciseconds = 0; roundActive = true; buzzLocked = false; UpdateClock(); SetStatus("¡LISTOS! " + redBuzzerKey + " = ROJO  |  " + blueBuzzerKey + " = AZUL", null); countdown.Start(); }
    void Buzz(string team) { if (!started || !roundActive || buzzLocked) return; buzzLocked = true; roundActive = false; countdown.Stop(); SetStatus(team == "red" ? "¡EQUIPO ROJO TIENE EL TURNO PARA RESPONDER!" : "¡EQUIPO AZUL TIENE EL TURNO PARA RESPONDER!", team); }
    void ResetBuzzers() { countdown.Stop(); seconds = 10; timerDeciseconds = 0; roundActive = false; buzzLocked = true; UpdateClock(); SetStatus(DefaultTitle, null); }
    void AddPoint(string team) { if (team == "red") redPoints++; else bluePoints++; Scores(); SetStatus(team == "red" ? "PUNTO PARA EQUIPO ROJO" : "PUNTO PARA EQUIPO AZUL", team); }
    void RevealAnswer() { answerRevealed = true; answerText = scenes[currentScene].Answer.ToLowerInvariant(); stage.Invalidate(); }
    void ShowScene(int index, bool startTimer)
    {
        if (scenes.Count == 0) return;
        currentScene = ((index % scenes.Count) + scenes.Count) % scenes.Count; var s = scenes[currentScene];
        Text = "DISEÑO v2 - Adivina la Palabra con 2 Fotos  ·  Escena " + (currentScene + 1) + " / " + scenes.Count;
        answerRevealed = false; answerText = s.Answer;
        var oldA = photoA; var oldB = photoB;
        photoA = LoadPhoto(ResolveAssetPath(s.Image1)); photoB = LoadPhoto(ResolveAssetPath(s.Image2));
        oldA?.Dispose(); oldB?.Dispose();
        SetSceneBackground(s.Background);
        ResetBuzzers(); if (startTimer && started) StartRound();
    }
    string ResolveAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (File.Exists(path)) return path;
        // Las escenas integradas guardan solo el nombre del archivo para que funcionen
        // aunque el programa se instale en otra carpeta o se actualice.
        return Path.Combine(AppContext.BaseDirectory, Path.GetFileName(path));
    }
    void SetSceneBackground(string path)
    {
        var old = sceneBackground; sceneBackground = null;
        var resolved = ResolveAssetPath(path);
        if (!string.IsNullOrWhiteSpace(resolved)) sceneBackground = LoadBitmap(resolved);
        old?.Dispose(); stage.Invalidate();
    }
    void PreviousScene() { if (scenes.Count > 0) ShowScene(currentScene - 1, true); }
    void NextScene() { if (scenes.Count > 0) ShowScene(currentScene + 1, true); }
    void ShowStartScreen()
    {
        startScreen.Dock = DockStyle.Fill; startScreen.BackColor = Color.FromArgb(235, 50, 8); startScreen.BringToFront();
        var title = new Label { Text = "ADIVINA LA PALABRA\nCON 2 FOTOS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 42), ForeColor = Color.White };
        var hint = new Label { Text = "PRESIONA ENTER PARA COMENZAR", Dock = DockStyle.Bottom, Height = 105, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 25), ForeColor = Color.Yellow };
        startScreen.Controls.Add(title); startScreen.Controls.Add(hint);
        var splashFullscreen = new Button { Text = "⛶", Width = 54, Height = 32, Left = ClientSize.Width - 66, Top = 5, Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.LimeGreen, ForeColor = Color.Black, Font = new Font("Arial Black", 15) };
        splashFullscreen.Click += (_, _) => ToggleFullscreen(); startScreen.Controls.Add(splashFullscreen); splashFullscreen.BringToFront();
        Controls.Add(startScreen); startScreen.BringToFront();
    }
    void BeginGame() { if (started) return; started = true; startScreen.Visible = false; ShowScene(currentScene, true); }
    void ShowWinner(string team)
    {
        countdown.Stop(); roundActive = false; buzzLocked = true; winner = team;
        winnerScreen.Controls.Clear(); winnerScreen.Dock = DockStyle.Fill; winnerScreen.BackColor = team == "red" ? Color.DarkRed : Color.DarkBlue;
        winnerScreen.Visible = true;
        var text = new Label { Text = team == "red" ? "¡GANÓ EL EQUIPO ROJO!" : "¡GANÓ EL EQUIPO AZUL!", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 48), ForeColor = Color.Gold };
        var close = new Button { Text = "VOLVER AL JUEGO", Dock = DockStyle.Bottom, Height = 75, Font = new Font("Arial Black", 18) }; close.Click += (_, _) => winnerScreen.Visible = false;
        winnerScreen.Controls.Add(text); winnerScreen.Controls.Add(close); Controls.Add(winnerScreen); winnerScreen.BringToFront();
    }
    void ToggleFullscreen()
    {
        if (!isFullscreen) { oldBorder = FormBorderStyle; oldBounds = Bounds; FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized; isFullscreen = true; }
        else { FormBorderStyle = oldBorder; WindowState = FormWindowState.Normal; Bounds = oldBounds; WindowState = FormWindowState.Maximized; isFullscreen = false; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (capturingBuzzerKey) return base.ProcessCmdKey(ref msg, key);
        if (key == Keys.M) { OpenSettings(); return true; }
        if (key == resetKey) { ResetBuzzers(); return true; }
        if (key == redBuzzerKey) { Buzz("red"); return true; }
        if (key == blueBuzzerKey) { Buzz("blue"); return true; }
        if (key == Keys.Enter) { if (!started) BeginGame(); else if (!roundActive && buzzLocked) StartRound(); return true; }
        if (key == Keys.Escape) { if (winnerScreen.Visible) winnerScreen.Visible = false; else if (isFullscreen) ToggleFullscreen(); else ResetBuzzers(); return true; }
        if (key == Keys.Left) { PreviousScene(); return true; }
        if (key == Keys.Right) { NextScene(); return true; }
        return base.ProcessCmdKey(ref msg, key);
    }

    void OpenSettings()
    {
        if (settingsOpen) return; settingsOpen = true;
        using var f = new Form { Text = "MENÚ DE CONFIGURACIÓN - DESAFÍOS LOCOS", StartPosition = FormStartPosition.CenterParent, Size = new Size(700, 690), BackColor = Color.FromArgb(28, 28, 36), ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var musicTab = new TabPage("MÚSICA") { BackColor = f.BackColor, ForeColor = Color.White };
        var logoTab = new TabPage("LOGOTIPO") { BackColor = f.BackColor, ForeColor = Color.White };
        var scenesTab = new TabPage("ESCENAS") { BackColor = f.BackColor, ForeColor = Color.White };
        var buzzersTab = new TabPage("PULSADORES") { BackColor = f.BackColor, ForeColor = Color.White };
        tabs.TabPages.AddRange(new[] { musicTab, logoTab, scenesTab, buzzersTab }); f.Controls.Add(tabs);
        var musicPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), WrapContents = false };
        musicTab.Controls.Add(musicPanel);
        musicPanel.Controls.Add(new Label { Text = "ASIGNA UNA CANCIÓN DIFERENTE A CADA BOTÓN VERDE", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 12), Margin = new Padding(4, 10, 4, 18) });
        for (int i = 0; i < 4; i++) { int n = i; var b = new Button { Text = "CARGAR MP3 PARA CELEBRACIÓN " + (i + 1), Width = 590, Height = 54, BackColor = Color.LawnGreen, ForeColor = Color.Black, Font = new Font("Arial Black", 10) }; b.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Archivos MP3|*.mp3" }; if (d.ShowDialog(f) == DialogResult.OK) { audios[n] = CopyToData(d.FileName, "celebracion" + (n + 1) + Path.GetExtension(d.FileName)); b.Text = "CELEBRACIÓN " + (n + 1) + " · " + Path.GetFileName(d.FileName); } }; musicPanel.Controls.Add(b); }
        var logoPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20) }; logoTab.Controls.Add(logoPanel);
        logoPanel.Controls.Add(new Label { Text = "CAMBIAR LOGOTIPO DE DESAFÍOS LOCOS (PNG)", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 13) });
        var logoButton = new Button { Text = "SELECCIONAR LOGOTIPO PNG", Width = 590, Height = 55 }; logoButton.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Imagen PNG|*.png" }; if (d.ShowDialog(f) == DialogResult.OK) { logoPath = CopyToData(d.FileName, "logo-desafios-locos.png"); SetImageLogo(); logoButton.Text = "LOGOTIPO SELECCIONADO: " + Path.GetFileName(d.FileName); } }; logoPanel.Controls.Add(logoButton);
        var scenePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18), AutoScroll = true, WrapContents = false }; scenesTab.Controls.Add(scenePanel);
        scenePanel.Controls.Add(new Label { Text = "ADMINISTRAR FONDOS, FOTOGRAFÍAS Y RESPUESTAS", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 12) });
        for (int i = 0; i < scenes.Count; i++) { int n = i; var b = new Button { Text = "EDITAR ESCENA " + (i + 1), Width = 590, Height = 42 }; b.Click += (_, _) => EditScene(n, f); scenePanel.Controls.Add(b); }
        var add = new Button { Text = "+ AGREGAR NUEVA ESCENA", Width = 590, Height = 48, BackColor = Color.LimeGreen }; add.Click += (_, _) => { scenes.Add(new Scene { Answer = "Nueva respuesta" }); f.Close(); ShowScene(scenes.Count - 1, false); OpenSettings(); }; scenePanel.Controls.Add(add);
        var buzzerPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(22), WrapContents = false, AutoScroll = true };
        buzzersTab.Controls.Add(buzzerPanel);
        buzzerPanel.Controls.Add(new Label { Text = "CONFIGURA QUÉ TECLA UTILIZA CADA EQUIPO", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 13), Margin = new Padding(3, 8, 3, 18) });
        var redKeyBox = new TextBox { ReadOnly = true, Text = redBuzzerKey.ToString(), Width = 90, TextAlign = HorizontalAlignment.Center, Font = new Font("Arial Black", 18), BackColor = Color.White };
        var blueKeyBox = new TextBox { ReadOnly = true, Text = blueBuzzerKey.ToString(), Width = 90, TextAlign = HorizontalAlignment.Center, Font = new Font("Arial Black", 18), BackColor = Color.White };
        KeyEventHandler? redCaptureHandler = null, blueCaptureHandler = null;
        void CaptureKey(TextBox target, bool isRed)
        {
            target.Text = "PRESIONA...";
            capturingBuzzerKey = true;
            target.Focus();
            if (isRed && redCaptureHandler != null) target.KeyDown -= redCaptureHandler;
            if (!isRed && blueCaptureHandler != null) target.KeyDown -= blueCaptureHandler;
            KeyEventHandler handler = (_, e) =>
            {
                var k = e.KeyCode;
                bool valid = (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9);
                if (!valid || k == Keys.M) { target.Text = "NO VÁLIDA"; e.SuppressKeyPress = true; return; }
                var other = isRed ? blueBuzzerKey : redBuzzerKey;
                if (k == other) { target.Text = "YA ASIGNADA"; e.SuppressKeyPress = true; return; }
                if (isRed) redBuzzerKey = k; else blueBuzzerKey = k;
                target.Text = k.ToString(); e.SuppressKeyPress = true; capturingBuzzerKey = false;
            };
            if (isRed) redCaptureHandler = handler; else blueCaptureHandler = handler;
            target.KeyDown += handler;
        }
        var redKeyButton = new Button { Text = "ELEGIR TECLA DEL EQUIPO ROJO", Width = 590, Height = 48, BackColor = Color.Firebrick, ForeColor = Color.White };
        redKeyButton.Click += (_, _) => CaptureKey(redKeyBox, true);
        var blueKeyButton = new Button { Text = "ELEGIR TECLA DEL EQUIPO AZUL", Width = 590, Height = 48, BackColor = Color.RoyalBlue, ForeColor = Color.White };
        blueKeyButton.Click += (_, _) => CaptureKey(blueKeyBox, false);
        buzzerPanel.Controls.Add(new Label { Text = "PULSADOR ROJO", AutoSize = true, ForeColor = Color.White }); buzzerPanel.Controls.Add(redKeyBox); buzzerPanel.Controls.Add(redKeyButton);
        buzzerPanel.Controls.Add(new Label { Text = "PULSADOR AZUL", AutoSize = true, ForeColor = Color.White, Margin = new Padding(3, 18, 3, 3) }); buzzerPanel.Controls.Add(blueKeyBox); buzzerPanel.Controls.Add(blueKeyButton);
        buzzerPanel.Controls.Add(new Label { Text = "Puedes asignar letras o números. No se permite repetir la misma tecla ni usar M (menú).", AutoSize = true, ForeColor = Color.LightGray, MaximumSize = new Size(590, 0), Margin = new Padding(3, 18, 3, 3) });
        var resetKeyBox = new TextBox { ReadOnly = true, Text = resetKey.ToString(), Width = 90, TextAlign = HorizontalAlignment.Center, Font = new Font("Arial Black", 18), BackColor = Color.White };
        var resetKeyButton = new Button { Text = "ELEGIR TECLA PARA REINICIAR PULSADORES", Width = 590, Height = 48, BackColor = Color.Purple, ForeColor = Color.White };
        resetKeyButton.Click += (_, _) => {
            resetKeyBox.Text = "PRESIONA..."; capturingBuzzerKey = true; resetKeyBox.Focus();
            KeyEventHandler handler = null!;
            handler = (_, e) => {
                var k = e.KeyCode;
                bool valid = (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9);
                if (!valid || k == Keys.M || k == Keys.Enter || k == redBuzzerKey || k == blueBuzzerKey) { resetKeyBox.Text = "NO DISPONIBLE"; e.SuppressKeyPress = true; return; }
                resetKey = k; resetKeyBox.Text = k.ToString(); capturingBuzzerKey = false; resetKeyBox.KeyDown -= handler; e.SuppressKeyPress = true;
            };
            resetKeyBox.KeyDown += handler;
        };
        buzzerPanel.Controls.Add(new Label { Text = "REINICIAR PULSADORES", AutoSize = true, ForeColor = Color.Gold, Margin = new Padding(3, 18, 3, 3) });
        buzzerPanel.Controls.Add(resetKeyBox); buzzerPanel.Controls.Add(resetKeyButton);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.FromArgb(18, 18, 24) };
        var saveAll = new Button { Text = "GUARDAR CAMBIOS", Width = 210, Height = 48, Left = 12, Top = 7, BackColor = Color.LimeGreen, ForeColor = Color.Black, Font = new Font("Arial Black", 11) };
        saveAll.Click += (_, _) => { if (SaveConfig()) MessageBox.Show(f, "¡Todos los cambios se guardaron correctamente!", "Desafíos Locos", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        footer.Controls.Add(saveAll);
        var leave = new Button { Text = "SALIR", Width = 150, Height = 48, Left = 230, Top = 7, BackColor = Color.Silver, ForeColor = Color.Black, Font = new Font("Arial Black", 11) };
        leave.Click += (_, _) => { f.Close(); };
        var closeGame = new Button { Text = "CERRAR JUEGO", Width = 190, Height = 48, Left = 390, Top = 7, BackColor = Color.Firebrick, ForeColor = Color.White, Font = new Font("Arial Black", 11) };
        closeGame.Click += (_, _) => { f.Close(); Close(); };
        footer.Controls.Add(leave); footer.Controls.Add(closeGame); f.Controls.Add(footer); footer.BringToFront();
        f.FormClosed += (_, _) => settingsOpen = false; f.ShowDialog(this);
    }
    void EditScene(int index, Form owner)
    {
        var s = scenes[index];
        using var f = new Form { Text = "Editar escena " + (index + 1), StartPosition = FormStartPosition.CenterParent, Size = new Size(560, 650), BackColor = Color.FromArgb(35, 35, 42) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(15), WrapContents = false, AutoScroll = true }; f.Controls.Add(panel);
        var answerBox = new TextBox { Text = s.Answer, Width = 450 };
        panel.Controls.Add(new Label { Text = "RESPUESTA (se mantiene oculta hasta presionar NOMBRE)", ForeColor = Color.White, AutoSize = true }); panel.Controls.Add(answerBox);
        var b1 = new Button { Text = "CAMBIAR FOTO 1", Width = 450, Height = 42 }; b1.Click += (_, _) => { var path = PickImage(); if (path != null) s.Image1 = CopyToData(path, "escena" + (index + 1) + "_foto1" + Path.GetExtension(path)); }; panel.Controls.Add(b1);
        var b2 = new Button { Text = "CAMBIAR FOTO 2", Width = 450, Height = 42 }; b2.Click += (_, _) => { var path = PickImage(); if (path != null) s.Image2 = CopyToData(path, "escena" + (index + 1) + "_foto2" + Path.GetExtension(path)); }; panel.Controls.Add(b2);
        var bg = new Button { Text = "CAMBIAR FONDO DE ESTA ESCENA", Width = 450, Height = 42, BackColor = Color.Goldenrod, ForeColor = Color.Black }; bg.Click += (_, _) => { var path = PickImage(); if (path != null) s.Background = CopyToData(path, "escena" + (index + 1) + "_fondo" + Path.GetExtension(path)); }; panel.Controls.Add(bg);
        panel.Controls.Add(new Label { Text = "O ELIGE UNO DE LOS 6 FONDOS QUE SUBISTE:", ForeColor = Color.Gold, AutoSize = true, Margin = new Padding(3, 12, 3, 4) });
        string[] builtInBackgrounds = { "FONDO 1.jpg", "FONDO 2.jpg", "FONDO 3.jpg", "FODNO 4.jpg", "FONDO 5.jpg", "FONDO 6.jpg" };
        foreach (var file in builtInBackgrounds)
        {
            string selectedFile = file;
            var choose = new Button { Text = "USAR " + file, Width = 450, Height = 36, BackColor = Color.FromArgb(65, 65, 78), ForeColor = Color.White };
            choose.Click += (_, _) => { var source = Path.Combine(AppContext.BaseDirectory, selectedFile); if (File.Exists(source)) { s.Background = source; if (index == currentScene) SetSceneBackground(source); } else MessageBox.Show(f, "No se encontró " + selectedFile + " junto al programa. Vuelve a compilar la aplicación.", "Fondo no disponible"); };
            panel.Controls.Add(choose);
        }
        var save = new Button { Text = "GUARDAR ESCENA", Width = 450, Height = 44, BackColor = Color.LimeGreen }; save.Click += (_, _) => { s.Answer = answerBox.Text; if (index == currentScene) ShowScene(index, false); f.Close(); }; panel.Controls.Add(save);
        f.ShowDialog(owner);
    }
    string? PickImage() { using var d = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp" }; return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; }
    string CopyToData(string source, string name) { var dest = Path.Combine(dataDir, name); File.Copy(source, dest, true); return dest; }
    void SetImageLogo() { var old = logoArt; logoArt = null; if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath)) logoArt = LoadBitmap(logoPath); old?.Dispose(); stage.Invalidate(); }
    void LoadBundledScenes()
    {
        // Las imágenes se distribuyen junto al ejecutable. Cada pareja comparte respuesta;
        // cuando existe un número repetido, se agrupan por ese número. Las fotos sueltas
        // (como SALTAMONTES, archivos 29 y 30) se emparejan por la respuesta común.
        var files = Directory.EnumerateFiles(AppContext.BaseDirectory)
            .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^\d+\s+", RegexOptions.IgnoreCase))
            .Where(p => new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Select(p => new
            {
                Path = p,
                Name = Path.GetFileName(p),
                Number = int.Parse(Regex.Match(Path.GetFileName(p), @"^(\d+)\s+").Groups[1].Value)
            })
            .GroupBy(x => x.Number)
            .OrderBy(g => g.Key)
            .ToArray();

        string ReadAnswer(string name)
        {
            var match = Regex.Match(Path.GetFileNameWithoutExtension(name),
                @"(?:RESPUESTA|REPUETA|RESPUETA)\s*(.+?)(?:\s*[12])?$", RegexOptions.IgnoreCase);
            return match.Success
                ? Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ").ToUpperInvariant()
                : "";
        }

        int PhotoOrder(string name)
        {
            var match = Regex.Match(Path.GetFileNameWithoutExtension(name), @"([12])$");
            return match.Success ? int.Parse(match.Groups[1].Value) : 9;
        }

        var pending = new List<(int Number, string Answer, string First, string Second)>();
        var loosePhotos = new List<(int Number, string Name, string Path, string Answer)>();

        foreach (var group in files)
        {
            var pair = group.OrderBy(x => PhotoOrder(x.Name))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (pair.Length == 2)
            {
                var answer = ReadAnswer(pair[0].Name);
                if (!string.IsNullOrWhiteSpace(answer))
                    pending.Add((group.Key, answer, pair[0].Path, pair[1].Path));
            }
            else if (pair.Length == 1)
            {
                var answer = ReadAnswer(pair[0].Name);
                if (!string.IsNullOrWhiteSpace(answer))
                    loosePhotos.Add((group.Key, pair[0].Name, pair[0].Path, answer));
            }
        }

        foreach (var looseGroup in loosePhotos.GroupBy(x => x.Answer, StringComparer.OrdinalIgnoreCase))
        {
            var pair = looseGroup.OrderBy(x => PhotoOrder(x.Name))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (pair.Length == 2)
                pending.Add((pair.Min(x => x.Number), looseGroup.Key, pair[0].Path, pair[1].Path));
        }

        foreach (var scene in pending.OrderBy(x => x.Number))
        {
            scenes.Add(new Scene
            {
                Answer = scene.Answer,
                Image1 = Path.GetFileName(scene.First),
                Image2 = Path.GetFileName(scene.Second)
            });
        }
    }

    void LoadConfig()
    {
        try { var path = Path.Combine(dataDir, "config.json"); if (!File.Exists(path)) return; var c = JsonSerializer.Deserialize<SavedConfig>(File.ReadAllText(path)); if (c == null) return; logoPath = c.Logo; redBuzzerKey = Enum.TryParse<Keys>(c.RedBuzzerKey, out var rk) ? rk : Keys.Q; blueBuzzerKey = Enum.TryParse<Keys>(c.BlueBuzzerKey, out var bk) ? bk : Keys.P; resetKey = Enum.TryParse<Keys>(c.ResetKey, out var rr) ? rr : Keys.R; if (redBuzzerKey == blueBuzzerKey || redBuzzerKey == Keys.M || blueBuzzerKey == Keys.M || resetKey == redBuzzerKey || resetKey == blueBuzzerKey || resetKey == Keys.M || resetKey == Keys.Enter) { redBuzzerKey = Keys.Q; blueBuzzerKey = Keys.P; resetKey = Keys.R; } for (int i = 0; i < 4 && i < c.Audios.Length; i++) audios[i] = c.Audios[i]; scenes.Clear(); scenes.AddRange(c.Scenes); }
        catch { }
    }
    bool SaveConfig()
    {
        try { var c = new SavedConfig { Logo = logoPath ?? "", Audios = audios.Select(x => x ?? "").ToArray(), Scenes = scenes, RedBuzzerKey = redBuzzerKey.ToString(), BlueBuzzerKey = blueBuzzerKey.ToString(), ResetKey = resetKey.ToString() }; File.WriteAllText(Path.Combine(dataDir, "config.json"), JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true })); return true; } catch (Exception ex) { MessageBox.Show(this, "No se pudieron guardar los cambios: " + ex.Message, "Error al guardar", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }

    void PlayCelebration(int n)
    {
        if (string.IsNullOrWhiteSpace(audios[n]) || !File.Exists(audios[n])) { MessageBox.Show(this, "Carga el MP3 de esta celebración desde el menú M.", "Desafíos Locos"); return; }
        StopAudio(); try { audio = new AudioFileReader(audios[n]!); player = new WaveOutEvent(); player.Init(audio); player.Play(); } catch (Exception ex) { MessageBox.Show(this, "No se pudo reproducir el audio: " + ex.Message); }
    }
    void StopAudio() { try { player?.Stop(); } catch { } player?.Dispose(); player = null; audio?.Dispose(); audio = null; }
    void Instructions()
    {
        const string text = "Bienvenidos a Adivina la Palabra con dos Fotos. Presiona Enter para comenzar. El equipo rojo utiliza la tecla configurada en el menú y el equipo azul utiliza su tecla configurada. El primer equipo que presione su pulsador obtiene el turno para responder. El botón Nombre revela la respuesta. Las flechas cambian de escena. Los botones Punto Rojo y Punto Azul suman un punto. Los botones Gana Rojo y Gana Azul muestran al equipo ganador. Los cuatro botones verdes reproducen canciones de celebración. El botón Detener Canción detiene la música. La tecla M abre el menú de configuración.";
        try { speech.SpeakAsyncCancelAll(); speech.SpeakAsync(text); } catch { MessageBox.Show(this, text, "Instrucciones del juego"); }
    }
    void ExitGame() { if (MessageBox.Show(this, "¿Deseas salir del juego?", "Desafíos Locos", MessageBoxButtons.YesNo) == DialogResult.Yes) Close(); }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        countdown.Stop(); ledAnimation.Stop(); StopAudio();
        foreach (var b in new[] { sceneBackground, defaultBackground, ledArt, barArt, counterRedArt, counterBlueArt, frameRedArt, frameBlueArt, plusArt, logoArt, photoA, photoB }) b?.Dispose();
        speech.Dispose(); base.OnFormClosed(e);
    }
}