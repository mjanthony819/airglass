using System.Diagnostics;

namespace AirGlass.Core;

/// <summary>Owns the uxplay.exe child process: start, stop, and streamed output.</summary>
public sealed class EngineManager : IDisposable
{
    private Process? _process;

    public event Action<string>? LogLine;
    public event Action? Exited;

    public static string DefaultEngineDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirGlass", "engine");

    public static string DefaultEngineExe => Path.Combine(DefaultEngineDirectory, "uxplay.exe");

    public bool EngineInstalled => File.Exists(DefaultEngineExe);

    public bool IsRunning => _process is { HasExited: false };

    public void Start(AppSettings settings)
    {
        Start(settings, DefaultEngineExe);
    }

    public void Start(AppSettings settings, string exePath)
    {
        Stop();
        if (!File.Exists(exePath))
            throw new FileNotFoundException("The mirroring engine is not installed yet.", exePath);

        var engineDir = Path.GetDirectoryName(exePath)!;
        var pluginDir = Path.Combine(engineDir, "lib", "gstreamer-1.0");
        var psi = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = engineDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in UxPlayArguments.Build(settings))
            psi.ArgumentList.Add(arg);
        if (Directory.Exists(pluginDir))
        {
            // Point GStreamer at the bundled plugins so no system GStreamer is needed.
            psi.Environment["GST_PLUGIN_PATH"] = pluginDir;
            psi.Environment["GST_PLUGIN_SYSTEM_PATH"] = pluginDir;
        }
        // Keep the engine's DLLs ahead of anything else on PATH.
        psi.Environment["PATH"] = engineDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the engine process.");
        _process.EnableRaisingEvents = true;
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) LogLine?.Invoke(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) LogLine?.Invoke(e.Data); };
        _process.Exited += (_, _) => Exited?.Invoke();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Stop()
    {
        try
        {
            if (IsRunning)
                _process!.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort: the process may already be gone.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Stop();
}
