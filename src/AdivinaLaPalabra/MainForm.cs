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
    sealed class SavedConfig { public string[] Audios { get; set; } = new string[4]; public string Logo { get; set; } = ""; public List<Scene> Scenes { get; set; } = new(); public string RedBuzzerKey { get; set; } = "Q"; public string BlueBuzzerKey { get; set; } = "P"; public string ResetKey { get; set; } = "R"; public string SuspenseMusic { get; set; } = ""; public string CountdownEffect { get; set; } = ""; public string RoundStartEffect { get; set; } = ""; public string BuzzerEffect { get; set; } = ""; public string StartImage { get; set; } = ""; public string GameImage { get; set; } = ""; }
    sealed class BufferedPanel : Panel { public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); } }

    readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesafiosLocos", "AdivinaLaPalabra");
    readonly List<Scene> scenes = new();
    readonly string?[] audios = new string?[4];
    string? suspenseMusicPath, countdownEffectPath, roundStartEffectPath, buzzerEffectPath, startImagePath, gameImagePath;
    string defaultSuspensePath = "", defaultCountdownPath = "", defaultRoundStartPath = "", defaultBuzzerPath = "";
    WaveOutEvent? suspensePlayer, effectPlayer;
    AudioFileReader? suspenseReader, effectReader;
    bool musicLoopEnabled;
    int lastCountdownSoundSecond = 21;
    readonly Color orange = Color.FromArgb(239, 57, 8);
    const string DefaultTitle = "ADIVINA LA PALABRA CON 2 FOTOS";
    readonly BufferedPanel stage = new();
    readonly Panel startScreen = new(), winnerScreen = new();
    readonly System.Windows.Forms.Timer ledAnimation = new() { Interval = 80 };
    string ledMessage = DefaultTitle; string? activeTeam; int ledPhase, ledTick;
    readonly System.Windows.Forms.Timer countdown = new() { Interval = 50 };
    readonly SpeechSynthesizer speech = new();
    WaveOutEvent? player;
    AudioFileReader? audio;
    Bitmap? sceneBackground, defaultBackground, ledArt, barArt, counterRedArt, counterBlueArt, frameRedArt, frameBlueArt, plusArt, logoArt, photoA, photoB;
    string answerText = "";
    Bitmap? frameRedOff, frameBlueOff, stageCache; Size cacheSize; string? cacheTeam; bool cacheDirty = true;
    readonly System.Diagnostics.Stopwatch countdownWatch = new();
    RectangleF redFrame = RedFrameBox, blueFrame = BlueFrameBox, redWin = new(WinX, WinY, WinW, WinH), blueWin = new(WinX, WinY, WinW, WinH);
    readonly List<string> bgFiles = new();
    Hit? pressedHit, flashHit; DateTime flashUntil;
    readonly Panel instructionsScreen = new();
    readonly Label instructionsBody = new();
    readonly System.Windows.Forms.Timer typeTimer = new() { Interval = 60 };
    string instructionsText = ""; int typedChars;
    string? winner, logoPath;
    Keys redBuzzerKey = Keys.Q, blueBuzzerKey = Keys.P, resetKey = Keys.R;
    int seconds = 20, redPoints, bluePoints, currentScene, timerDeciseconds;
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
        EnsureDefaultSoundAssets();
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
        ledAnimation.Tick += (_, _) => { ledTick++; ledPhase = ledTick % 24; if (activeTeam != null || pressedHit != null || DateTime.UtcNow < flashUntil) stage.Invalidate(); }; ledAnimation.Start();
        countdown.Tick += (_, _) =>
        {
            timerDeciseconds = Math.Min(200, (int)(countdownWatch.ElapsedMilliseconds / 100));
            seconds = Math.Max(0, 20 - timerDeciseconds / 10);
            if (roundActive && activeTeam == null)
            {
                if (seconds > 0 && seconds != lastCountdownSoundSecond) { lastCountdownSoundSecond = seconds; PlayEffect(countdownEffectPath, defaultCountdownPath); }
                ledMessage = "TIEMPO " + seconds.ToString("00") + "  ·  " + redBuzzerKey + " ROJO | " + blueBuzzerKey + " AZUL";
            }
            UpdateClock();
            if (timerDeciseconds >= 200) { countdown.Stop(); roundActive = false; buzzLocked = true; PlayEffect(buzzerEffectPath, defaultBuzzerPath); SetStatus("¡TIEMPO! SE ACABÓ LA RONDA", null); }
        };
        ShowStartScreen();
    }


    // ============ PANTALLA DE JUEGO ============
    // Todo se dibuja sobre un lienzo de diseño de 1427 x 923 px que se escala a cualquier tamaño de ventana,
    // para que se vea siempre igual que la maqueta (marcador, respuesta, marcos con focos y barra de botones).
    const float DW = 1427f, DH = 923f;
    static readonly RectangleF LedRect = new(28, 18, 1377, 74);
    static readonly RectangleF RedCounterRect = new(24, 92, 300, 178);
    static readonly RectangleF BlueCounterRect = new(1103, 92, 300, 178);
    static readonly RectangleF AnswerBand = new(455, 132, 548, 90);
    static readonly RectangleF LeftLogoRect = new(332, 110, 130, 138);
    static readonly RectangleF RightLogoRect = new(997, 104, 130, 138);
    static readonly RectangleF RedFrameBox = new(25, 274, 650, 556);
    static readonly RectangleF BlueFrameBox = new(765, 274, 650, 556);
    static readonly RectangleF PlusRect = new(680, 506, 88, 88);
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
        Bitmap? T(string f) => LoadPhoto(Path.Combine(AppContext.BaseDirectory, f));
        counterRedArt = T("CONTADOR ROJO.png"); counterBlueArt = T("CONTADOR AZUL.png");
        frameRedArt = T("MARCO ROJO ALA MEDIDA.png") ?? T("MARCO ROJO PNG.png");
        frameBlueArt = T("MARCO AZUL ALA MEDIDA.png") ?? T("MARCO AZUL PNG.png");
        plusArt = T("SIGNO MAS PNG.png");
        frameRedOff = MakeBulbsOff(frameRedArt); frameBlueOff = MakeBulbsOff(frameBlueArt);
        redWin = FindWindow(frameRedArt); blueWin = FindWindow(frameBlueArt);
        redFrame = FitRect(frameRedArt, RedFrameBox); blueFrame = FitRect(frameBlueArt, BlueFrameBox);
        LoadBackgroundList();
        SetImageLogo();

        stage.Dock = DockStyle.Fill; stage.BackColor = orange;
        stage.Paint += (_, e) => PaintStage(e.Graphics);
        stage.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { pressedHit = HitAt(e.Location); stage.Invalidate(); } };
        stage.MouseUp += (_, e) =>
        {
            var h = pressedHit; pressedHit = null;
            if (e.Button == MouseButtons.Left && h != null && ReferenceEquals(HitAt(e.Location), h)) { flashHit = h; flashUntil = DateTime.UtcNow.AddMilliseconds(240); stage.Invalidate(); h.Act(); }
            else stage.Invalidate();
        };
        stage.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left && AnswerBand.Contains(ToDesign(e.Location))) EditCurrentAnswer(); };
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
        // Capa estática (fondo, marcos, fotos, barra) dibujada una sola vez: así el juego va fluido y el reloj no se atrasa.
        if (stageCache == null || cacheSize != cr.Size || cacheDirty || cacheTeam != activeTeam)
        {
            stageCache?.Dispose(); stageCache = new Bitmap(cr.Width, cr.Height, PixelFormat.Format32bppPArgb);
            using (var cg = Graphics.FromImage(stageCache)) RenderStatic(cg, cr);
            cacheSize = cr.Size; cacheTeam = activeTeam; cacheDirty = false;
        }
        g.DrawImageUnscaled(stageCache, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float s = Math.Min(cr.Width / DW, cr.Height / DH);
        g.TranslateTransform((cr.Width - DW * s) / 2f, (cr.Height - DH * s) / 2f); g.ScaleTransform(s, s);

        var st = g.Save(); g.TranslateTransform(LedRect.X, LedRect.Y); DrawMarquee(g, new Rectangle(0, 0, (int)LedRect.Width, (int)LedRect.Height)); g.Restore(st);
        DrawScore(g, RedCounterRect, redPoints); DrawScore(g, BlueCounterRect, bluePoints);
        DrawAnswer(g);

        // Resplandor del equipo que ganó el pulsador
        if (activeTeam != null)
        {
            int pulse = 110 + (int)(Math.Abs(Math.Sin(ledPhase * Math.PI / 12)) * 140);
            bool red = activeTeam == "red";
            using var pen = new Pen(red ? Color.FromArgb(pulse, 255, 40, 20) : Color.FromArgb(pulse, 25, 140, 255), 8) { LineJoin = LineJoin.Round };
            var fr = red ? redFrame : blueFrame; fr.Inflate(-4, -4); g.DrawRectangle(pen, fr.X, fr.Y, fr.Width, fr.Height);
            var cn = red ? RedCounterRect : BlueCounterRect; cn.Inflate(-2, -2); g.DrawRectangle(pen, cn.X, cn.Y, cn.Width, cn.Height);
        }
        DrawPressFeedback(g);
    }

    void RenderStatic(Graphics g, Rectangle cr)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var bg = sceneBackground ?? defaultBackground;
        if (bg != null) g.DrawImage(bg, cr); else g.Clear(orange);
        float s = Math.Min(cr.Width / DW, cr.Height / DH);
        g.TranslateTransform((cr.Width - DW * s) / 2f, (cr.Height - DH * s) / 2f); g.ScaleTransform(s, s);

        if (ledArt != null) g.DrawImage(ledArt, LedRect);
        if (counterRedArt != null) g.DrawImage(counterRedArt, RedCounterRect);
        if (counterBlueArt != null) g.DrawImage(counterBlueArt, BlueCounterRect);

        DrawOutlined(g, "RESPUESTA", 31, new RectangleF(AnswerBand.X, 96, AnswerBand.Width, 38), Brushes.White, Color.Black, 5f);
        using (var band = new SolidBrush(Color.FromArgb(244, 244, 244))) g.FillRectangle(band, AnswerBand);
        DrawZoom(g, logoArt, LeftLogoRect); DrawZoom(g, logoArt, RightLogoRect);

        // Fotos (debajo) y marcos (encima). Los focos del marco solo se encienden para el equipo que pulsó.
        DrawCover(g, photoA, WindowOf(redFrame, redWin)); DrawCover(g, photoB, WindowOf(blueFrame, blueWin));
        Bitmap? fRed = activeTeam == "red" ? frameRedArt : (frameRedOff ?? frameRedArt);
        Bitmap? fBlue = activeTeam == "blue" ? frameBlueArt : (frameBlueOff ?? frameBlueArt);
        if (fRed != null) g.DrawImage(fRed, redFrame);
        if (fBlue != null) g.DrawImage(fBlue, blueFrame);
        if (plusArt != null) g.DrawImage(plusArt, PlusRect); else DrawOutlined(g, "+", 80, PlusRect, Brushes.Gold, Color.Black, 4f);

        if (barArt != null) g.DrawImage(barArt, BarRect);
    }

    // Copia del marco con los focos apagados (los focos se detectan por su color claro).
    static Bitmap? MakeBulbsOff(Bitmap? src)
    {
        if (src == null) return null;
        try
        {
            var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(copy)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(src, 0, 0, src.Width, src.Height); }
            var data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                var buf = new byte[Math.Abs(data.Stride) * copy.Height]; Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int i = 0; i + 3 < buf.Length; i += 4)
                {
                    if (buf[i + 3] < 200) continue;
                    int b = buf[i], gg = buf[i + 1], r = buf[i + 2];
                    float w = (Math.Min(Math.Min(r, gg), b + 70) - 105) / 55f; if (w <= 0) continue; if (w > 1) w = 1;
                    float k = 0.55f + 0.45f * ((r * .3f + gg * .59f + b * .11f) / 255f);
                    buf[i + 2] = (byte)(r * (1 - w) + 70 * k * w); buf[i + 1] = (byte)(gg * (1 - w) + 58 * k * w); buf[i] = (byte)(b * (1 - w) + 60 * k * w);
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { copy.UnlockBits(data); }
            return copy;
        }
        catch { return null; }
    }

    RectangleF HitRect(Hit h) => new(BarRect.X + h.L * BarRect.Width, BarRect.Y + BarRect.Height * 0.12f, (h.R - h.L) * BarRect.Width, BarRect.Height * 0.76f);
    // Efecto de botón presionado: se oscurece con borde blanco mientras se mantiene el clic y destella al soltar.
    void DrawPressFeedback(Graphics g)
    {
        if (pressedHit != null)
        {
            var r = HitRect(pressedHit); using var path = RoundRect(r, Math.Min(r.Width, r.Height) * 0.28f);
            using var shade = new SolidBrush(Color.FromArgb(105, 0, 0, 0)); g.FillPath(shade, path);
            using var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 3.5f); g.DrawPath(pen, path);
        }
        else if (flashHit != null && DateTime.UtcNow < flashUntil)
        {
            double left = (flashUntil - DateTime.UtcNow).TotalMilliseconds / 240.0;
            var r = HitRect(flashHit); using var path = RoundRect(r, Math.Min(r.Width, r.Height) * 0.28f);
            using var glow = new SolidBrush(Color.FromArgb((int)(150 * left), 255, 255, 255)); g.FillPath(glow, path);
        }
    }
    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var gp = new GraphicsPath(); float d = rad * 2;
        gp.AddArc(r.X, r.Y, d, d, 180, 90); gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        gp.CloseFigure(); return gp;
    }
    // Ventana transparente interior del marco: se detecta sola, así cualquier marco "a la medida" funciona.
    static RectangleF FindWindow(Bitmap? b)
    {
        var fallback = new RectangleF(WinX, WinY, WinW, WinH);
        if (b == null) return fallback;
        try
        {
            int step = Math.Max(1, b.Width / 300), gw = b.Width / step, gh = b.Height / step;
            var clear = new bool[gw, gh];
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var buf = new byte[Math.Abs(data.Stride) * b.Height]; Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int gy = 0; gy < gh; gy++) for (int gx = 0; gx < gw; gx++) clear[gx, gy] = buf[gy * step * data.Stride + gx * step * 4 + 3] < 24;
            }
            finally { b.UnlockBits(data); }
            int cx = gw / 2, cy = gh / 2; if (!clear[cx, cy]) return fallback;
            var seen = new bool[gw, gh]; var q = new Queue<(int, int)>(); q.Enqueue((cx, cy)); seen[cx, cy] = true;
            int minX = cx, maxX = cx, minY = cy, maxY = cy;
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if (nx < 0 || ny < 0 || nx >= gw || ny >= gh || seen[nx, ny] || !clear[nx, ny]) continue;
                    seen[nx, ny] = true; q.Enqueue((nx, ny));
                }
            }
            if (minX <= 0 || minY <= 0 || maxX >= gw - 1 || maxY >= gh - 1) return fallback;
            return new RectangleF((float)minX / gw, (float)minY / gh, (float)(maxX - minX + 1) / gw, (float)(maxY - minY + 1) / gh);
        }
        catch { return fallback; }
    }
    static RectangleF FitRect(Image? img, RectangleF box)
    {
        if (img == null) return box;
        float sc = Math.Min(box.Width / img.Width, box.Height / img.Height), w = img.Width * sc, h = img.Height * sc;
        return new RectangleF(box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);
    }

    static RectangleF WindowOf(RectangleF f, RectangleF w)
    {
        var r = new RectangleF(f.X + f.Width * w.X, f.Y + f.Height * w.Y, f.Width * w.Width, f.Height * w.Height);
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
        var box = new RectangleF(counter.X + counter.Width * 0.575f, counter.Y + counter.Height * 0.40f, counter.Width * 0.345f, counter.Height * 0.43f);
        string s = points.ToString(); float px = 64;
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        Font font = new("Arial Black", px, FontStyle.Bold, GraphicsUnit.Pixel);
        while (px > 14) { var m = g.MeasureString(s, font, 10000, sf); if (m.Width <= box.Width * 0.92f && m.Height <= box.Height * 1.05f) break; font.Dispose(); px -= 3; font = new Font("Arial Black", px, FontStyle.Bold, GraphicsUnit.Pixel); }
        using (font)
        {
            using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.DrawString(s, font, shadow, new RectangleF(box.X + 2, box.Y + 2, box.Width, box.Height), sf);
            g.DrawString(s, font, Brushes.WhiteSmoke, box, sf);
        }
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

    void SetStatus(string text, string? team) { activeTeam = team; ledMessage = team == "red" ? "TURNO PARA EL EQUIPO ROJO" : team == "blue" ? "TURNO PARA EL EQUIPO AZUL" : text; ledPhase = 0; ledTick = 0; stage.Invalidate(); }
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
                int pos = (ledTick * 2) % n; double t = ((i - pos + n) % n) / (double)n * 4.0;
                float x, y;
                if (t < 1) { x = inset + (bounds.Width - inset * 2) * (float)t; y = inset; }
                else if (t < 2) { x = bounds.Width - inset; y = inset + (bounds.Height - inset * 2) * (float)(t - 1); }
                else if (t < 3) { x = bounds.Width - inset - (bounds.Width - inset * 2) * (float)(t - 2); y = bounds.Height - inset; }
                else { x = inset; y = bounds.Height - inset - (bounds.Height - inset * 2) * (float)(t - 3); }
                int d = (i - pos + n) % n; int alpha = d < 10 ? 255 - d * 20 : 60;
                using var bulb = new SolidBrush(activeTeam == "red" ? Color.FromArgb(alpha, 255, 35, 20) : Color.FromArgb(alpha, 20, 145, 255));
                g.FillEllipse(bulb, x - 6, y - 6, 12, 12);
            }
        }
        using var font = new Font("Consolas", bounds.Height * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var rect = new RectangleF(10, 6, bounds.Width - 20, bounds.Height - 12);
        Color ledColor = activeTeam == "red" ? Color.FromArgb(255, 255, 45, 35) : activeTeam == "blue" ? Color.FromArgb(255, 40, 150, 255) : Color.FromArgb(255, 255, 80, 40);
        using (var glow = new SolidBrush(Color.FromArgb(70, ledColor)))
            foreach (var (dx, dy) in new[] { (-2f, 0f), (2f, 0f), (0f, -2f), (0f, 2f) })
                g.DrawString(ledMessage, font, glow, new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height), sf);
        using var brush = new SolidBrush(ledColor);
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

    void StartRound() { if (!started) return; countdown.Stop(); seconds = 20; timerDeciseconds = 0; lastCountdownSoundSecond = 21; roundActive = true; buzzLocked = false; PlayEffect(roundStartEffectPath, defaultRoundStartPath); UpdateClock(); SetStatus("¡LISTOS! " + redBuzzerKey + " = ROJO  |  " + blueBuzzerKey + " = AZUL", null); countdownWatch.Restart(); countdown.Start(); }
    void Buzz(string team) { if (!started || !roundActive || buzzLocked) return; buzzLocked = true; roundActive = false; countdown.Stop(); PlayEffect(buzzerEffectPath, defaultBuzzerPath); SetStatus(team == "red" ? "¡EQUIPO ROJO TIENE EL TURNO PARA RESPONDER!" : "¡EQUIPO AZUL TIENE EL TURNO PARA RESPONDER!", team); }
    void ResetBuzzers() { countdown.Stop(); seconds = 20; timerDeciseconds = 0; roundActive = false; buzzLocked = true; UpdateClock(); SetStatus(DefaultTitle, null); }
    void AddPoint(string team) { if (team == "red") redPoints++; else bluePoints++; Scores(); }
    void RevealAnswer() { answerRevealed = true; answerText = scenes[currentScene].Answer.ToLowerInvariant(); stage.Invalidate(); }
    void ShowScene(int index, bool startTimer)
    {
        if (scenes.Count == 0) return;
        currentScene = ((index % scenes.Count) + scenes.Count) % scenes.Count; var s = scenes[currentScene];
        Text = "DISEÑO v4 - Adivina la Palabra con 2 Fotos  ·  Escena " + (currentScene + 1) + " / " + scenes.Count;
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
    // Lista de fondos incluidos (FONDO 1, 2, 3, FODNO 4...). Si una escena no tiene fondo propio se rota entre ellos.
    void LoadBackgroundList()
    {
        bgFiles.Clear();
        try
        {
            var found = Directory.EnumerateFiles(AppContext.BaseDirectory)
                .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^(FONDO|FODNO)\s*\d+\.(jpg|jpeg|png)$", RegexOptions.IgnoreCase))
                .OrderBy(p => int.Parse(Regex.Match(Path.GetFileName(p), @"\d+").Value)).ToList();
            // El naranja (FODNO 4) va primero para que la primera escena sea igual a la maqueta.
            var first = found.FirstOrDefault(p => Regex.IsMatch(Path.GetFileName(p), @"\s*4\."));
            if (first != null) { found.Remove(first); found.Insert(0, first); }
            bgFiles.AddRange(found);
        }
        catch { }
    }
    void SetSceneBackground(string path)
    {
        var old = sceneBackground; sceneBackground = null;
        var resolved = ResolveAssetPath(path);
        if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(gameImagePath)) resolved = ResolveAssetPath(gameImagePath);
        else if (string.IsNullOrWhiteSpace(path) && bgFiles.Count > 0) resolved = bgFiles[currentScene % bgFiles.Count];
        if (!string.IsNullOrWhiteSpace(resolved)) sceneBackground = LoadBitmap(resolved);
        old?.Dispose(); cacheDirty = true; stage.Invalidate();
    }
    void PreviousScene() { if (scenes.Count > 0) ShowScene(currentScene - 1, true); }
    void NextScene() { if (scenes.Count > 0) ShowScene(currentScene + 1, true); }
    void ShowStartScreen()
    {
        SetStartScreenImage();
        startScreen.Dock = DockStyle.Fill; startScreen.BackColor = Color.FromArgb(235, 50, 8); startScreen.BackgroundImageLayout = ImageLayout.Stretch; startScreen.BringToFront();
        var title = new Label { Text = "ADIVINA LA PALABRA\nCON 2 FOTOS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 42), ForeColor = Color.White };
        var hint = new Label { Text = "PRESIONA ENTER PARA COMENZAR", Dock = DockStyle.Bottom, Height = 105, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 25), ForeColor = Color.Yellow };
        startScreen.Controls.Add(title); startScreen.Controls.Add(hint);
        var splashFullscreen = new Button { Text = "⛶", Width = 54, Height = 32, Left = ClientSize.Width - 66, Top = 5, Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.LimeGreen, ForeColor = Color.Black, Font = new Font("Arial Black", 15) };
        splashFullscreen.Click += (_, _) => ToggleFullscreen(); startScreen.Controls.Add(splashFullscreen); splashFullscreen.BringToFront();
        Controls.Add(startScreen); startScreen.BringToFront();
    }
    void BeginGame() { if (started) return; started = true; startScreen.Visible = false; StartSuspenseMusic(); ShowScene(currentScene, true); }
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
        if (key == Keys.Escape) { if (instructionsScreen.Visible) CloseInstructions(); else if (winnerScreen.Visible) winnerScreen.Visible = false; else if (isFullscreen) ToggleFullscreen(); else ResetBuzzers(); return true; }
        if (key == Keys.Left) { PreviousScene(); return true; }
        if (key == Keys.Right) { NextScene(); return true; }
        return base.ProcessCmdKey(ref msg, key);
    }

    void OpenSettings()
    {
        if (settingsOpen) return; settingsOpen = true;
        using var f = new Form { Text = "MENÚ DE CONFIGURACIÓN - DESAFÍOS LOCOS", StartPosition = FormStartPosition.CenterParent, Size = new Size(760, 720), BackColor = Color.FromArgb(28, 28, 36), ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var musicTab = new TabPage("MÚSICA") { BackColor = f.BackColor, ForeColor = Color.White };
        var soundTab = new TabPage("EFECTOS") { BackColor = f.BackColor, ForeColor = Color.White };
        var screenTab = new TabPage("PANTALLAS") { BackColor = f.BackColor, ForeColor = Color.White };
        var logoTab = new TabPage("LOGOTIPO") { BackColor = f.BackColor, ForeColor = Color.White };
        var scenesTab = new TabPage("ESCENAS") { BackColor = f.BackColor, ForeColor = Color.White };
        var buzzersTab = new TabPage("PULSADORES") { BackColor = f.BackColor, ForeColor = Color.White };
        var answersTab = new TabPage("CORREGIR RESPUESTAS") { BackColor = f.BackColor, ForeColor = Color.White };
        tabs.TabPages.AddRange(new[] { musicTab, soundTab, screenTab, logoTab, scenesTab, answersTab, buzzersTab }); f.Controls.Add(tabs);
        var musicPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), WrapContents = false };
        musicTab.Controls.Add(musicPanel);
        musicPanel.Controls.Add(new Label { Text = "ASIGNA UNA CANCIÓN DIFERENTE A CADA BOTÓN VERDE", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 12), Margin = new Padding(4, 10, 4, 18) });
        for (int i = 0; i < 4; i++) { int n = i; var b = new Button { Text = "CARGAR MP3 PARA CELEBRACIÓN " + (i + 1), Width = 590, Height = 54, BackColor = Color.LawnGreen, ForeColor = Color.Black, Font = new Font("Arial Black", 10) }; b.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Archivos MP3|*.mp3" }; if (d.ShowDialog(f) == DialogResult.OK) { audios[n] = CopyToData(d.FileName, "celebracion" + (n + 1) + Path.GetExtension(d.FileName)); b.Text = "CELEBRACIÓN " + (n + 1) + " · " + Path.GetFileName(d.FileName); } }; musicPanel.Controls.Add(b); }
        var soundPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18), AutoScroll = true, WrapContents = false }; soundTab.Controls.Add(soundPanel);
        soundPanel.Controls.Add(new Label { Text = "SONIDOS PREDETERMINADOS DEL JUEGO · PUEDES CAMBIARLOS POR MP3 O WAV", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 11), MaximumSize = new Size(680, 0), Margin = new Padding(3, 8, 3, 12) });
        void AddSoundPicker(string title, Func<string?> getPath, Action<string?> setPath, string fileBase)
        {
            var current = getPath();
            var button = new Button { Text = title + (string.IsNullOrWhiteSpace(current) ? " · PREDETERMINADO" : " · " + Path.GetFileName(current)), Width = 660, Height = 48, BackColor = Color.FromArgb(65, 65, 78), ForeColor = Color.White, Font = new Font("Arial Black", 9) };
            button.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Audio MP3 o WAV|*.mp3;*.wav" }; if (d.ShowDialog(f) == DialogResult.OK) { var saved = CopyToData(d.FileName, fileBase + Path.GetExtension(d.FileName).ToLowerInvariant()); setPath(saved); button.Text = title + " · " + Path.GetFileName(saved); } };
            soundPanel.Controls.Add(button);
        }
        AddSoundPicker("MÚSICA DE INTRIGA DURANTE EL JUEGO", () => suspenseMusicPath, v => suspenseMusicPath = v, "musica-intriga");
        AddSoundPicker("SONIDO DEL CONTADOR (CADA SEGUNDO)", () => countdownEffectPath, v => countdownEffectPath = v, "sonido-contador");
        AddSoundPicker("EFECTO AL INICIAR CADA RONDA", () => roundStartEffectPath, v => roundStartEffectPath = v, "sonido-inicio-ronda");
        AddSoundPicker("EFECTO DEL PULSADOR Y FIN DEL TIEMPO", () => buzzerEffectPath, v => buzzerEffectPath = v, "sonido-pulsador");
        var resetSounds = new Button { Text = "RESTABLECER SONIDOS PREDETERMINADOS", Width = 660, Height = 44, BackColor = Color.Goldenrod, ForeColor = Color.Black };
        resetSounds.Click += (_, _) => { suspenseMusicPath = countdownEffectPath = roundStartEffectPath = buzzerEffectPath = null; foreach (Control c in soundPanel.Controls) if (c is Button b && b != resetSounds) b.Text = b.Text.Split(" · ")[0] + " · PREDETERMINADO"; };
        soundPanel.Controls.Add(resetSounds);
        var screenPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18), AutoScroll = true, WrapContents = false }; screenTab.Controls.Add(screenPanel);
        screenPanel.Controls.Add(new Label { Text = "PERSONALIZA LAS DOS PANTALLAS DEL JUEGO", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 12), Margin = new Padding(3, 8, 3, 12) });
        var startImageButton = new Button { Text = "ELEGIR FOTO DE LA PANTALLA INICIAL" + (string.IsNullOrWhiteSpace(startImagePath) ? " · PREDETERMINADA" : " · " + Path.GetFileName(startImagePath)), Width = 660, Height = 52, BackColor = Color.FromArgb(65, 65, 78), ForeColor = Color.White };
        startImageButton.Click += (_, _) => { var path = PickImage(); if (path != null) { startImagePath = CopyToData(path, "pantalla-inicial" + Path.GetExtension(path).ToLowerInvariant()); SetStartScreenImage(); startImageButton.Text = "FOTO INICIAL · " + Path.GetFileName(path); } };
        screenPanel.Controls.Add(startImageButton);
        var gameImageButton = new Button { Text = "ELEGIR FOTO AL PRESIONAR ENTER" + (string.IsNullOrWhiteSpace(gameImagePath) ? " · FONDO ACTUAL" : " · " + Path.GetFileName(gameImagePath)), Width = 660, Height = 52, BackColor = Color.FromArgb(65, 65, 78), ForeColor = Color.White };
        gameImageButton.Click += (_, _) => { var path = PickImage(); if (path != null) { gameImagePath = CopyToData(path, "pantalla-juego" + Path.GetExtension(path).ToLowerInvariant()); SetSceneBackground(scenes.Count > 0 ? scenes[currentScene].Background : ""); gameImageButton.Text = "FOTO AL COMENZAR · " + Path.GetFileName(path); } };
        screenPanel.Controls.Add(gameImageButton);
        var resetImages = new Button { Text = "RESTABLECER FOTOS PREDETERMINADAS", Width = 660, Height = 44, BackColor = Color.Goldenrod, ForeColor = Color.Black };
        resetImages.Click += (_, _) => { startImagePath = gameImagePath = null; SetStartScreenImage(); SetSceneBackground(scenes.Count > 0 ? scenes[currentScene].Background : ""); startImageButton.Text = "ELEGIR FOTO DE LA PANTALLA INICIAL · PREDETERMINADA"; gameImageButton.Text = "ELEGIR FOTO AL PRESIONAR ENTER · FONDO ACTUAL"; };
        screenPanel.Controls.Add(resetImages);
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
        var answersPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(14), AutoScroll = true, WrapContents = false };
        answersTab.Controls.Add(answersPanel);
        answersPanel.Controls.Add(new Label { Text = "CORRIGE EL NOMBRE DE CADA RESPUESTA Y PRESIONA GUARDAR", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 11), Margin = new Padding(3, 4, 3, 10) });
        var answerBoxes = new List<TextBox>();
        for (int i = 0; i < scenes.Count; i++)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            row.Controls.Add(new Label { Text = (i + 1) + ".", Width = 40, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.White, Height = 28 });
            var tb = new TextBox { Text = scenes[i].Answer, Width = 480, Font = new Font("Arial", 12, FontStyle.Bold) };
            answerBoxes.Add(tb); row.Controls.Add(tb); answersPanel.Controls.Add(row);
        }
        var saveAnswers = new Button { Text = "GUARDAR RESPUESTAS CORREGIDAS", Width = 540, Height = 46, BackColor = Color.LimeGreen, ForeColor = Color.Black, Font = new Font("Arial Black", 10), Margin = new Padding(3, 12, 3, 4) };
        saveAnswers.Click += (_, _) => { for (int i = 0; i < answerBoxes.Count && i < scenes.Count; i++) scenes[i].Answer = answerBoxes[i].Text.Trim(); if (SaveConfig()) { MessageBox.Show(f, "¡Respuestas corregidas y guardadas!", "Desafíos Locos", MessageBoxButtons.OK, MessageBoxIcon.Information); } if (currentScene < scenes.Count && answerRevealed) answerText = scenes[currentScene].Answer.ToLowerInvariant(); else if (currentScene < scenes.Count) answerText = scenes[currentScene].Answer; stage.Invalidate(); };
        answersPanel.Controls.Add(saveAnswers);
        var reloadScenes = new Button { Text = "RECARGAR ESCENAS ORIGINALES DEL JUEGO", Width = 540, Height = 40, BackColor = Color.Goldenrod, ForeColor = Color.Black, Font = new Font("Arial Black", 9) };
        reloadScenes.Click += (_, _) => { if (MessageBox.Show(f, "Se borrarán las escenas personalizadas y se cargarán otra vez las incluidas con el juego. ¿Continuar?", "Desafíos Locos", MessageBoxButtons.YesNo) != DialogResult.Yes) return; scenes.Clear(); LoadBundledScenes(); if (scenes.Count == 0) scenes.Add(new Scene { Answer = "AGUAFIESTAS" }); SaveConfig(); f.Close(); ShowScene(0, false); OpenSettings(); };
        answersPanel.Controls.Add(reloadScenes);
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
        panel.Controls.Add(new Label { Text = "O ELIGE UNO DE LOS FONDOS INCLUIDOS (si no eliges, rotan solos):", ForeColor = Color.Gold, AutoSize = true, Margin = new Padding(3, 12, 3, 4) });
        var builtInBackgrounds = bgFiles.Select(x => Path.GetFileName(x)).ToArray();
        foreach (var file in builtInBackgrounds)
        {
            string selectedFile = file;
            var choose = new Button { Text = "USAR " + file, Width = 450, Height = 36, BackColor = Color.FromArgb(65, 65, 78), ForeColor = Color.White };
            choose.Click += (_, _) => { var source = Path.Combine(AppContext.BaseDirectory, selectedFile); if (File.Exists(source)) { s.Background = source; if (index == currentScene) SetSceneBackground(source); } else MessageBox.Show(f, "No se encontró " + selectedFile + " junto al programa. Vuelve a compilar la aplicación.", "Fondo no disponible"); };
            panel.Controls.Add(choose);
        }
        var save = new Button { Text = "GUARDAR ESCENA", Width = 450, Height = 44, BackColor = Color.LimeGreen }; save.Click += (_, _) => { s.Answer = answerBox.Text.Trim(); SaveConfig(); if (index == currentScene) ShowScene(index, false); f.Close(); }; panel.Controls.Add(save);
        f.ShowDialog(owner);
    }
    string? PickImage() { using var d = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp" }; return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; }
    string CopyToData(string source, string name) { var dest = Path.Combine(dataDir, name); File.Copy(source, dest, true); return dest; }
    void SetImageLogo() { var old = logoArt; logoArt = null; if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath)) logoArt = LoadBitmap(logoPath); old?.Dispose(); cacheDirty = true; stage.Invalidate(); }
    static readonly Dictionary<string, string> SpellFix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ROMPEXABEZAS"] = "ROMPECABEZAS", ["CANDADDO"] = "CANDADO", ["ARCOIRRIS"] = "ARCOÍRIS", ["CIENPIES"] = "CIEMPIÉS",
        ["PIZAPAPELES"] = "PISAPAPELES", ["PUESRCO ESPIN"] = "PUERCOESPÍN", ["HIERVA BUENA"] = "HIERBABUENA", ["JIRASOL"] = "GIRASOL",
        ["PIZARON"] = "PIZARRÓN", ["ESPANTA PAJAROS"] = "ESPANTAPÁJAROS", ["ESPANTAPAJAROS"] = "ESPANTAPÁJAROS", ["PARAAGUAS"] = "PARAGUAS",
        ["LAVA MANOS"] = "LAVAMANOS", ["MONOPATIN"] = "MONOPATÍN", ["PANTALON"] = "PANTALÓN", ["AUTOMOVIL"] = "AUTOMÓVIL",
        ["CORTA UÑAS"] = "CORTAÚÑAS", ["VIDEOJUEGO"] = "VIDEOJUEGOS", ["TELARAÑA"] = "TELARAÑA"
    };

    void LoadBundledScenes()
    {
        // Cada escena = número + respuesta. Se ignoran los archivos duplicados con "#U00d1" en el nombre
        // (se tratan como Ñ) y las dos fotos se ordenan por su terminación 1 / 2.
        string Norm(string s) => s.Replace("#U00d1", "Ñ").Replace("#U00D1", "Ñ");
        var items = new List<(int Number, string Answer, string Path, string Name, bool Clean, int Order)>();
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            string file = Path.GetFileName(path);
            if (!new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
            var num = Regex.Match(file, @"^(\d+)\s+"); if (!num.Success) continue;
            string stem = Norm(Path.GetFileNameWithoutExtension(file));
            var m = Regex.Match(stem, @"(?:RESPUESTA|REPUETA|RESPUETA)\s*(.+?)\s*([12])?$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            string answer = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ").ToUpperInvariant();
            if (SpellFix.TryGetValue(answer, out var fixedAnswer)) answer = fixedAnswer;
            int order = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 9;
            items.Add((int.Parse(num.Groups[1].Value), answer, path, file, !file.Contains("#U00", StringComparison.OrdinalIgnoreCase), order));
        }
        var built = new List<(int Number, Scene Scene)>();
        var loose = new List<(int Number, string Answer, string Path, int Order)>();
        int Overlap(string name, string key) { var w = Regex.Match(Regex.Replace(Norm(Path.GetFileNameWithoutExtension(name)), @"^\d+\s+", ""), @"^[A-ZÑa-zñ]+").Value.ToUpperInvariant(); int i = 0; while (i < w.Length && i < key.Length && w[i] == key[i]) i++; return i; }
        foreach (var g in items.GroupBy(x => x.Number).OrderBy(k => k.Key))
        {
            // De cada nombre de archivo repetido (con o sin #U00d1) se conserva el que tiene la Ñ correcta.
            var files = g.GroupBy(x => Norm(x.Name), StringComparer.OrdinalIgnoreCase).Select(gr => gr.OrderByDescending(x => x.Clean).First()).ToList();
            if (files.Count < 2) { foreach (var f1 in files) loose.Add((f1.Number, f1.Answer, f1.Path, f1.Order)); continue; }
            string answer = g.First().Answer, key = Regex.Replace(answer, @"[^A-ZÑ]", "");
            var ordered = files.OrderBy(x => x.Order).ThenByDescending(x => Overlap(x.Name, key)).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            built.Add((g.Key, new Scene { Answer = answer, Image1 = Path.GetFileName(ordered[0].Path), Image2 = Path.GetFileName(ordered[1].Path) }));
        }
        // Fotos sueltas (por ejemplo SALTAMONTES, archivos 29 y 30): se emparejan por la respuesta común.
        foreach (var lg in loose.GroupBy(x => x.Answer, StringComparer.OrdinalIgnoreCase))
        {
            var pair = lg.OrderBy(x => x.Order).ThenBy(x => x.Number).Take(2).ToArray();
            if (pair.Length == 2) built.Add((pair.Min(x => x.Number), new Scene { Answer = lg.Key, Image1 = Path.GetFileName(pair[0].Path), Image2 = Path.GetFileName(pair[1].Path) }));
        }
        foreach (var entry in built.OrderBy(x => x.Number)) scenes.Add(entry.Scene);
    }

    void LoadConfig()
    {
        try { var path = Path.Combine(dataDir, "config.json"); if (!File.Exists(path)) return; var c = JsonSerializer.Deserialize<SavedConfig>(File.ReadAllText(path)); if (c == null) return; logoPath = c.Logo; suspenseMusicPath = c.SuspenseMusic; countdownEffectPath = c.CountdownEffect; roundStartEffectPath = c.RoundStartEffect; buzzerEffectPath = c.BuzzerEffect; startImagePath = c.StartImage; gameImagePath = c.GameImage; redBuzzerKey = Enum.TryParse<Keys>(c.RedBuzzerKey, out var rk) ? rk : Keys.Q; blueBuzzerKey = Enum.TryParse<Keys>(c.BlueBuzzerKey, out var bk) ? bk : Keys.P; resetKey = Enum.TryParse<Keys>(c.ResetKey, out var rr) ? rr : Keys.R; if (redBuzzerKey == blueBuzzerKey || redBuzzerKey == Keys.M || blueBuzzerKey == Keys.M || resetKey == redBuzzerKey || resetKey == blueBuzzerKey || resetKey == Keys.M || resetKey == Keys.Enter) { redBuzzerKey = Keys.Q; blueBuzzerKey = Keys.P; resetKey = Keys.R; } if (c.Audios != null) for (int i = 0; i < 4 && i < c.Audios.Length; i++) audios[i] = c.Audios[i]; if (c.Scenes != null) { scenes.Clear(); scenes.AddRange(c.Scenes); } }
        catch { }
    }
    bool SaveConfig()
    {
        try { var c = new SavedConfig { Logo = logoPath ?? "", Audios = audios.Select(x => x ?? "").ToArray(), Scenes = scenes, RedBuzzerKey = redBuzzerKey.ToString(), BlueBuzzerKey = blueBuzzerKey.ToString(), ResetKey = resetKey.ToString(), SuspenseMusic = suspenseMusicPath ?? "", CountdownEffect = countdownEffectPath ?? "", RoundStartEffect = roundStartEffectPath ?? "", BuzzerEffect = buzzerEffectPath ?? "", StartImage = startImagePath ?? "", GameImage = gameImagePath ?? "" }; File.WriteAllText(Path.Combine(dataDir, "config.json"), JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true })); return true; } catch (Exception ex) { MessageBox.Show(this, "No se pudieron guardar los cambios: " + ex.Message, "Error al guardar", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }

    void EnsureDefaultSoundAssets()
    {
        defaultSuspensePath = Path.Combine(dataDir, "predeterminado-intriga.wav");
        defaultCountdownPath = Path.Combine(dataDir, "predeterminado-contador.wav");
        defaultRoundStartPath = Path.Combine(dataDir, "predeterminado-inicio.wav");
        defaultBuzzerPath = Path.Combine(dataDir, "predeterminado-pulsador.wav");
        if (!File.Exists(defaultSuspensePath)) WriteDefaultWav(defaultSuspensePath, 16000, t => 0.075 * Math.Sin(2 * Math.PI * 55 * t) + 0.045 * Math.Sin(2 * Math.PI * 82.5 * t) + 0.025 * Math.Sin(2 * Math.PI * 110 * t) + 0.018 * Math.Sin(2 * Math.PI * 220 * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.25 * t)));
        if (!File.Exists(defaultCountdownPath)) WriteDefaultWav(defaultCountdownPath, 130, t => 0.28 * Math.Sin(2 * Math.PI * 1050 * t) * Math.Exp(-t * 28));
        if (!File.Exists(defaultRoundStartPath)) WriteDefaultWav(defaultRoundStartPath, 650, t => 0.22 * Math.Sin(2 * Math.PI * (360 + 420 * t) * t) * Math.Min(1, t * 18) * Math.Max(0, 1 - t / 0.65));
        if (!File.Exists(defaultBuzzerPath)) WriteDefaultWav(defaultBuzzerPath, 420, t => 0.24 * (Math.Sin(2 * Math.PI * 440 * t) + 0.65 * Math.Sin(2 * Math.PI * 554 * t)) * Math.Exp(-t * 5));
    }
    static void WriteDefaultWav(string path, int durationMs, Func<double, double> sample)
    {
        const int rate = 44100;
        int count = rate * durationMs / 1000;
        using var writer = new WaveFileWriter(path, new WaveFormat(rate, 16, 1));
        var buffer = new byte[count * 2];
        for (int i = 0; i < count; i++) { short value = (short)(Math.Clamp(sample((double)i / rate), -1.0, 1.0) * short.MaxValue); BitConverter.TryWriteBytes(buffer.AsSpan(i * 2, 2), value); }
        writer.Write(buffer, 0, buffer.Length);
    }
    void StartSuspenseMusic()
    {
        StopSuspenseMusic();
        string path = !string.IsNullOrWhiteSpace(suspenseMusicPath) && File.Exists(suspenseMusicPath) ? suspenseMusicPath : defaultSuspensePath;
        try
        {
            suspenseReader = new AudioFileReader(path); suspensePlayer = new WaveOutEvent(); musicLoopEnabled = true;
            suspensePlayer.PlaybackStopped += (_, _) => { if (musicLoopEnabled && suspensePlayer != null && suspenseReader != null) { try { suspenseReader.Position = 0; suspensePlayer.Play(); } catch { } } };
            suspensePlayer.Init(suspenseReader); suspensePlayer.Play();
        }
        catch { StopSuspenseMusic(); }
    }
    void StopSuspenseMusic()
    {
        musicLoopEnabled = false; try { suspensePlayer?.Stop(); } catch { }
        suspensePlayer?.Dispose(); suspensePlayer = null; suspenseReader?.Dispose(); suspenseReader = null;
    }
    void PlayEffect(string? customPath, string fallbackPath)
    {
        string path = !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath) ? customPath : fallbackPath;
        if (!File.Exists(path)) return;
        StopEffect();
        try { effectReader = new AudioFileReader(path); effectPlayer = new WaveOutEvent(); effectPlayer.Init(effectReader); effectPlayer.Play(); }
        catch { StopEffect(); }
    }
    void StopEffect()
    {
        try { effectPlayer?.Stop(); } catch { }
        effectPlayer?.Dispose(); effectPlayer = null; effectReader?.Dispose(); effectReader = null;
    }
    void SetStartScreenImage()
    {
        var old = startScreen.BackgroundImage;
        startScreen.BackgroundImage = LoadBitmap(ResolveAssetPath(startImagePath ?? ""));
        startScreen.BackgroundImageLayout = ImageLayout.Stretch; old?.Dispose(); startScreen.Invalidate();
    }
    void PlayCelebration(int n)
    {
        if (string.IsNullOrWhiteSpace(audios[n]) || !File.Exists(audios[n])) { MessageBox.Show(this, "Carga el MP3 de esta celebración desde el menú M.", "Desafíos Locos"); return; }
        StopAudio(); try { audio = new AudioFileReader(audios[n]!); player = new WaveOutEvent(); player.Init(audio); player.Play(); } catch (Exception ex) { MessageBox.Show(this, "No se pudo reproducir el audio: " + ex.Message); }
    }
    void StopAudio() { try { player?.Stop(); } catch { } player?.Dispose(); player = null; audio?.Dispose(); audio = null; }
    void Instructions()
    {
        instructionsText = "¡Bienvenidos, competidores! En esta prueba verán dos fotografías en la pantalla. Cada foto representa una palabra. Si juntan las dos palabras, formarán una palabra nueva, y esa es la respuesta. Por ejemplo: porta y vasos forman portavasos. Tendrán diez segundos para descubrirla. El equipo rojo y el equipo azul tienen cada uno su pulsador. El primer equipo que lo presione detiene el tiempo, y se encienden las luces de su color. Solo ese equipo podrá decir la respuesta en voz alta. Si aciertan, ganan un punto. Gana el equipo que consiga más puntos. Escuchen con atención, trabajen en equipo, ¡y mucha suerte!";
        if (instructionsScreen.Controls.Count == 0)
        {
            instructionsScreen.Dock = DockStyle.Fill; instructionsScreen.BackColor = Color.FromArgb(22, 22, 34); instructionsScreen.Padding = new Padding(60, 30, 60, 20);
            var title = new Label { Text = "INSTRUCCIONES DEL JUEGO", Dock = DockStyle.Top, Height = 90, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 34), ForeColor = Color.Gold };
            instructionsBody.Dock = DockStyle.Fill; instructionsBody.Font = new Font("Arial", 27, FontStyle.Bold); instructionsBody.ForeColor = Color.White; instructionsBody.TextAlign = ContentAlignment.MiddleCenter;
            var close = new Button { Text = "CERRAR", Dock = DockStyle.Bottom, Height = 70, BackColor = Color.Gold, ForeColor = Color.Black, Font = new Font("Arial Black", 18), FlatStyle = FlatStyle.Flat };
            close.Click += (_, _) => CloseInstructions();
            instructionsScreen.Controls.Add(instructionsBody); instructionsScreen.Controls.Add(title); instructionsScreen.Controls.Add(close);
            typeTimer.Tick += (_, _) => { typedChars = Math.Min(instructionsText.Length, typedChars + 1); instructionsBody.Text = instructionsText.Substring(0, typedChars); if (typedChars >= instructionsText.Length) typeTimer.Stop(); };
            speech.SpeakCompleted += (_, e) => { if (!e.Cancelled && instructionsScreen.Visible) BeginInvoke(new Action(() => { typeTimer.Stop(); instructionsBody.Text = instructionsText; })); };
        }
        Controls.Add(instructionsScreen); instructionsScreen.Visible = true; instructionsScreen.BringToFront();
        typedChars = 0; instructionsBody.Text = ""; typeTimer.Start();
        try
        {
            speech.SpeakAsyncCancelAll();
            var es = speech.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "es");
            if (es != null) speech.SelectVoice(es.VoiceInfo.Name);
            speech.SpeakAsync(instructionsText);
        }
        catch { typeTimer.Stop(); instructionsBody.Text = instructionsText; }
    }
    // Doble clic sobre la franja blanca de la respuesta: corrige el nombre de la escena actual y lo guarda al instante.
    void EditCurrentAnswer()
    {
        if (scenes.Count == 0) return;
        using var f = new Form { Text = "Corregir respuesta", StartPosition = FormStartPosition.CenterParent, Width = 580, Height = 200, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Color.FromArgb(30, 30, 40) };
        var lbl = new Label { Text = "Escena " + (currentScene + 1) + ": escribe la respuesta correcta", ForeColor = Color.White, Left = 15, Top = 12, Width = 540 };
        var tb = new TextBox { Left = 15, Top = 42, Width = 535, Font = new Font("Arial", 16, FontStyle.Bold), Text = scenes[currentScene].Answer };
        var ok = new Button { Text = "GUARDAR", Left = 15, Top = 95, Width = 260, Height = 42, DialogResult = DialogResult.OK, BackColor = Color.LimeGreen };
        var cancel = new Button { Text = "CANCELAR", Left = 290, Top = 95, Width = 260, Height = 42, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var text = tb.Text.Trim(); if (text.Length == 0) return;
        scenes[currentScene].Answer = text; answerText = answerRevealed ? text.ToLowerInvariant() : text; SaveConfig(); stage.Invalidate();
    }
    void CloseInstructions() { typeTimer.Stop(); try { speech.SpeakAsyncCancelAll(); } catch { } instructionsScreen.Visible = false; }
    void ExitGame() { if (MessageBox.Show(this, "¿Deseas salir del juego?", "Desafíos Locos", MessageBoxButtons.YesNo) == DialogResult.Yes) Close(); }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        countdown.Stop(); ledAnimation.Stop(); StopAudio(); StopSuspenseMusic(); StopEffect();
        stageCache?.Dispose(); frameRedOff?.Dispose(); frameBlueOff?.Dispose();
        foreach (var b in new[] { sceneBackground, defaultBackground, ledArt, barArt, counterRedArt, counterBlueArt, frameRedArt, frameBlueArt, plusArt, logoArt, photoA, photoB, startScreen.BackgroundImage as Bitmap }) b?.Dispose();
        speech.Dispose(); base.OnFormClosed(e);
    }
}