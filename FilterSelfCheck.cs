using NAudio.Wave;

namespace SimpleBassShakerRouter;

static class FilterSelfCheck
{
    public static int Run(TextWriter output)
    {
        var failures = new List<string>();
        Check(output, failures, "10 Hz", Ratio(10f), 0.05f, 0.55f);
        Check(output, failures, "40 Hz", Ratio(40f), 0.75f, 1.05f);
        Check(output, failures, "80 Hz", Ratio(80f), 0.35f, 0.70f);
        Check(output, failures, "1000 Hz", Ratio(1000f), 0f, 0.02f);
        CheckMix(output, failures);

        if (failures.Count == 0)
        {
            output.WriteLine("PASS");
            return 0;
        }

        output.WriteLine("FAIL");
        foreach (string failure in failures)
            output.WriteLine(failure);
        return 1;
    }

    static void CheckMix(TextWriter output, List<string> failures)
    {
        var surround = new WaveFormatExtensible(48000, 32, 6);
        if (!SampleReader.TryCreateLayout(surround, out SourceLayout layout))
        {
            failures.Add("Could not read a 6-channel mix format.");
            output.WriteLine("surround mix failed");
            return;
        }

        string indexes = string.Join(",", layout.MixChannels);
        output.WriteLine($"surround mix {indexes}");
        if (indexes != "0,1,3")
            failures.Add($"Surround mix channels were {indexes}, expected 0,1,3.");

        var stereo = new WaveFormat(48000, 16, 2);
        if (!SampleReader.TryCreateLayout(stereo, out SourceLayout stereoLayout) || stereoLayout.MixChannels.Length != 2)
        {
            failures.Add("Stereo mix did not keep both channels.");
            output.WriteLine("stereo mix failed");
        }
        else
        {
            output.WriteLine("stereo mix 0,1");
        }
    }

    static void Check(TextWriter output, List<string> failures, string name, float ratio, float min, float max)
    {
        output.WriteLine($"{name} ratio {ratio:0.0000}");
        if (float.IsNaN(ratio) || ratio < min || ratio > max)
            failures.Add($"{name} ratio {ratio:0.0000} was outside {min:0.00}-{max:0.00}");
    }

    static float Ratio(float frequency)
    {
        const int sampleRate = 48000;
        const int length = sampleRate * 2;
        var filter = new ShakerFilter();
        filter.Configure(sampleRate, 80f);
        double input = 0;
        double output = 0;
        int start = sampleRate;
        for (int i = 0; i < length; i++)
        {
            float x = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
            float y = filter.Process(x);
            if (float.IsNaN(y) || float.IsInfinity(y))
                return float.NaN;
            if (i < start)
                continue;
            input += x * x;
            output += y * y;
        }

        return (float)Math.Sqrt(output / input);
    }
}
