using NAudio.CoreAudioApi;

namespace SimpleBassShakerRouter;

sealed class MainForm : Form
{
    readonly AudioEngine _engine = new();
    readonly UserSettings _settings = SettingsStore.Load();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    readonly ComboBox _sourceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly ComboBox _shakerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly TrackBar _cutoff = new() { Minimum = 30, Maximum = 200, TickFrequency = 10, TickStyle = TickStyle.None, Dock = DockStyle.Fill };
    readonly TrackBar _level = new() { Minimum = 0, Maximum = 200, TickFrequency = 20, TickStyle = TickStyle.None, Dock = DockStyle.Fill };
    readonly Label _cutoffValue = new() { TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
    readonly Label _levelValue = new() { TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
    readonly ProgressBar _meter = new() { Minimum = 0, Maximum = 100, Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
    readonly Label _bufferValue = new()
    {
        Text = "Buffer —",
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight
    };
    readonly Label _signalLabel = new()
    {
        Text = "Signal",
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };
    readonly CheckBox _liveView = new()
    {
        Text = "Live view (last 5 s)",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 4, 0, 4),
        Padding = new Padding(0),
        CheckAlign = ContentAlignment.MiddleLeft,
        TextAlign = ContentAlignment.MiddleLeft
    };
    readonly BandViewPanel _bandView = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
    readonly Button _startButton = new() { Text = "Start", AutoSize = true, Padding = new Padding(16, 6, 16, 6) };
    readonly Button _refreshButton = new() { Text = "Refresh devices", AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
    readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly TableLayoutPanel _layout = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 10,
        Padding = new Padding(16)
    };
    bool _loading = true;
    bool _uiReady;
    DateTime _clipUntil = DateTime.MinValue;

    public MainForm()
    {
        Text = "Simple Bass Shaker Router";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        AcceptButton = _startButton;
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
                Icon = new Icon(iconPath);
        }
        catch
        {
            // Fall back to the default window icon if the asset is missing.
        }

        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var intro = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Copies the low end of one playback device to another, for a bass shaker. The source keeps playing the full mix. Turn on live view only when you want signal and pass vs reject energy."
        };
        _layout.Controls.Add(intro, 0, 0);
        _layout.SetColumnSpan(intro, 2);

        AddField(_layout, 1, "Source", _sourceCombo);
        AddField(_layout, 2, "Shaker", _shakerCombo);
        AddField(_layout, 3, "Cutoff", SliderRow(_cutoff, _cutoffValue));
        AddField(_layout, 4, "Level", SliderRow(_level, _levelValue));
        _layout.Controls.Add(_liveView, 0, 5);
        _layout.SetColumnSpan(_liveView, 2);
        _layout.Controls.Add(_signalLabel, 0, 6);
        _layout.Controls.Add(SignalRow(_meter, _bufferValue), 1, 6);
        _layout.Controls.Add(_bandView, 0, 7);
        _layout.SetColumnSpan(_bandView, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(_refreshButton);
        _layout.Controls.Add(buttons, 0, 8);
        _layout.SetColumnSpan(buttons, 2);
        _layout.Controls.Add(_status, 0, 9);
        _layout.SetColumnSpan(_status, 2);
        Controls.Add(_layout);

        _cutoff.ValueChanged += (_, _) =>
        {
            _cutoffValue.Text = $"{_cutoff.Value} Hz";
            if (!_loading)
                _engine.Update(_cutoff.Value, _level.Value / 100f);
        };
        _level.ValueChanged += (_, _) =>
        {
            _levelValue.Text = $"{_level.Value}%";
            if (!_loading)
                _engine.Update(_cutoff.Value, _level.Value / 100f);
        };
        _sourceCombo.SelectedIndexChanged += (_, _) => { if (!_loading) UpdateAvailability(); };
        _shakerCombo.SelectedIndexChanged += (_, _) => { if (!_loading) UpdateAvailability(); };
        _startButton.Click += (_, _) => ToggleRunning();
        _refreshButton.Click += (_, _) => LoadDevices();
        _liveView.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                ApplyVisualizationState();
        };
        _engine.Failed += OnFailed;
        _timer.Tick += (_, _) => RefreshMeter();
        _timer.Start();

        _cutoff.Value = _settings.CutoffHz;
        _level.Value = _settings.LevelPercent;
        _liveView.Checked = _settings.LiveView;
        LoadDevices();
        _loading = false;
        _uiReady = true;
        ApplyVisualizationState();
        UpdateAvailability();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _engine.Failed -= OnFailed;
        SaveSettings();
        _engine.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_uiReady)
            ApplyVisualizationState();
    }

    void ToggleRunning()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            _bandView.Clear();
            SaveSettings();
            UpdateAvailability();
            SetIdle("Stopped.");
            return;
        }

        if (_sourceCombo.SelectedItem is not DeviceChoice source || _shakerCombo.SelectedItem is not DeviceChoice shaker)
        {
            SetIdle("Choose a source and a shaker device.");
            return;
        }

        UseWaitCursor = true;
        try
        {
            _engine.Start(source.Id, shaker.Id, source.Name, shaker.Name, _cutoff.Value, _level.Value / 100f);
            ApplyVisualizationState();
            SaveSettings();
            SetRunning();
        }
        catch (Exception ex)
        {
            SetIdle(ex.Message);
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    void OnFailed(object? sender, string message)
    {
        if (IsDisposed)
            return;
        _engine.Stop();
        _bandView.Clear();
        UpdateAvailability();
        SetIdle(message);
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void ApplyVisualizationState()
    {
        if (!_uiReady || _layout.RowStyles.Count < 8)
            return;

        bool showUi = _liveView.Checked;
        bool active = showUi && WindowState != FormWindowState.Minimized && Visible;

        _signalLabel.Visible = showUi;
        _meter.Visible = showUi;
        _bufferValue.Visible = showUi;
        _bandView.Visible = showUi;
        _layout.RowStyles[6] = new RowStyle(SizeType.Absolute, showUi ? 36 : 0);
        _layout.RowStyles[7] = showUi
            ? new RowStyle(SizeType.Percent, 100)
            : new RowStyle(SizeType.Absolute, 0);

        Size min = showUi ? new Size(700, 640) : new Size(700, 420);
        if (MinimumSize != min)
            MinimumSize = min;
        if (showUi && ClientSize.Height < 640)
            ClientSize = new Size(Math.Max(ClientSize.Width, 740), 680);
        else if (!showUi && ClientSize.Height < 420)
            ClientSize = new Size(Math.Max(ClientSize.Width, 740), 480);

        _engine.VisualizationActive = active;
        _bandView.SetActive(active);
        if (!active)
        {
            _bandView.Clear();
            _meter.Value = 0;
            _bufferValue.Text = "Buffer —";
        }
    }

    void RefreshMeter()
    {
        if (!_engine.IsRunning)
        {
            _meter.Value = 0;
            _bufferValue.Text = "Buffer —";
            return;
        }

        if (_engine.ConsumeClipping())
            _clipUntil = DateTime.UtcNow.AddMilliseconds(600);

        if (_engine.VisualizationActive)
        {
            float peak = _engine.ConsumePeak();
            _meter.Value = (int)Math.Clamp(peak * 100f, 0, 100);
            _bufferValue.Text = $"Buffer {_engine.BufferedMilliseconds} ms";
            _bandView.UpdateHistory(_engine.History);
        }

        if (DateTime.UtcNow < _clipUntil)
        {
            _status.ForeColor = Color.DarkRed;
            _status.Text = "Clipping. Lower the level.";
            return;
        }

        _status.ForeColor = Color.DarkGreen;
        if (!_liveView.Checked)
            _status.Text = "Running. Live view off.";
        else if (WindowState == FormWindowState.Minimized)
            _status.Text = "Running. Live view paused while minimized.";
        else
            _status.Text = $"Running. {_engine.RouteDescription}";
    }

    void LoadDevices()
    {
        string? sourceId = (_sourceCombo.SelectedItem as DeviceChoice)?.Id ?? _settings.SourceId;
        string? shakerId = (_shakerCombo.SelectedItem as DeviceChoice)?.Id ?? _settings.ShakerId;
        string? defaultId = null;
        var snapshots = new List<(string Id, string Name)>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            try
            {
                try
                {
                    using MMDevice def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    defaultId = def.ID;
                }
                catch
                {
                    defaultId = null;
                }

                foreach (MMDevice device in devices)
                    snapshots.Add((device.ID, string.IsNullOrWhiteSpace(device.FriendlyName) ? "Playback device" : device.FriendlyName));
            }
            finally
            {
                foreach (MMDevice device in devices)
                    device.Dispose();
            }
        }
        catch (Exception ex)
        {
            SetIdle($"Couldn't list playback devices. {ex.Message}");
            return;
        }

        _loading = true;
        _sourceCombo.Items.Clear();
        _shakerCombo.Items.Clear();
        foreach ((string id, string name) in snapshots)
        {
            string label = name;
            if (snapshots.Count(item => item.Name == name) > 1)
            {
                string tail = id.Length > 8 ? id[^8..] : id;
                label = $"{name} ({tail})";
            }

            _sourceCombo.Items.Add(new DeviceChoice(id, label));
            _shakerCombo.Items.Add(new DeviceChoice(id, label));
        }

        Select(_sourceCombo, sourceId, snapshots.FindIndex(item => item.Id == defaultId));
        string? selectedSource = (_sourceCombo.SelectedItem as DeviceChoice)?.Id;
        int other = snapshots.FindIndex(item => item.Id != selectedSource);
        Select(_shakerCombo, shakerId == selectedSource ? null : shakerId, other);
        _loading = false;
        UpdateAvailability();
    }

    static void Select(ComboBox box, string? id, int fallback)
    {
        if (!string.IsNullOrEmpty(id))
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is DeviceChoice choice && choice.Id == id)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
        }

        if (box.Items.Count == 0)
            return;
        int index = fallback >= 0 ? fallback : 0;
        box.SelectedIndex = Math.Clamp(index, 0, box.Items.Count - 1);
    }

    void UpdateAvailability()
    {
        bool running = _engine.IsRunning;
        _sourceCombo.Enabled = !running;
        _shakerCombo.Enabled = !running;
        _refreshButton.Enabled = !running;
        _startButton.Text = running ? "Stop" : "Start";

        if (running)
            return;

        if (_sourceCombo.Items.Count == 0)
        {
            _startButton.Enabled = false;
            SetIdle("No playback devices found.");
            return;
        }

        string? sourceId = (_sourceCombo.SelectedItem as DeviceChoice)?.Id;
        string? shakerId = (_shakerCombo.SelectedItem as DeviceChoice)?.Id;
        if (sourceId != null && sourceId == shakerId)
        {
            _startButton.Enabled = false;
            SetIdle("Choose a different shaker output. One device for both would feed the shaker back into itself.");
            return;
        }

        if (_sourceCombo.Items.Count < 2)
        {
            _startButton.Enabled = false;
            SetIdle("Plug in a second playback device for the shaker.");
            return;
        }

        _startButton.Enabled = sourceId != null && shakerId != null;
        SetIdle("Ready.");
    }

    void SetRunning()
    {
        _startButton.Text = "Stop";
        _startButton.Enabled = true;
        _sourceCombo.Enabled = false;
        _shakerCombo.Enabled = false;
        _refreshButton.Enabled = false;
        _status.ForeColor = Color.DarkGreen;
        _status.Text = $"Running. {_engine.RouteDescription}";
    }

    void SetIdle(string message)
    {
        if (_engine.IsRunning)
            return;
        _sourceCombo.Enabled = true;
        _shakerCombo.Enabled = true;
        _refreshButton.Enabled = true;
        _startButton.Text = "Start";
        _status.ForeColor = SystemColors.ControlText;
        _status.Text = message;
        _meter.Value = 0;
    }

    void SaveSettings()
    {
        _settings.SourceId = (_sourceCombo.SelectedItem as DeviceChoice)?.Id;
        _settings.ShakerId = (_shakerCombo.SelectedItem as DeviceChoice)?.Id;
        _settings.CutoffHz = _cutoff.Value;
        _settings.LevelPercent = _level.Value;
        _settings.LiveView = _liveView.Checked;
        try
        {
            SettingsStore.Save(_settings);
        }
        catch
        {
            // Closing should still succeed when settings cannot be written.
        }
    }

    static void AddField(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    static Control SliderRow(TrackBar slider, Label value)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        slider.Margin = new Padding(0, 8, 0, 0);
        value.Margin = new Padding(0);
        row.Controls.Add(slider, 0, 0);
        row.Controls.Add(value, 1, 0);
        return row;
    }

    static Control SignalRow(ProgressBar meter, Label buffer)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        meter.Margin = new Padding(0, 6, 8, 6);
        buffer.Margin = new Padding(0);
        row.Controls.Add(meter, 0, 0);
        row.Controls.Add(buffer, 1, 0);
        return row;
    }

    sealed class DeviceChoice(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public override string ToString() => Name;
    }
}
