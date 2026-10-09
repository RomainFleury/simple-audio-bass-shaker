using System.Runtime.InteropServices;

namespace SimpleBassShakerRouter;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-check") >= 0)
            Environment.Exit(RunReport(args, "--self-check", FilterSelfCheck.Run));

        if (Array.IndexOf(args, "--list-devices") >= 0)
            Environment.Exit(RunReport(args, "--list-devices", DevicePrinter.Run));

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    static int RunReport(string[] args, string flag, Func<TextWriter, int> run)
    {
        int index = Array.IndexOf(args, flag);
        string? path = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith('-')
            ? args[index + 1]
            : null;
        if (path == null)
        {
            ConsoleHost.Attach();
            return run(Console.Out);
        }

        using var file = new StreamWriter(path) { AutoFlush = true };
        return run(file);
    }
}

static class ConsoleHost
{
    const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(int dwProcessId);

    public static void Attach()
    {
        AttachConsole(AttachParentProcess);
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch
        {
            // The exit code still reports the result when no console is attached.
        }
    }
}
