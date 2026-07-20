// ═══════════════════════════════════════════════════════════════════
//  MainForm.cs — Settings & Control GUI
//  Dropdowns for audio source (mic or speaker loopback), AI model
//  and language; start/stop continuous mode; live log panel.
// ═══════════════════════════════════════════════════════════════════

namespace AsrService;

public sealed class MainForm : Form
{
    private readonly AsrController _controller;

    // ── Controls ─────────────────────────────────────────────────
    private readonly ComboBox _sourceCombo   = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _modelCombo    = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _languageCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _vadCombo      = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _loadButton      = new() { Text = "Download && Load Model", Dock = DockStyle.Fill, Height = 34 };
    private readonly Button _toggleButton    = new() { Text = "● Start Continuous Recording", Dock = DockStyle.Fill, Height = 34, Enabled = false };
    private readonly Button _fileButton      = new() { Text = "📄 Transcribe File (video / audio)…", Dock = DockStyle.Fill, Height = 34, Enabled = false };
    private readonly Label _modelNotes       = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoSize = false };
    private readonly TextBox _logBox         = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f),
        BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.Gainsboro,
    };
    private readonly Label _statusLabel = new() { Text = "Model not loaded", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NotifyIcon _trayIcon = new();

    private bool _busy = false;
    private readonly float _scale = 1f;

    /// <summary>Scale a 96-DPI pixel value to the monitor's DPI.</summary>
    private int S(int v) => (int)(v * _scale + 0.5f);

    public MainForm(AsrController controller)
    {
        _controller = controller;

        Text = "ASR Service — Speech to Text";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        // All layout constants below are in 96-DPI units — scale them for
        // the actual monitor DPI (e.g. ×2 at 200% display scaling), else
        // labels truncate and rows overlap on high-DPI screens.
        _scale = DeviceDpi / 96f;
        MinimumSize = new Size(S(680), S(640));
        Size = new Size(S(760), S(720));

        BuildLayout();
        PopulateCombos();
        HookEvents();
        SetupTray();

        // Route Console output (used by all components) into the log box
        Console.SetOut(new LogBoxWriter(this, isError: false));
        Console.SetError(new LogBoxWriter(this, isError: true));

        // Show the active audio source on startup
        AppendLog($"[GUI] Audio source: {_sourceCombo.SelectedItem}", false);
    }

    // ── Layout ───────────────────────────────────────────────────

    private void BuildLayout()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10,
            Padding = new Padding(S(20), S(16), S(20), S(12)),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(130)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Breathing room between rows: every control gets a vertical margin.
        void Pad(Control c) => c.Margin = new Padding(S(4), S(6), S(4), S(6));

        int row = 0;
        void AddRow(string label, Control control, int height = 42)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, S(height)));
            var lbl = new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            Pad(lbl);
            Pad(control);
            table.Controls.Add(lbl, 0, row);
            table.Controls.Add(control, 1, row);
            row++;
        }

        AddRow("Audio Source:", _sourceCombo);
        AddRow("AI Model:", _modelCombo);
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, S(52)));
        Pad(_modelNotes);
        table.Controls.Add(_modelNotes, 1, row); row++;
        AddRow("Language:", _languageCombo);
        AddRow("VAD Engine:", _vadCombo);
        AddRow("", _loadButton, 52);
        AddRow("", _toggleButton, 52);
        AddRow("", _fileButton, 52);

        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _logBox.Margin = new Padding(S(4), S(12), S(4), S(6));
        table.SetColumnSpan(_logBox, 2);
        table.Controls.Add(_logBox, 0, row); row++;

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
        Pad(_statusLabel);
        table.SetColumnSpan(_statusLabel, 2);
        table.Controls.Add(_statusLabel, 0, row);

        Controls.Add(table);
    }

    private void PopulateCombos()
    {
        // Audio sources: microphones + speaker (loopback) entries
        foreach (var dev in AudioRecorder.ListDevices())
            _sourceCombo.Items.Add(dev);

        var settings = _controller.Settings;
        int savedIdx = -1;
        for (int i = 0; i < _sourceCombo.Items.Count; i++)
        {
            var d = (AudioDeviceInfo)_sourceCombo.Items[i]!;
            if (d.Id == settings.AudioDeviceId && d.IsLoopback == settings.AudioDeviceIsLoopback)
            { savedIdx = i; break; }
        }
        _sourceCombo.SelectedIndex = savedIdx >= 0 ? savedIdx : 0;

        // Models
        foreach (var model in ModelRegistry.Models)
            _modelCombo.Items.Add(model);
        _modelCombo.SelectedItem = ModelRegistry.GetById(settings.ModelId);

        // Languages — the list depends on the selected model (Parakeet = English only)
        RefreshLanguageOptions();

        // VAD engines
        foreach (var (code, display) in VadCatalog.Engines)
            _vadCombo.Items.Add(new VadItem(code, display));
        _vadCombo.SelectedIndex = Math.Max(0,
            Array.FindIndex(VadCatalog.Engines, v => v.Code == settings.VadEngine));

        UpdateModelNotes();
        RefreshLoadButtonState();
    }

    /// <summary>
    /// Rebuilds the language dropdown for the selected model. Parakeet TDT v2
    /// is English-only, so only "English" is offered (and the box is disabled).
    /// Multilingual models (Nemotron 3.5) get the full language list.
    /// </summary>
    private void RefreshLanguageOptions()
    {
        var model = (ModelInfo)_modelCombo.SelectedItem!;
        var settings = _controller.Settings;

        _languageCombo.Items.Clear();

        if (!model.Multilingual)
        {
            // English-only model — offer English alone.
            _languageCombo.Items.Add(new LanguageItem("en", "English"));
            _languageCombo.SelectedIndex = 0;
            _languageCombo.Enabled = false;
            settings.Language = "en";
            settings.Save();
            _controller.Settings.Language = "en";
            return;
        }

        _languageCombo.Enabled = true;
        foreach (var (code, display) in LanguageCatalog.Languages)
            _languageCombo.Items.Add(new LanguageItem(code, display));
        int idx = Array.FindIndex(LanguageCatalog.Languages, l => l.Code == settings.Language);
        _languageCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void HookEvents()
    {
        _sourceCombo.SelectedIndexChanged += (_, _) =>
        {
            var dev = (AudioDeviceInfo)_sourceCombo.SelectedItem!;
            var s = _controller.Settings;
            s.AudioDeviceId = dev.Id;
            s.AudioDeviceIsLoopback = dev.IsLoopback;
            s.AudioDeviceName = dev.Name;
            s.Save();
            AppendLog($"[GUI] Audio source: {dev}", false);
            if (dev.IsLoopback)
                AppendLog("[GUI] Speaker loopback records only what this PC plays (e.g. the other person on a call).", false);
        };

        _modelCombo.SelectedIndexChanged += (_, _) =>
        {
            var model = (ModelInfo)_modelCombo.SelectedItem!;
            _controller.Settings.ModelId = model.Id;
            _controller.Settings.Save();
            UpdateModelNotes();
            RefreshLanguageOptions(); // Parakeet → English only; Nemotron → full list
            RefreshLoadButtonState();
        };

        _languageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_languageCombo.SelectedItem is not LanguageItem lang) return;
            _controller.Settings.Language = lang.Code;
            _controller.Settings.Save();
        };

        _vadCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_vadCombo.SelectedItem is not VadItem vad) return;
            _controller.Settings.VadEngine = vad.Code;
            _controller.Settings.Save();
            AppendLog($"[GUI] VAD engine: {vad.Display}", false);
        };

        _loadButton.Click += async (_, _) => await LoadModelAsync();

        _fileButton.Click += async (_, _) => await TranscribeFileAsync();

        _toggleButton.Click += (_, _) =>
        {
            _controller.ToggleContinuousMode();
        };

        _controller.ContinuousModeChanged += running => BeginInvoke(() =>
        {
            _toggleButton.Text = running ? "■ Stop Continuous Recording" : "● Start Continuous Recording";
            _sourceCombo.Enabled = _modelCombo.Enabled = _vadCombo.Enabled = !running;
            // Language box stays disabled for English-only models even when idle.
            _languageCombo.Enabled = !running && ((ModelInfo)_modelCombo.SelectedItem!).Multilingual;
            RefreshLoadButtonState(); // keeps the button disabled if the model is already downloaded
            _statusLabel.Text = running
                ? $"● Recording continuously from: {_controller.Settings.AudioDeviceName}"
                : "Ready — hold Right Alt to talk, double-tap for continuous mode";
        });

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // Minimize to tray instead of exiting — hotkeys keep working
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(1500, "ASR Service",
                    "Still running in the tray. Right Alt push-to-talk stays active.", ToolTipIcon.Info);
            }
        };
    }

    private void UpdateModelNotes()
    {
        var model = (ModelInfo)_modelCombo.SelectedItem!;
        _modelNotes.Text = model.Notes;
        _modelNotes.ForeColor = model.Loader == ModelLoader.Unsupported ? Color.Firebrick
                              : model.Experimental ? Color.DarkOrange : Color.DimGray;
    }

    /// <summary>
    /// The "Download &amp; Load Model" button is only useful when the selected
    /// model still needs downloading. Once it's present on disk (it auto-loads
    /// on launch), disable the button. Also disabled while busy / recording /
    /// for models that can't run locally.
    /// </summary>
    private void RefreshLoadButtonState()
    {
        var model = (ModelInfo)_modelCombo.SelectedItem!;
        bool downloaded = model.Loader != ModelLoader.Unsupported && ModelRegistry.IsModelPresent(model);

        _loadButton.Enabled = !_busy
                              && !_controller.IsContinuousMode
                              && model.Loader != ModelLoader.Unsupported
                              && !downloaded;

        _loadButton.Text = downloaded ? "Model downloaded ✓" : "Download && Load Model";
    }

    /// <summary>Used by Program.cs to load an already-downloaded model on startup.</summary>
    public Task AutoLoadModelAsync() => LoadModelAsync();

    private async Task LoadModelAsync()
    {
        if (_busy) return;
        _busy = true;
        _loadButton.Enabled = false;
        _toggleButton.Enabled = false;
        _statusLabel.Text = "Downloading / loading model...";

        try
        {
            await _controller.PrepareModelAsync();
            _toggleButton.Enabled = true;
            _fileButton.Enabled = true;
            _statusLabel.Text = "Ready — hold Right Alt to talk, double-tap for continuous mode";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}", true);
            _statusLabel.Text = "Model load failed — see log";
            MessageBox.Show(this, ex.Message, "Model load failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            RefreshLoadButtonState(); // stays disabled if the model is now present
        }
    }

    private async Task TranscribeFileAsync()
    {
        if (_busy) return;

        using var dialog = new OpenFileDialog
        {
            Title = "Select a video or audio file to transcribe",
            Filter = "Media files|*.mp3;*.mp4;*.wav;*.m4a;*.mkv;*.avi;*.mov;*.flac;*.ogg;*.opus;*.webm;*.aac;*.wma;*.wmv;*.3gp;*.ts|" +
                     "All files|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string path = dialog.FileName;
        _busy = true;
        _fileButton.Enabled = _loadButton.Enabled = _toggleButton.Enabled = false;
        _statusLabel.Text = $"Transcribing {Path.GetFileName(path)}...";

        try
        {
            string text = await Task.Run(() => _controller.TranscribeFileAsync(path));

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "No speech was detected in this file.",
                    "Transcription complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                string outPath = Path.ChangeExtension(path, ".transcript.txt");
                var open = MessageBox.Show(this,
                    $"Transcript saved to:\n{outPath}\n\nOpen it now?",
                    "Transcription complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outPath) { UseShellExecute = true });
            }
            _statusLabel.Text = "Ready — hold Right Alt to talk, double-tap for continuous mode";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] File transcription failed: {ex.Message}", true);
            _statusLabel.Text = "File transcription failed — see log";
            MessageBox.Show(this, ex.Message, "Transcription failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            _fileButton.Enabled = _loadButton.Enabled = true;
            _toggleButton.Enabled = _controller.IsModelLoaded;
        }
    }

    // ── Tray icon ────────────────────────────────────────────────

    private void SetupTray()
    {
        _trayIcon.Icon = SystemIcons.Application;
        _trayIcon.Text = "ASR Service";
        _trayIcon.Visible = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Window", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            _controller.Dispose();
            Application.Exit();
        });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
    }

    // ── Logging ──────────────────────────────────────────────────

    private readonly List<string> _pendingLogs = new();

    internal void AppendLog(string text, bool isError)
    {
        if (IsDisposed) return;

        // Console output can arrive before Application.Run() creates the
        // window handle (e.g. the startup banner) — BeginInvoke would throw.
        // Buffer those lines and flush them once the handle exists.
        if (!IsHandleCreated)
        {
            lock (_pendingLogs) _pendingLogs.Add(text);
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (_logBox.TextLength > 200_000)
                    _logBox.Clear(); // keep the log bounded

                _logBox.AppendText(text + Environment.NewLine);
            });
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (InvalidOperationException) { /* handle torn down mid-write */ }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        lock (_pendingLogs)
        {
            foreach (string line in _pendingLogs)
                _logBox.AppendText(line + Environment.NewLine);
            _pendingLogs.Clear();
        }
    }

    /// <summary>TextWriter that mirrors Console output into the log box.</summary>
    private sealed class LogBoxWriter : System.IO.TextWriter
    {
        private readonly MainForm _form;
        private readonly bool _isError;
        private readonly System.Text.StringBuilder _line = new();

        public LogBoxWriter(MainForm form, bool isError) { _form = form; _isError = isError; }

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                string text = _line.ToString().TrimEnd('\r');
                _line.Clear();
                if (text.Length > 0) _form.AppendLog(text, _isError);
            }
            else
            {
                _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            foreach (char c in value) Write(c);
        }
    }

    private sealed record LanguageItem(string Code, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record VadItem(string Code, string Display)
    {
        public override string ToString() => Display;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _trayIcon.Dispose();
        base.Dispose(disposing);
    }
}
