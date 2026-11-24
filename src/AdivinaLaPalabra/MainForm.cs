using NAudio.Wave;
using System.Speech.Synthesis;
using System.Text.Json;

namespace AdivinaLaPalabra;

public sealed class MainForm : Form
{
    sealed class Scene { public string Answer { get; set; } = ""; public string Image1 { get; set; } = ""; public string Image2 { get; set; } = ""; }
    sealed class SavedConfig { public string[] Audios { get; set; } = new string[4]; public string Logo { get; set; } = ""; public List<Scene> Scenes { get; set; } = new(); }

    readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesafiosLocos", "AdivinaLaPalabra");
    readonly List<Scene> scenes = new();
    readonly string?[] audios = new string?[4];
    readonly Color orange = Color.FromArgb(239, 57, 8);
    readonly Label redScore = new(), blueScore = new(), clock = new(), answerLabel = new(), sceneNumber = new(), status = new();
    readonly PictureBox photo1 = new(), photo2 = new(), logoBox = new();
    readonly Panel redPanel = new(), bluePanel = new(), timerPanel = new(), gamePanel = new(), startScreen = new(), winnerScreen = new();
    readonly System.Windows.Forms.Timer countdown = new() { Interval = 1000 };
    readonly SpeechSynthesizer speech = new();
    WaveOutEvent? player;
    AudioFileReader? audio;
    string? winner, logoPath;
    int seconds = 10, redPoints, bluePoints, currentScene;
    bool roundActive, buzzLocked = true, answerRevealed, settingsOpen, started, isFullscreen;
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
        countdown.Tick += (_, _) => { seconds--; UpdateClock(); if (seconds <= 0) { countdown.Stop(); roundActive = false; buzzLocked = true; SetStatus("¡TIEMPO! SE ACABÓ LA RONDA", null); } };
        ShowStartScreen();
    }

    void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), BackColor = orange };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); Controls.Add(root);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        BuildTeam(redPanel, "EQUIPO ROJO", redScore, Color.LightCoral);
        BuildTeam(bluePanel, "EQUIPO AZUL", blueScore, Color.DeepSkyBlue);
        var center = new Panel { Dock = DockStyle.Fill };
        logoBox.SizeMode = PictureBoxSizeMode.Zoom; logoBox.Dock = DockStyle.Left; logoBox.Width = 120; logoBox.BackColor = Color.Transparent;
        var brand = new Label { Text = "DESAFÍOS\nLOCOS", Dock = DockStyle.Left, Width = 115, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 20), ForeColor = Color.Gold };
        timerPanel.Dock = DockStyle.Fill; timerPanel.BackColor = Color.FromArgb(25, 25, 25);
        timerPanel.Paint += (_, e) => { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var p = new Pen(Color.Lime, 8); e.Graphics.DrawArc(p, 7, 7, timerPanel.Width - 14, timerPanel.Height - 14, -90, 360f * seconds / 10f); };
        clock.Dock = DockStyle.Fill; clock.TextAlign = ContentAlignment.MiddleCenter; clock.Font = new Font("Arial Black", 28); clock.ForeColor = Color.Lime;
        timerPanel.Controls.Add(clock); center.Controls.Add(timerPanel); center.Controls.Add(brand); center.Controls.Add(logoBox);
        top.Controls.Add(redPanel, 0, 0); top.Controls.Add(center, 1, 0); top.Controls.Add(bluePanel, 2, 0); root.Controls.Add(top, 0, 0);

        gamePanel.Dock = DockStyle.Fill; gamePanel.BackColor = Color.FromArgb(225, 255, 215, 190); gamePanel.Padding = new Padding(12);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        sceneNumber.Dock = DockStyle.Fill; sceneNumber.TextAlign = ContentAlignment.MiddleCenter; sceneNumber.Font = new Font("Arial Black", 17); sceneNumber.ForeColor = Color.Yellow;
        var title = new Label { Text = "ADIVINA LA PALABRA CON 2 FOTOS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 24), ForeColor = Color.White };
        var pics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        pics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); pics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82)); pics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        ConfigurePhoto(photo1); ConfigurePhoto(photo2);
        var plus = new Label { Text = "+", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 55), ForeColor = Color.Gold };
        pics.Controls.Add(photo1, 0, 0); pics.Controls.Add(plus, 1, 0); pics.Controls.Add(photo2, 2, 0);
        var answerPanel = new Panel { Dock = DockStyle.Fill };
        var answerTitle = new Label { Text = "RESPUESTA", Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 20), ForeColor = Color.White };
        answerLabel.Dock = DockStyle.Fill; answerLabel.TextAlign = ContentAlignment.MiddleCenter; answerLabel.BackColor = Color.WhiteSmoke; answerLabel.ForeColor = Color.Red; answerLabel.Font = new Font("Arial Black", 48);
        answerPanel.Controls.Add(answerLabel); answerPanel.Controls.Add(answerTitle);
        layout.Controls.Add(sceneNumber, 0, 0); layout.Controls.Add(title, 0, 1); layout.Controls.Add(pics, 0, 2); layout.Controls.Add(answerPanel, 0, 3);
        gamePanel.Controls.Add(layout); root.Controls.Add(gamePanel, 0, 1);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = Color.FromArgb(90, 0, 0, 0), Padding = new Padding(2) };
        AddButton(bar, "SALIR", Color.White, Color.Black, ExitGame);
        AddButton(bar, "INSTRUCCIONES DEL JUEGO", Color.MediumPurple, Color.White, Instructions);
        AddButton(bar, "GANA ROJO", Color.Red, Color.White, () => ShowWinner("red"));
        AddButton(bar, "GANA AZUL", Color.RoyalBlue, Color.White, () => ShowWinner("blue"));
        AddButton(bar, "REINICIAR PULSADORES", Color.Purple, Color.White, ResetBuzzers);
        for (int i = 0; i < 4; i++) { int n = i; AddButton(bar, "CELEBRACIÓN " + (i + 1), Color.LawnGreen, Color.Black, () => PlayCelebration(n)); }
        AddButton(bar, "DETENER CANCIÓN", Color.Gold, Color.Black, StopAudio);
        AddButton(bar, "NOMBRE", Color.DeepSkyBlue, Color.Black, RevealAnswer);
        AddButton(bar, "PUNTO ROJO", Color.Red, Color.White, () => AddPoint("red"));
        AddButton(bar, "PUNTO AZUL", Color.RoyalBlue, Color.White, () => AddPoint("blue"));
        AddButton(bar, "◀", Color.LightSkyBlue, Color.Black, PreviousScene, 62);
        AddButton(bar, "▶", Color.IndianRed, Color.White, NextScene, 62);
        root.Controls.Add(bar, 0, 2);

        status.Dock = DockStyle.Bottom; status.Height = 34; status.TextAlign = ContentAlignment.MiddleCenter; status.Font = new Font("Arial Black", 14); status.BackColor = Color.FromArgb(175, 0, 0, 0); gamePanel.Controls.Add(status); status.BringToFront();
        var fullscreen = new Button { Text = "⛶", Width = 54, Height = 32, BackColor = Color.LimeGreen, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font("Arial Black", 15) };
        fullscreen.Location = new Point(ClientSize.Width - 68, 3); fullscreen.Click += (_, _) => ToggleFullscreen(); Controls.Add(fullscreen); fullscreen.BringToFront();
        logoBox.BringToFront();
        Scores(); UpdateClock();
    }

    void BuildTeam(Panel panel, string name, Label score, Color accent)
    {
        panel.Dock = DockStyle.Fill; panel.Margin = new Padding(5); panel.BackColor = Color.FromArgb(45, 8, 16);
        var title = new Label { Text = name, Dock = DockStyle.Top, Height = 42, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 19), ForeColor = accent };
        score.Dock = DockStyle.Fill; score.TextAlign = ContentAlignment.MiddleCenter; score.Font = new Font("Arial Black", 65);
        var icon = new Label { Text = "●", Dock = DockStyle.Left, Width = 65, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial", 40), ForeColor = accent };
        panel.Controls.Add(score); panel.Controls.Add(icon); panel.Controls.Add(title);
    }
    void ConfigurePhoto(PictureBox p) { p.Dock = DockStyle.Fill; p.Margin = new Padding(5); p.BackColor = Color.FromArgb(40, 40, 40); p.BorderStyle = BorderStyle.FixedSingle; p.SizeMode = PictureBoxSizeMode.Zoom; }
    void AddButton(FlowLayoutPanel bar, string text, Color bg, Color fg, Action action, int? width = null)
    {
        var b = new Button { Text = text, Width = width ?? Math.Max(88, text.Length * 8), Height = 66, BackColor = bg, ForeColor = fg, Font = new Font("Arial Black", 9), FlatStyle = FlatStyle.Flat, Margin = new Padding(3) };
        b.Click += (_, _) => action(); bar.Controls.Add(b);
    }
    void Scores() { redScore.Text = redPoints.ToString(); blueScore.Text = bluePoints.ToString(); }
    void UpdateClock() { clock.Text = seconds + "\nSEG"; timerPanel.Invalidate(); }
    void SetStatus(string text, string? team) { status.Text = text; redPanel.BackColor = team == "red" ? Color.Firebrick : Color.FromArgb(45, 8, 16); bluePanel.BackColor = team == "blue" ? Color.RoyalBlue : Color.FromArgb(8, 28, 60); }
    void StartRound() { if (!started) return; countdown.Stop(); seconds = 10; roundActive = true; buzzLocked = false; UpdateClock(); SetStatus("¡LISTOS! Q = ROJO  |  P = AZUL", null); countdown.Start(); }
    void Buzz(string team) { if (!started || !roundActive || buzzLocked) return; buzzLocked = true; roundActive = false; countdown.Stop(); SetStatus(team == "red" ? "¡EQUIPO ROJO TIENE EL TURNO PARA RESPONDER!" : "¡EQUIPO AZUL TIENE EL TURNO PARA RESPONDER!", team); }
    void ResetBuzzers() { countdown.Stop(); seconds = 10; roundActive = false; buzzLocked = true; UpdateClock(); SetStatus("PULSADORES REINICIADOS · ENTER PARA INICIAR EL TIEMPO", null); }
    void AddPoint(string team) { if (team == "red") redPoints++; else bluePoints++; Scores(); SetStatus(team == "red" ? "PUNTO PARA EQUIPO ROJO" : "PUNTO PARA EQUIPO AZUL", team); }
    void RevealAnswer() { answerRevealed = true; answerLabel.Text = scenes[currentScene].Answer; }
    void ShowScene(int index, bool startTimer)
    {
        if (scenes.Count == 0) return;
        currentScene = Math.Clamp(index, 0, scenes.Count - 1); var s = scenes[currentScene];
        sceneNumber.Text = "ESCENA " + (currentScene + 1) + " / " + scenes.Count;
        answerRevealed = false; answerLabel.Text = "¿ ? ¿ ?"; SetImage(photo1, s.Image1); SetImage(photo2, s.Image2);
        ResetBuzzers(); if (startTimer && started) StartRound();
    }
    void SetImage(PictureBox box, string path)
    {
        var old = box.Image; box.Image = null; old?.Dispose();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) { using var img = Image.FromFile(path); box.Image = new Bitmap(img); }
    }
    void PreviousScene() { if (currentScene > 0) ShowScene(currentScene - 1, true); }
    void NextScene() { if (currentScene < scenes.Count - 1) ShowScene(currentScene + 1, true); }
    void ShowStartScreen()
    {
        startScreen.Dock = DockStyle.Fill; startScreen.BackColor = Color.FromArgb(235, 50, 8); startScreen.BringToFront();
        var title = new Label { Text = "ADIVINA LA PALABRA\nCON 2 FOTOS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 42), ForeColor = Color.White };
        var hint = new Label { Text = "PRESIONA ENTER PARA COMENZAR", Dock = DockStyle.Bottom, Height = 105, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial Black", 25), ForeColor = Color.Yellow };
        startScreen.Controls.Add(title); startScreen.Controls.Add(hint); Controls.Add(startScreen); startScreen.BringToFront();
        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath)) { try { logoBox.Image = Image.FromFile(logoPath); } catch { } }
    }
    void BeginGame() { if (started) return; started = true; startScreen.Visible = false; ShowScene(currentScene, true); }
    void ShowWinner(string team)
    {
        countdown.Stop(); roundActive = false; buzzLocked = true; winner = team;
        winnerScreen.Controls.Clear(); winnerScreen.Dock = DockStyle.Fill; winnerScreen.BackColor = team == "red" ? Color.DarkRed : Color.DarkBlue;
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
        if (key == Keys.M) { OpenSettings(); return true; }
        if (key == Keys.Q) { Buzz("red"); return true; }
        if (key == Keys.P) { Buzz("blue"); return true; }
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
        tabs.TabPages.AddRange(new[] { musicTab, logoTab, scenesTab }); f.Controls.Add(tabs);
        var musicPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), WrapContents = false };
        musicTab.Controls.Add(musicPanel);
        musicPanel.Controls.Add(new Label { Text = "ASIGNA UNA CANCIÓN DIFERENTE A CADA BOTÓN VERDE", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 12), Margin = new Padding(4, 10, 4, 18) });
        for (int i = 0; i < 4; i++) { int n = i; var b = new Button { Text = "CARGAR MP3 PARA CELEBRACIÓN " + (i + 1), Width = 590, Height = 54, BackColor = Color.LawnGreen, ForeColor = Color.Black, Font = new Font("Arial Black", 10) }; b.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Archivos MP3|*.mp3" }; if (d.ShowDialog(f) == DialogResult.OK) { audios[n] = CopyToData(d.FileName, "celebracion" + (n + 1) + Path.GetExtension(d.FileName)); b.Text = "CELEBRACIÓN " + (n + 1) + " · " + Path.GetFileName(d.FileName); SaveConfig(); } }; musicPanel.Controls.Add(b); }
        var logoPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20) }; logoTab.Controls.Add(logoPanel);
        logoPanel.Controls.Add(new Label { Text = "CAMBIAR LOGOTIPO DE DESAFÍOS LOCOS (PNG)", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 13) });
        var logoButton = new Button { Text = "SELECCIONAR LOGOTIPO PNG", Width = 590, Height = 55 }; logoButton.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Imagen PNG|*.png" }; if (d.ShowDialog(f) == DialogResult.OK) { logoPath = CopyToData(d.FileName, "logo-desafios-locos.png"); SetImageLogo(); SaveConfig(); MessageBox.Show(f, "Logotipo guardado. Se mostrará en la pantalla del juego."); } }; logoPanel.Controls.Add(logoButton);
        var scenePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18), AutoScroll = true, WrapContents = false }; scenesTab.Controls.Add(scenePanel);
        scenePanel.Controls.Add(new Label { Text = "ADMINISTRAR PREGUNTAS Y FOTOGRAFÍAS", AutoSize = true, ForeColor = Color.Gold, Font = new Font("Arial Black", 12) });
        for (int i = 0; i < scenes.Count; i++) { int n = i; var b = new Button { Text = "EDITAR ESCENA " + (i + 1), Width = 590, Height = 42 }; b.Click += (_, _) => EditScene(n, f); scenePanel.Controls.Add(b); }
        var add = new Button { Text = "+ AGREGAR NUEVA ESCENA", Width = 590, Height = 48, BackColor = Color.LimeGreen }; add.Click += (_, _) => { scenes.Add(new Scene { Answer = "Nueva respuesta" }); SaveConfig(); f.Close(); ShowScene(scenes.Count - 1, false); OpenSettings(); }; scenePanel.Controls.Add(add);
        var close = new Button { Text = "GUARDAR Y CERRAR MENÚ", Dock = DockStyle.Bottom, Height = 55, BackColor = Color.Gold, ForeColor = Color.Black, Font = new Font("Arial Black", 12) };
        close.Click += (_, _) => { SaveConfig(); f.Close(); };
        f.Controls.Add(close); close.BringToFront();
        f.FormClosed += (_, _) => settingsOpen = false; f.ShowDialog(this);
    }
    void EditScene(int index, Form owner)
    {
        var s = scenes[index];
        using var f = new Form { Text = "Editar escena " + (index + 1), StartPosition = FormStartPosition.CenterParent, Size = new Size(520, 330), BackColor = Color.FromArgb(35, 35, 42) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(15), WrapContents = false }; f.Controls.Add(panel);
        var answerBox = new TextBox { Text = s.Answer, Width = 450 };
        panel.Controls.Add(new Label { Text = "RESPUESTA (se mantiene oculta hasta presionar NOMBRE)", ForeColor = Color.White, AutoSize = true }); panel.Controls.Add(answerBox);
        var b1 = new Button { Text = "CAMBIAR FOTO 1", Width = 450, Height = 42 }; b1.Click += (_, _) => { var path = PickImage(); if (path != null) s.Image1 = CopyToData(path, "escena" + (index + 1) + "_foto1" + Path.GetExtension(path)); }; panel.Controls.Add(b1);
        var b2 = new Button { Text = "CAMBIAR FOTO 2", Width = 450, Height = 42 }; b2.Click += (_, _) => { var path = PickImage(); if (path != null) s.Image2 = CopyToData(path, "escena" + (index + 1) + "_foto2" + Path.GetExtension(path)); }; panel.Controls.Add(b2);
        var save = new Button { Text = "GUARDAR ESCENA", Width = 450, Height = 44, BackColor = Color.LimeGreen }; save.Click += (_, _) => { s.Answer = answerBox.Text; SaveConfig(); if (index == currentScene) ShowScene(index, false); f.Close(); }; panel.Controls.Add(save);
        f.ShowDialog(owner);
    }
    string? PickImage() { using var d = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp" }; return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; }
    string CopyToData(string source, string name) { var dest = Path.Combine(dataDir, name); File.Copy(source, dest, true); return dest; }
    void SetImageLogo() { if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath)) { var old = logoBox.Image; using var img = Image.FromFile(logoPath); logoBox.Image = new Bitmap(img); old?.Dispose(); } }
    void LoadConfig()
    {
        try { var path = Path.Combine(dataDir, "config.json"); if (!File.Exists(path)) return; var c = JsonSerializer.Deserialize<SavedConfig>(File.ReadAllText(path)); if (c == null) return; logoPath = c.Logo; for (int i = 0; i < 4 && i < c.Audios.Length; i++) audios[i] = c.Audios[i]; scenes.Clear(); scenes.AddRange(c.Scenes); }
        catch { }
    }
    void SaveConfig()
    {
        try { var c = new SavedConfig { Logo = logoPath ?? "", Audios = audios.Select(x => x ?? "").ToArray(), Scenes = scenes }; File.WriteAllText(Path.Combine(dataDir, "config.json"), JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    void PlayCelebration(int n)
    {
        if (string.IsNullOrWhiteSpace(audios[n]) || !File.Exists(audios[n])) { MessageBox.Show(this, "Carga el MP3 de esta celebración desde el menú M.", "Desafíos Locos"); return; }
        StopAudio(); try { audio = new AudioFileReader(audios[n]!); player = new WaveOutEvent(); player.Init(audio); player.Play(); } catch (Exception ex) { MessageBox.Show(this, "No se pudo reproducir el audio: " + ex.Message); }
    }
    void StopAudio() { try { player?.Stop(); } catch { } player?.Dispose(); player = null; audio?.Dispose(); audio = null; }
    void Instructions()
    {
        const string text = "Bienvenidos a Adivina la Palabra con dos Fotos. Presiona Enter para comenzar. El equipo rojo utiliza la tecla Q y el equipo azul utiliza la tecla P. El primer equipo que presione su pulsador obtiene el turno para responder. El botón Nombre revela la respuesta. Las flechas cambian de escena. Los botones Punto Rojo y Punto Azul suman un punto. Los botones Gana Rojo y Gana Azul muestran al equipo ganador. Los cuatro botones verdes reproducen canciones de celebración. El botón Detener Canción detiene la música. La tecla M abre el menú de configuración.";
        try { speech.SpeakAsyncCancelAll(); speech.SpeakAsync(text); } catch { MessageBox.Show(this, text, "Instrucciones del juego"); }
    }
    void ExitGame() { if (MessageBox.Show(this, "¿Deseas salir del juego?", "Desafíos Locos", MessageBoxButtons.YesNo) == DialogResult.Yes) Close(); }
    protected override void OnFormClosed(FormClosedEventArgs e) { countdown.Stop(); StopAudio(); speech.Dispose(); base.OnFormClosed(e); }
}