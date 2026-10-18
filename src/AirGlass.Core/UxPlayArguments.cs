namespace AirGlass.Core;

/// <summary>Builds the uxplay.exe command line from user settings.</summary>
public static class UxPlayArguments
{
    public static readonly string[] Resolutions = { "1280x720", "1920x1080", "2560x1440" };

    public static IReadOnlyList<string> Build(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.DeviceName))
            throw new ArgumentException("Device name cannot be empty.");
        if (!Resolutions.Contains(s.Resolution))
            throw new ArgumentException($"Unsupported resolution '{s.Resolution}'.");
        if (s.UsePin && s.Pin.Length > 0 && (s.Pin.Length != 4 || !s.Pin.All(char.IsDigit)))
            throw new ArgumentException("PIN must be exactly 4 digits, or empty for a random PIN.");
        if (s.BasePort is < 1024 or > 65532)
            throw new ArgumentException("Base port must be between 1024 and 65532.");

        var args = new List<string>
        {
            "-n", s.DeviceName.Trim(),
            "-nh",
            "-s", s.Resolution + "@60",
            "-p", s.BasePort.ToString(),
        };
        if (s.LowLatency)
        {
            args.Add("-vsync");
            args.Add("no");
        }
        if (s.UsePin)
            args.Add(s.Pin.Length == 4 ? "-pin" + s.Pin : "-pin");
        return args;
    }

    public static string ToCommandLine(IReadOnlyList<string> args) =>
        string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
