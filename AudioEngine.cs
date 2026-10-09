using System.Buffers;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SimpleBassShakerRouter;

sealed class AudioEngine : IDisposable
{
    readonly object _gate = new();
    readonly object _meterLock = new();
    readonly ShakerFilter _filter = new();
    readonly EnergyHistory _history = new();

    SynchronizationContext? _ui;
    MMDeviceEnumerator? _enumerator;
    WasapiLoopbackCapture? _capture;
    WasapiOut? _output;
    BufferedWaveProvider? _buffer;
    SourceLayout _layout;
    int _accepting;
    int _failurePosted;
    int _visualizationActive;
    bool _running;
    float _peak;
    int _clipped;
    int _fadeRemaining;
    int _fadeLength;
    volatile float _cutoffHz = 80f;
    volatile float _level = 1f;

    public event EventHandler<string>? Failed;

    public EnergyHistory History => _history;

    /// <summary>
    /// When false, skip pass/reject history so minimized or game-time use stays cheap.
    /// </summary>
    public bool VisualizationActive
    {
        get => Volatile.Read(ref _visualizationActive) == 1;
        set
        {
            if (value)
            {
                Volatile.Write(ref _visualizationActive, 1);
            }
            else
            {
                Volatile.Write(ref _visualizationActive, 0);
                _history.Clear();
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _running;
        }
    }

    public string RouteDescription { get; private set; } = "";

    public int BufferedMilliseconds
    {
        get
        {
            lock (_gate)
                return _buffer == null ? 0 : (int)_buffer.BufferedDuration.TotalMilliseconds;
        }
    }

    public void Update(float cutoffHz, float level)
    {
        _cutoffHz = cutoffHz;
        _level = Math.Clamp(level, 0f, 2f);
    }

    public float ConsumePeak()
    {
        lock (_meterLock)
        {
            float value = _peak;
            _peak *= 0.55f;
            if (_peak < 0.001f)
                _peak = 0;
            return value;
        }
    }

    public bool ConsumeClipping() => Interlocked.Exchange(ref _clipped, 0) == 1;

    public void Start(string sourceId, string shakerId, string sourceName, string shakerName, float cutoffHz, float level)
    {
        if (string.Equals(sourceId, shakerId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pick two different devices. The same device would feed the shaker back into itself.");

        Update(cutoffHz, level);
        _ui = SynchronizationContext.Current;
        Interlocked.Exchange(ref _failurePosted, 0);

        MMDeviceEnumerator enumerator = new();
        MMDevice? source = null;
        MMDevice? shaker = null;
        WasapiLoopbackCapture? capture = null;
        WasapiOut? output = null;
        try
        {
            source = enumerator.GetDevice(sourceId);
            shaker = enumerator.GetDevice(shakerId);
            capture = new WasapiLoopbackCapture(source);
            if (!SampleReader.TryCreateLayout(capture.WaveFormat, out SourceLayout layout))
            {
                throw new NotSupportedException(
                    $"The source format isn't supported yet ({SampleReader.Describe(capture.WaveFormat)}).");
            }

            var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(layout.SampleRate, 2))
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            output = new WasapiOut(shaker, AudioClientShareMode.Shared, true, 50);
            output.Init(buffer);
            output.PlaybackStopped += OnPlaybackStopped;
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnRecordingStopped;

            _filter.Reset();
            _history.SetSampleRate(layout.SampleRate);
            lock (_gate)
            {
                _enumerator = enumerator;
                _capture = capture;
                _output = output;
                _buffer = buffer;
                _layout = layout;
                _fadeLength = Math.Max(1, layout.SampleRate / 50);
                _fadeRemaining = _fadeLength;
                RouteDescription = $"{sourceName} → {shakerName}. {layout.Description}.";
                _running = true;
            }

            Volatile.Write(ref _accepting, 1);
            capture.StartRecording();
            output.Play();
            enumerator = null!;
            capture = null;
            output = null;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _accepting, 0);
            try { capture?.StopRecording(); } catch { /* not started */ }
            try { output?.Stop(); } catch { /* not started */ }
            try { capture?.Dispose(); } catch { /* release anyway */ }
            try { output?.Dispose(); } catch { /* release anyway */ }
            try { enumerator?.Dispose(); } catch { /* release anyway */ }
            lock (_gate)
            {
                _running = false;
                _capture = null;
                _output = null;
                _buffer = null;
                _enumerator = null;
            }

            throw new InvalidOperationException(AudioErrors.Humanize(ex), ex);
        }
    }

    public void Stop()
    {
        WasapiLoopbackCapture? capture;
        WasapiOut? output;
        MMDeviceEnumerator? enumerator;
        lock (_gate)
        {
            Volatile.Write(ref _accepting, 0);
            capture = _capture;
            output = _output;
            enumerator = _enumerator;
            _capture = null;
            _output = null;
            _buffer = null;
            _enumerator = null;
            _running = false;
        }

        _history.Clear();
        try { capture?.StopRecording(); } catch { /* already stopped */ }
        try { output?.Stop(); } catch { /* already stopped */ }
        try { capture?.Dispose(); } catch { /* release anyway */ }
        try { output?.Dispose(); } catch { /* release anyway */ }
        try { enumerator?.Dispose(); } catch { /* release anyway */ }
    }

    public void Dispose() => Stop();

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (Volatile.Read(ref _accepting) == 0 || e.BytesRecorded <= 0)
            return;

        BufferedWaveProvider? buffer;
        SourceLayout layout;
        lock (_gate)
        {
            buffer = _buffer;
            layout = _layout;
        }

        if (buffer == null || layout.MixChannels == null || layout.MixChannels.Length == 0)
            return;

        try
        {
            int frames = e.BytesRecorded / layout.BlockAlign;
            if (frames <= 0)
                return;

            float[] mono = ArrayPool<float>.Shared.Rent(frames);
            byte[] interleaved = ArrayPool<byte>.Shared.Rent(frames * 8);
            try
            {
                SampleReader.Downmix(e.Buffer, frames * layout.BlockAlign, layout, mono);
                _filter.Configure(layout.SampleRate, _cutoffHz);
                float gain = _level;
                bool clipped = false;
                float peak = 0;
                bool visualize = Volatile.Read(ref _visualizationActive) == 1;
                Span<float> stereo = MemoryMarshal.Cast<byte, float>(interleaved.AsSpan(0, frames * 8));
                for (int i = 0; i < frames; i++)
                {
                    float filtered = _filter.Process(mono[i]);
                    if (visualize)
                        _history.AddFrame(filtered, mono[i] - filtered);

                    float sample = filtered * gain;
                    if (_fadeRemaining > 0)
                    {
                        sample *= 1f - (_fadeRemaining / (float)_fadeLength);
                        _fadeRemaining--;
                    }

                    float abs = Math.Abs(sample);
                    if (abs > peak)
                        peak = abs;
                    if (sample > 1f)
                    {
                        sample = 1f;
                        clipped = true;
                    }
                    else if (sample < -1f)
                    {
                        sample = -1f;
                        clipped = true;
                    }

                    stereo[i * 2] = sample;
                    stereo[i * 2 + 1] = sample;
                }

                buffer.AddSamples(interleaved, 0, frames * 8);
                LimitLatency(buffer);
                if (visualize && peak > 0)
                {
                    lock (_meterLock)
                    {
                        if (peak > _peak)
                            _peak = peak;
                    }
                }

                if (clipped)
                    Interlocked.Exchange(ref _clipped, 1);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(mono);
                ArrayPool<byte>.Shared.Return(interleaved);
            }
        }
        catch (Exception ex)
        {
            NotifyFailed(AudioErrors.Humanize(ex));
        }
    }

