using NAudio.CoreAudioApi;

namespace SimpleBassShakerRouter;

static class DevicePrinter
{
    public static int Run(TextWriter output)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            output.WriteLine($"Render devices: {devices.Count}");
            foreach (var device in devices)
            {
                try
                {
                    string format = device.AudioClient.MixFormat.ToString() ?? "";
                    output.WriteLine(device.FriendlyName);
                    output.WriteLine($"  {format}");
                }
                finally
                {
                    device.Dispose();
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine(ex.Message);
            return 1;
        }
    }
}
