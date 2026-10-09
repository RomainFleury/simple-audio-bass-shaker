namespace SimpleBassShakerRouter;

/// <summary>
/// Mono signal path for a bass shaker: a fixed 18 Hz high-pass, then a 24 dB/oct low-pass.
/// The low-pass is two Butterworth stages (Linkwitz-Riley), about -6 dB at the cutoff.
/// </summary>
sealed class ShakerFilter
{
    const float InfrasonicHz = 18f;
    const float ButterworthQ = 0.70710678118f;

    private readonly Biquad _highPass = new();
    private readonly Biquad _lowPassA = new();
    private readonly Biquad _lowPassB = new();
    private int _sampleRate;
    private float _cutoffHz = float.NaN;

    public void Configure(int sampleRate, float cutoffHz)
    {
        if (sampleRate < 8000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (sampleRate != _sampleRate)
        {
            _sampleRate = sampleRate;
            _highPass.SetHighPass(sampleRate, InfrasonicHz, ButterworthQ);
            _cutoffHz = float.NaN;
        }

        float limited = Math.Clamp(cutoffHz, 20f, sampleRate * 0.45f);
        if (!float.IsNaN(_cutoffHz) && Math.Abs(limited - _cutoffHz) < 0.05f)
            return;

        _cutoffHz = limited;
        _lowPassA.SetLowPass(sampleRate, limited, ButterworthQ);
        _lowPassB.SetLowPass(sampleRate, limited, ButterworthQ);
    }

    public void Reset()
    {
        _highPass.Reset();
        _lowPassA.Reset();
        _lowPassB.Reset();
        _sampleRate = 0;
        _cutoffHz = float.NaN;
    }

    public float Process(float sample) =>
        _lowPassB.Process(_lowPassA.Process(_highPass.Process(sample)));

    sealed class Biquad
    {
        private double _b0 = 1;
        private double _b1;
        private double _b2;
        private double _a1;
        private double _a2;
        private double _x1;
        private double _x2;
        private double _y1;
        private double _y2;

        public void SetLowPass(int sampleRate, float frequency, float q) =>
            Set(sampleRate, frequency, q, lowPass: true);

        public void SetHighPass(int sampleRate, float frequency, float q) =>
            Set(sampleRate, frequency, q, lowPass: false);

        public void Reset()
        {
            _x1 = _x2 = _y1 = _y2 = 0;
        }

        public float Process(float sample)
        {
            double x = sample;
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = Math.Abs(y) < 1e-20 ? 0 : y;
            return (float)_y1;
        }

        private void Set(int sampleRate, float frequency, float q, bool lowPass)
        {
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cos = Math.Cos(w0);
            double sin = Math.Sin(w0);
            double alpha = sin / (2 * q);
            double b0, b1, b2;
            if (lowPass)
            {
                b0 = (1 - cos) / 2;
                b1 = 1 - cos;
                b2 = (1 - cos) / 2;
            }
            else
            {
                b0 = (1 + cos) / 2;
                b1 = -(1 + cos);
                b2 = (1 + cos) / 2;
            }

            double a0 = 1 + alpha;
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = (-2 * cos) / a0;
            _a2 = (1 - alpha) / a0;
        }
    }
}