    static void LimitLatency(BufferedWaveProvider buffer)
    {
        if (buffer.BufferedDuration.TotalMilliseconds <= 180)
            return;

        int align = buffer.WaveFormat.BlockAlign;
        int targetBytes = buffer.WaveFormat.AverageBytesPerSecond * 90 / 1000;
        targetBytes -= targetBytes % align;
        int excess = buffer.BufferedBytes - targetBytes;
        excess -= excess % align;
        if (excess <= 0)
            return;

        byte[] trash = ArrayPool<byte>.Shared.Rent(excess);
        try
        {
            buffer.Read(trash, 0, excess);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(trash);
        }
    }

    void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null && Volatile.Read(ref _accepting) == 1)
            NotifyFailed(AudioErrors.Humanize(e.Exception));
    }

    void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null && Volatile.Read(ref _accepting) == 1)
            NotifyFailed(AudioErrors.Humanize(e.Exception));
    }

    void NotifyFailed(string message)
    {
        if (Interlocked.Exchange(ref _failurePosted, 1) == 1)
            return;

        void Raise() => Failed?.Invoke(this, message);
        if (_ui != null)
            _ui.Post(_ => Raise(), null);
        else
            Raise();
    }
}

static class AudioErrors
{
    public static string Humanize(Exception ex)
    {
        if (ex is InvalidOperationException invalid && invalid.InnerException != null && IsAudioClientError(invalid.InnerException))
            return invalid.Message;

        uint code = ex is COMException com ? unchecked((uint)com.HResult) : unchecked((uint)ex.HResult);
        string? friendly = code switch
        {
            0x88890008 => "The device rejected the audio format. In Windows sound settings, set it to 16- or 24-bit, 44100 or 48000 Hz, then try again.",
            0x8889000A => "That device is already in exclusive use.",
            0x88890012 => "Exclusive mode is blocking shared access to that device.",
            _ => null
        };

        if (friendly == null)
            return string.IsNullOrWhiteSpace(ex.Message) ? "Audio stopped." : ex.Message;

        return string.IsNullOrWhiteSpace(ex.Message) ? friendly : $"{friendly} ({ex.Message})";
    }

    static bool IsAudioClientError(Exception ex)
    {
        uint code = unchecked((uint)ex.HResult);
        return code is 0x88890008 or 0x8889000A or 0x88890012;
    }
}
