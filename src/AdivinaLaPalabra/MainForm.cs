using NAudio.Wave;
using System.Speech.Synthesis;
using System.Text.Json;

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
    readonly Label redScore = new(), blueScore = new(), clock = new(), answerLabel = new(), sceneNumber = new(), status = new();
    readonly PictureBox photo1 = new(), photo2 = new(), logoBox = new();
    Panel redPhotoFrame = new(), bluePhotoFrame = new();
    readonly Panel redPanel = new(), bluePanel = new(), gamePanel = new(), startScreen = new(), winnerScreen = new();
    readonly BufferedPanel timerPanel = new();
    readonly BufferedPanel ledPanel = new();
    readonly System.Windows.Forms.Timer ledAnimation = new() { Interval = 80 };
    string ledMessage = "ADIVINA LA PALABRA CON 2 FOTOS"; string? activeTeam; int ledPhase;
    readonly System.Windows.Forms.Timer countdown = new() { Interval = 100 };
    readonly SpeechSynthesizer speech = new();
    WaveOutEvent? player;
    AudioFileReader? audio;
    Image? currentBackground, timerArtwork;
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
        if (scenes.Count == 0) scenes.Add(new Scene { Answer = "lavadora" });
        BuildInterface(); ShowScene(0, false);
        ledAnimation.Tick += (_, _) => { ledPhase = (ledPhase + 1) % 24; ledPanel.Invalidate(); redPanel.Invalidate(); bluePanel.Invalidate(); }; ledAnimation.Start();
        countdown.Tick += (_, _) => { timerDeciseconds = Math.Min(100, timerDeciseconds + 1); seconds = Math.Max(0, 10 - timerDeciseconds / 10); UpdateClock(); if (timerDeciseconds >= 100) { countdown.Stop(); roundActive = false; buzzLocked = true; SetStatus("¡TIEMPO! SE ACABÓ LA RONDA", null); } };
        ShowStartScreen();
    }

    void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10, 6, 10, 6), BackColor = Color.Transparent, BackgroundImageLayout = ImageLayout.Stretch };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); Controls.Add(root);
        ledPanel.Dock = DockStyle.Fill; ledPanel.BackColor = Color.FromArgb(18, 15, 20);
        string ledPath = Path.Combine(AppContext.BaseDirectory, "PANTALLA LEDS.jpg");
        if (File.Exists(ledPath)) { using var li = Image.FromFile(ledPath); ledPanel.BackgroundImage = new Bitmap(li); ledPanel.BackgroundImageLayout = ImageLayout.Stretch; }
        ledPanel.Paint += (_, e) => DrawMarquee(e.Graphics, ledPanel.ClientRectangle); root.Controls.Add(ledPanel, 0, 0);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        BuildTeam(redPanel, "EQUIPO ROJO", redScore, Color.LightCoral);
        BuildTeam(bluePanel, "EQUIPO AZUL", blueScore, Color.DeepSkyBlue);
        var center = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        logoBox.SizeMode = PictureBoxSizeMode.Zoom; logoBox.Dock = DockStyle.Left; logoBox.Width = 68; logoBox.BackColor = Color.Transparent;
        var brand = new Label { Text = "DESAFÍOS LOCOS", Dock = DockStyle.Top, Height = 25, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 11), ForeColor = Color.Gold, BackColor = Color.Transparent };
        timerPanel.Dock = DockStyle.Fill; timerPanel.BackColor = Color.FromArgb(34, 30, 31); timerPanel.BackgroundImageLayout = ImageLayout.None; timerPanel.MinimumSize = new Size(150, 110);
        var timerPath = Path.Combine(AppContext.BaseDirectory, "CRONOMETRO.png"); if (File.Exists(timerPath)) { using var ti = Image.FromFile(timerPath); timerArtwork = new Bitmap(ti); }
        
        clock.Visible = false;
        timerPanel.Paint += (_, e) => DrawAnimatedClock(e.Graphics, timerPanel.ClientRectangle);
        center.Controls.Add(timerPanel); center.Controls.Add(brand); center.Controls.Add(logoBox);
        top.Controls.Add(redPanel, 0, 0); top.Controls.Add(center, 1, 0); top.Controls.Add(bluePanel, 2, 0); root.Controls.Add(top, 0, 1);

        gamePanel.Dock = DockStyle.Fill; gamePanel.BackColor = Color.Transparent; gamePanel.BackgroundImageLayout = ImageLayout.Stretch; gamePanel.Padding = new Padding(12, 8, 12, 2);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Color.Transparent };
        layout.RowCount = 2; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sceneNumber.Dock = DockStyle.Fill; sceneNumber.TextAlign = ContentAlignment.MiddleCenter; sceneNumber.Font = new Font("Arial Black", 17); sceneNumber.ForeColor = Color.Yellow; sceneNumber.BackColor = Color.Transparent;
        var title = new Label { Text = "ADIVINA LA PALABRA CON 2 FOTOS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 24), ForeColor = Color.White, BackColor = Color.Transparent };
        var pics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent };
        pics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); pics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82)); pics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        ConfigurePhoto(photo1); ConfigurePhoto(photo2);
        var plus = new Label { Text = "+", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 55), ForeColor = Color.Gold, BackColor = Color.Transparent };
        redPhotoFrame = CreatePhotoFrame(photo1, "MARCO ROJO PNG.png", "red"); pics.Controls.Add(redPhotoFrame, 0, 0);
        var plusImage = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        string plusPath = Path.Combine(AppContext.BaseDirectory, "SIGNO MAS PNG.png");
        if (File.Exists(plusPath)) { using var pi = Image.FromFile(plusPath); plusImage.Image = new Bitmap(pi); pics.Controls.Add(plusImage, 1, 0); } else pics.Controls.Add(plus, 1, 0);
        bluePhotoFrame = CreatePhotoFrame(photo2, "MARCO AZUL PNG.png", "blue"); pics.Controls.Add(bluePhotoFrame, 2, 0);
        var answerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        answerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        answerPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var answerTitle = new Label { Text = "RESPUESTA", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 16), ForeColor = Color.White, BackColor = Color.Transparent, Margin = new Padding(0) };
        answerLabel.Dock = DockStyle.Fill; answerLabel.TextAlign = ContentAlignment.MiddleCenter; answerLabel.BackColor = Color.WhiteSmoke; answerLabel.ForeColor = Color.Red; answerLabel.Font = new Font("Arial Black", 38); answerLabel.AutoEllipsis = true; answerLabel.Margin = new Padding(0);
        answerPanel.Controls.Add(answerTitle, 0, 0); answerPanel.Controls.Add(answerLabel, 0, 1);
        layout.Controls.Add(answerPanel, 0, 0); layout.Controls.Add(pics, 0, 1);
        gamePanel.Controls.Add(layout); root.Controls.Add(gamePanel, 0, 2);

        var barBackground = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 20), Padding = new Padding(12, 5, 12, 5) };
        string barPath = Path.Combine(AppContext.BaseDirectory, "BARRA DE BOTONES.jpg");
        if (File.Exists(barPath)) { using var bi = Image.FromFile(barPath); barBackground.BackgroundImage = new Bitmap(bi); barBackground.BackgroundImageLayout = ImageLayout.Stretch; }
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent, Padding = new Padding(2) }; barBackground.Controls.Add(bar);
        AddButton(bar, "SALIR", Color.White, Color.Black, ExitGame, 78);
        AddButton(bar, "INSTRUCCIONES", Color.MediumPurple, Color.White, Instructions, 112);
        AddButton(bar, "GANA ROJO", Color.Red, Color.White, () => ShowWinner("red"), 88);
        AddButton(bar, "GANA AZUL", Color.RoyalBlue, Color.White, () => ShowWinner("blue"), 88);
        AddButton(bar, "REINICIAR PULSADORES", Color.Purple, Color.White, ResetBuzzers, 126);
        for (int i = 0; i < 4; i++) { int n = i; AddButton(bar, "CELEBRACIÓN " + (i + 1), Color.LawnGreen, Color.Black, () => PlayCelebration(n), 96); }
        AddButton(bar, "DETENER CANCIÓN", Color.Gold, Color.Black, StopAudio, 100);
        AddButton(bar, "NOMBRE", Color.DeepSkyBlue, Color.Black, RevealAnswer, 76);
        AddButton(bar, "PUNTO ROJO", Color.Red, Color.White, () => AddPoint("red"), 88);
        AddButton(bar, "PUNTO AZUL", Color.RoyalBlue, Color.White, () => AddPoint("blue"), 88);
        AddButton(bar, "◀", Color.LightSkyBlue, Color.Black, PreviousScene, 50);
        AddButton(bar, "▶", Color.IndianRed, Color.White, NextScene, 50);
        root.Controls.Add(barBackground, 0, 3);

        status.Visible = false;
        var fullscreen = new Button { Text = "⛶", Width = 54, Height = 32, BackColor = Color.LimeGreen, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font("Arial Black", 15) };
        fullscreen.Location = new Point(ClientSize.Width - 68, 3); fullscreen.Click += (_, _) => ToggleFullscreen(); Controls.Add(fullscreen); fullscreen.BringToFront();
        logoBox.BringToFront();
        Scores(); UpdateClock();
    }

    void BuildTeam(Panel panel, string name, Label score, Color accent)
    {
        panel.Dock = DockStyle.Fill; panel.Margin = new Padding(5); panel.BackColor = Color.Transparent; panel.BackgroundImageLayout = ImageLayout.Zoom;
        string counterFile = name.Contains("ROJO", StringComparison.OrdinalIgnoreCase) ? "CONTADOR ROJO.png" : "CONTADOR AZUL.png";
        string counterPath = Path.Combine(AppContext.BaseDirectory, counterFile);
        if (File.Exists(counterPath)) { using var counterImage = Image.FromFile(counterPath); panel.BackgroundImage = new Bitmap(counterImage); }
        var title = new Label { Text = name, Dock = DockStyle.Top, Height = 42, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 19), ForeColor = accent, BackColor = Color.Transparent };
        score.Dock = DockStyle.Fill; score.TextAlign = ContentAlignment.MiddleCenter; score.Font = new Font("Arial Black", 65); score.ForeColor = Color.White; score.BackColor = Color.Transparent; score.Padding = new Padding(0, 0, 0, 34);
        panel.Controls.Add(score); panel.Controls.Add(title);
        panel.Paint += (_, e) => {
            if (activeTeam != (name.Contains("ROJO", StringComparison.OrdinalIgnoreCase) ? "red" : "blue")) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int pulse = 130 + (int)(Math.Abs(Math.Sin(ledPhase * Math.PI / 12)) * 120);
            using var pen = new Pen(name.Contains("ROJO", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(pulse, 255, 35, 20) : Color.FromArgb(pulse, 30, 145, 255), 7);
            e.Graphics.DrawRectangle(pen, 5, 5, Math.Max(1, panel.Width - 11), Math.Max(1, panel.Height - 11));
        };
    }
    void ConfigurePhoto(PictureBox p) { p.Dock = DockStyle.Fill; p.Margin = new Padding(0); p.BackColor = Color.Transparent; p.BorderStyle = BorderStyle.None; p.SizeMode = PictureBoxSizeMode.Zoom; }
    void AddButton(FlowLayoutPanel bar, string text, Color bg, Color fg, Action action, int? width = null)
    {
        var b = new Button { Text = text, Width = width ?? Math.Max(88, text.Length * 8), Height = 66, BackColor = bg, ForeColor = fg, Font = new Font("Arial Black", 9), FlatStyle = FlatStyle.Flat, Margin = new Padding(3) };
        b.Click += (_, _) => action(); bar.Controls.Add(b);
    }
    void Scores() { redScore.Text = redPoints.ToString(); blueScore.Text = bluePoints.ToString(); }
    void UpdateClock() { timerPanel.Invalidate(); }
    void DrawAnimatedClock(Graphics g, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || timerArtwork == null) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        const float sourceW = 1047f, sourceH = 1008f;
        float scale = Math.Min(bounds.Width / sourceW, bounds.Height / sourceH);
        float ox = bounds.Left + (bounds.Width - sourceW * scale) / 2f;
        float oy = bounds.Top + (bounds.Height - sourceH * scale) / 2f;
        var state = g.Save();
        g.TranslateTransform(ox, oy); g.ScaleTransform(scale, scale);
        // Fondo y animación se pintan juntos en un único búfer, sin parpadeos.
        g.DrawImage(timerArtwork, 0, 0, sourceW, sourceH);
        int lit = Math.Clamp(timerDeciseconds * 6 / 10, 0, 60);
        // Apaga primero las 60 rayitas impresas en la imagen; después enciende solo las que corresponden al tiempo.
        for (int i = 0; i < 60; i++)
        {
            double a = (-90.0 + i * 6.0) * Math.PI / 180.0;
            float cx = 523.5f + (float)Math.Cos(a) * 410f;
            float cy = 504f + (float)Math.Sin(a) * 410f;
            var markState = g.Save();
            g.TranslateTransform(cx, cy); g.RotateTransform((float)(i * 6));
            using (var off = new SolidBrush(Color.FromArgb(255, 65, 65, 67))) g.FillRectangle(off, -12, -40, 24, 80);
            if (i < lit)
            {
                using (var glow = new SolidBrush(Color.FromArgb(85, 0, 255, 100))) g.FillEllipse(glow, -16, -43, 32, 86);
                using (var green = new SolidBrush(Color.FromArgb(255, 0, 245, 90))) g.FillRectangle(green, -11, -39, 22, 78);
            }
            g.Restore(markState);
        }
        // Oculta tanto el 10 como la palabra SEG que vienen impresos en el archivo base.
        using (var dark = new SolidBrush(Color.FromArgb(255, 34, 30, 31))) g.FillRectangle(dark, 350, 345, 350, 430);
        using (var font = new Font("Arial", 184, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(Color.Lime))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(seconds.ToString("00"), font, brush, new RectangleF(350, 350, 350, 275), sf);
        using (var segFont = new Font("Arial", 76, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var segBrush = new SolidBrush(Color.Lime))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString("SEG", segFont, segBrush, new RectangleF(350, 670, 350, 100), sf);
        g.Restore(state);
    }
    void SetStatus(string text, string? team) { status.Text = text; activeTeam = team; ledMessage = team == "red" ? "TURNO PARA EL EQUIPO ROJO" : team == "blue" ? "TURNO PARA EL EQUIPO AZUL" : text; ledPhase = 0; ledPanel.Invalidate(); redPanel.BackColor = Color.Transparent; bluePanel.BackColor = Color.Transparent; }
    void DrawMarquee(Graphics g, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(255, 255, 115, 60));
        using var font = new Font("Consolas", Math.Max(20, bounds.Height * 0.48f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        if (activeTeam != null)
        {
            // Focos recorriendo el contorno completo en círculo, en el color del equipo que ganó el pulsador.
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
        g.DrawString(ledMessage, font, brush, new RectangleF(10, 8, bounds.Width - 20, bounds.Height - 16), sf);
    }
    Panel CreatePhotoFrame(PictureBox photo, string file, string team)
    {
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(54, 58, 54, 58), BackgroundImageLayout = ImageLayout.Stretch };
        string path = Path.Combine(AppContext.BaseDirectory, file);
        if (File.Exists(path)) { using var im = Image.FromFile(path); frame.BackgroundImage = new Bitmap(im); }
        photo.Dock = DockStyle.Fill; photo.Margin = new Padding(0); frame.Controls.Add(photo);
        frame.Paint += (_, e) => { if (activeTeam != team) return; int pulse = 100 + (int)(Math.Abs(Math.Sin(ledPhase * Math.PI / 12)) * 150); using var pen = new Pen(team == "red" ? Color.FromArgb(pulse, 255, 40, 20) : Color.FromArgb(pulse, 20, 130, 255), 8); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.DrawRectangle(pen, 4, 4, Math.Max(1, frame.Width - 9), Math.Max(1, frame.Height - 9)); };
        return frame;
    }
    void StartRound() { if (!started) return; countdown.Stop(); seconds = 10; timerDeciseconds = 0; roundActive = true; buzzLocked = false; UpdateClock(); SetStatus("¡LISTOS! " + redBuzzerKey + " = ROJO  |  " + blueBuzzerKey + " = AZUL", null); countdown.Start(); }
    void Buzz(string team) { if (!started || !roundActive || buzzLocked) return; buzzLocked = true; roundActive = false; countdown.Stop(); SetStatus(team == "red" ? "¡EQUIPO ROJO TIENE EL TURNO PARA RESPONDER!" : "¡EQUIPO AZUL TIENE EL TURNO PARA RESPONDER!", team); }
    void ResetBuzzers() { countdown.Stop(); seconds = 10; timerDeciseconds = 0; roundActive = false; buzzLocked = true; UpdateClock(); SetStatus("PULSADORES REINICIADOS · LISTOS", null); }
    void AddPoint(string team) { if (team == "red") redPoints++; else bluePoints++; Scores(); SetStatus(team == "red" ? "PUNTO PARA EQUIPO ROJO" : "PUNTO PARA EQUIPO AZUL", team); }
    void RevealAnswer() { answerRevealed = true; answerLabel.Text = scenes[currentScene].Answer; }
    void ShowScene(int index, bool startTimer)
    {
        if (scenes.Count == 0) return;
        currentScene = ((index % scenes.Count) + scenes.Count) % scenes.Count; var s = scenes[currentScene];
        sceneNumber.Text = "ESCENA " + (currentScene + 1) + " / " + scenes.Count;
        answerRevealed = false; answerLabel.Text = "¿ ? ¿ ?"; SetImage(photo1, s.Image1); SetImage(photo2, s.Image2); SetSceneBackground(s.Background);
        ResetBuzzers(); if (startTimer && started) StartRound();
    }
    void SetImage(PictureBox box, string path)
    {
        var old = box.Image; box.Image = null; old?.Dispose();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) { using var img = Image.FromFile(path); box.Image = new Bitmap(img); }
    }
    void SetSceneBackground(string path)
    {
        var old = currentBackground; currentBackground = null; gamePanel.BackgroundImage = null; old?.Dispose();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) { using var img = Image.FromFile(path); currentBackground = new Bitmap(img); }
        gamePanel.BackgroundImage = currentBackground;
        if (Controls.Count > 0 && Controls[0] is TableLayoutPanel root) { root.BackgroundImage = currentBackground; root.BackgroundImageLayout = ImageLayout.Stretch; }
        BackgroundImage = currentBackground; BackgroundImageLayout = ImageLayout.Stretch;
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
        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath)) { try { logoBox.Image = Image.FromFile(logoPath); } catch { } }
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
    void SetImageLogo() { if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath)) { var old = logoBox.Image; using var img = Image.FromFile(logoPath); logoBox.Image = new Bitmap(img); old?.Dispose(); } }
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
    protected override void OnFormClosed(FormClosedEventArgs e) { countdown.Stop(); ledAnimation.Stop(); StopAudio(); currentBackground?.Dispose(); timerArtwork?.Dispose(); speech.Dispose(); base.OnFormClosed(e); }
}