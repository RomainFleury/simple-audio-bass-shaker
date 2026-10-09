using System.Reflection;
using NAudio.Wave;

namespace SimpleBassShakerRouter;

enum PcmKind
{
    Float32,
    Int16,
    Int32
}

/// <summary>
/// Which source channels are mixed into the mono shaker feed.
/// Stereo uses every channel. Surround uses front left, front right, and LFE when the mask says so.
/// </summary>
readonly record struct SourceLayout(
    int SampleRate,
    int Channels,
    int BlockAlign,
    int BytesPerSample,
    PcmKind Kind,
    int[] MixChannels)
{
    public string Description =>
        $"{SampleRate} Hz, {Channels} ch, {Kind switch
        {
            PcmKind.Float32 => "32-bit float",
            PcmKind.Int16 => "16-bit",
            _ => "32-bit"
        }}";
}

static class SampleReader
{
    const int SpeakerFrontLeft = 0x1;
    const int SpeakerFrontRight = 0x2;
    const int SpeakerLowFrequency = 0x8;

    static readonly Guid IeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");
    static readonly Guid Pcm = new("00000001-0000-0010-8000-00AA00389B71");

    public static string Describe(WaveFormat format) =>
        $"{format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample}-bit {format.Encoding}";

    public static bool TryCreateLayout(WaveFormat format, out SourceLayout layout)
    {
        layout = default;
        if (format.Channels < 1 || format.SampleRate < 8000)
            return false;
        if (!TryGetKind(format, out PcmKind kind))
            return false;

        int bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0 || format.BlockAlign < format.Channels * bytesPerSample)
            return false;

        layout = new SourceLayout(
            format.SampleRate,
            format.Channels,
            format.BlockAlign,
            bytesPerSample,
            kind,
            SelectMixChannels(format));
        return true;
    }

    public static void Downmix(ReadOnlySpan<byte> source, int byteCount, SourceLayout layout, Span<float> mono)
    {
        int frames = byteCount / layout.BlockAlign;
        var channels = layout.MixChannels;
        float weight = channels.Length;
        for (int frame = 0; frame < frames; frame++)
        {
            int frameOffset = frame * layout.BlockAlign;
            float sum = 0;
            foreach (int channel in channels)
            {
                int offset = frameOffset + channel * layout.BytesPerSample;
                sum += Read(source, offset, layout.Kind);
            }

            mono[frame] = sum / weight;
        }
    }

    static int[] SelectMixChannels(WaveFormat format)
    {
        if (format is WaveFormatExtensible extensible)
        {
            int mask = ReadChannelMask(extensible);
            var selected = new List<int>();
            int channelIndex = 0;
            for (int bit = 0; bit < 32 && channelIndex < format.Channels; bit++)
            {
                int flag = 1 << bit;
                if ((mask & flag) == 0)
                    continue;
                if (flag is SpeakerFrontLeft or SpeakerFrontRight or SpeakerLowFrequency)
                    selected.Add(channelIndex);
                channelIndex++;
            }

            if (mask != 0 && selected.Count > 0)
                return selected.ToArray();
        }

        var all = new int[format.Channels];
        for (int i = 0; i < all.Length; i++)
            all[i] = i;
        return all;
    }

    static int ReadChannelMask(WaveFormatExtensible format)
    {
        FieldInfo? field = typeof(WaveFormatExtensible).GetField(
            "dwChannelMask",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(format) is int mask ? mask : 0;
    }

    static bool TryGetKind(WaveFormat format, out PcmKind kind)
    {
        WaveFormatEncoding encoding = format.Encoding;
        if (format is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == IeeeFloat)
                encoding = WaveFormatEncoding.IeeeFloat;
            else if (extensible.SubFormat == Pcm)
                encoding = WaveFormatEncoding.Pcm;
        }

        if (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            kind = PcmKind.Float32;
            return true;
        }

        if (encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            kind = PcmKind.Int16;
            return true;
        }

        if (encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32)
        {
            kind = PcmKind.Int32;
            return true;
        }

        kind = default;
        return false;
    }

    static float Read(ReadOnlySpan<byte> source, int offset, PcmKind kind)
    {
        switch (kind)
        {
            case PcmKind.Float32:
                return System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(source.Slice(offset, 4));
            case PcmKind.Int16:
                return System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(source.Slice(offset, 2)) / 32768f;
            default:
                return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4)) / 2147483648f;
        }
    }
}
