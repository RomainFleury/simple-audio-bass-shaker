namespace SimpleBassShakerRouter;

/// <summary>
/// Ring buffer of pass vs reject energy over a fixed window (default 5 seconds).
/// </summary>
sealed class EnergyHistory
{
    public const int WindowSeconds = 5;
    public const int SamplesPerSecond = 25;

    readonly float[] _pass;
    readonly float[] _reject;
    readonly object _lock = new();
    int _write;
    int _count;
    double _passEnergy;
    double _rejectEnergy;
    int _pendingFrames;
    int _framesPerSample;

    public EnergyHistory()
    {
        int length = WindowSeconds * SamplesPerSecond;
        _pass = new float[length];
        _reject = new float[length];
        _framesPerSample = 48000 / SamplesPerSecond;
    }

    public int Capacity => _pass.Length;

    public void SetSampleRate(int sampleRate)
    {
        lock (_lock)
        {
            _framesPerSample = Math.Max(1, sampleRate / SamplesPerSecond);
            ClearUnlocked();
        }
    }

    public void Clear()
    {
        lock (_lock)
            ClearUnlocked();
    }

    public void AddFrame(float passSample, float rejectSample)
    {
        lock (_lock)
        {
            _passEnergy += passSample * passSample;
            _rejectEnergy += rejectSample * rejectSample;
            _pendingFrames++;
            while (_pendingFrames >= _framesPerSample)
            {
                PushUnlocked(
                    (float)Math.Sqrt(_passEnergy / _framesPerSample),
                    (float)Math.Sqrt(_rejectEnergy / _framesPerSample));
                _passEnergy = 0;
                _rejectEnergy = 0;
                _pendingFrames -= _framesPerSample;
            }
        }
    }

    public void CopyTo(Span<float> pass, Span<float> reject, out int count)
    {
        lock (_lock)
        {
            count = Math.Min(_count, Math.Min(pass.Length, reject.Length));
            int start = (_write - count + _pass.Length) % _pass.Length;
            for (int i = 0; i < count; i++)
            {
                int index = (start + i) % _pass.Length;
                pass[i] = _pass[index];
                reject[i] = _reject[index];
            }
        }
    }

    void PushUnlocked(float pass, float reject)
    {
        _pass[_write] = pass;
        _reject[_write] = reject;
        _write = (_write + 1) % _pass.Length;
        if (_count < _pass.Length)
            _count++;
    }

    void ClearUnlocked()
    {
        Array.Clear(_pass);
        Array.Clear(_reject);
        _write = 0;
        _count = 0;
        _passEnergy = 0;
        _rejectEnergy = 0;
        _pendingFrames = 0;
    }
}
