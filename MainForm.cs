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
    readonly Button _startButton = new() { Text = "Start", AutoSize = true, Padding = new Padding(16, 6, 16, 6) };
    readonly Button _refreshButton = new() { Text = "Refresh devices", AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
    readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    bool _loading = true;
    DateTime _clipUntil = DateTime.MinValue;

    public MainForm()
    {
        Text = "Simple Bass Shaker Router";
        Font = new Font("Segoe UI", 10f);
        MinimumSize = new Size(680, 520);
        ClientSize = new Size(700, 520);
        StartPosition = FormStartPosition.CenterScreen;
        AcceptButton = _startButton;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(16)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var intro = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Copies the low end of one playback device to another, for a bass shaker. The source keeps playing the full mix. Stereo uses both channels. Surround uses the front pair plus the low-frequency channel. A fixed high-pass at 18 Hz blocks the slowest rumbles."
        };
        table.Controls.Add(intro, 0, 0);
        table.SetColumnSpan(intro, 2);

        AddField(table, 1, "Source", _sourceCombo);
        AddField(table, 2, "Shaker", _shakerCombo);
        AddField(table, 3, "Cutoff", SliderRow(_cutoff, _cutoffValue));
        AddField(table, 4, "Level", SliderRow(_level, _levelValue));
        AddField(table, 5, "Signal", _meter);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(_refreshButton);
        table.Controls.Add(buttons, 0, 6);
        table.SetColumnSpan(buttons, 2);
        table.Controls.Add(_status, 0, 7);
        table.SetColumnSpan(_status, 2);
        Controls.Add(table);

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
        _engine.Failed += OnFailed;
        _timer.Tick += (_, _) => RefreshMeter();
        _timer.Start();

        _cutoff.Value = _settings.CutoffHz;
        _level.Value = _settings.LevelPercent;
        LoadDevices();
        _loading = false;
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

    void ToggleRunning()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            SaveSettings();
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
        SetIdle(message);
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void RefreshMeter()
    {
        if (!_engine.IsRunning)
        {
            _meter.Value = 0;
            return;
        }

        float peak = _engine.ConsumePeak();
        _meter.Value = (int)Math.Clamp(peak * 100f, 0, 100);
        if (_engine.ConsumeClipping())
            _clipUntil = DateTime.UtcNow.AddMilliseconds(600);

        if (DateTime.UtcNow < _clipUntil)
        {
            _status.ForeColor = Color.DarkRed;
            _status.Text = $"Clipping. Lower the level. {_engine.RouteDescription} Buffer {_engine.BufferedMilliseconds} ms.";
            return;
        }

        _status.ForeColor = Color.DarkGreen;
        _status.Text = $"Running. {_engine.RouteDescription} Buffer {_engine.BufferedMilliseconds} ms.";
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

    sealed class DeviceChoice(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public override string ToString() => Name;
    }
}
