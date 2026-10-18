using System.IO.Compression;
using System.Security.Cryptography;

namespace AirGlass.Core;

/// <summary>
/// Downloads the portable UxPlay engine bundle built by this repository's
/// "Build engine" GitHub Actions workflow, verifies its SHA-256 against the
/// checksum published next to it, and extracts it under the per-user engine
/// directory. Nothing is bundled with the app itself, which keeps the app
/// MIT-licensed and the GPL-licensed engine a clearly separate download.
/// </summary>
public sealed class EngineInstaller
{
    public const string ReleaseBase =
        "https://github.com/mjanthony819/airglass/releases/download/engine-latest";
    public const string ChecksumUrl = ReleaseBase + "/airglass-engine.sha256";
    public const string ZipUrl = ReleaseBase + "/airglass-engine-win-x64.zip";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };

    public async Task DownloadAndInstallAsync(
        IProgress<(int Percent, string Message)> progress, CancellationToken ct)
    {
        progress.Report((0, "Reading engine checksum..."));
        var checksumText = (await Http.GetStringAsync(ChecksumUrl, ct)).Trim();
        var expectedSha = checksumText.Split(' ', '*', '\t')[0];
        if (expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("The published engine checksum is malformed.");

        var tempZip = Path.Combine(Path.GetTempPath(), "airglass-engine-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            progress.Report((1, "Downloading engine (this is a one-time ~80 MB download)..."));
            using (var response = await Http.GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1;
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(tempZip);
                var buffer = new byte[1 << 16];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0)
                        progress.Report((1 + (int)(read * 94 / total), "Downloading engine..."));
                }
            }

            progress.Report((96, "Verifying download..."));
            var actualSha = Sha256OfFile(tempZip);
            if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Engine checksum mismatch. Expected {expectedSha}, got {actualSha}. The download was discarded.");

            progress.Report((97, "Installing engine..."));
            var engineDir = EngineManager.DefaultEngineDirectory;
            if (Directory.Exists(engineDir))
                Directory.Delete(engineDir, recursive: true);
            Directory.CreateDirectory(engineDir);
            ZipFile.ExtractToDirectory(tempZip, engineDir);
            if (!File.Exists(EngineManager.DefaultEngineExe))
                throw new InvalidDataException("The engine archive did not contain uxplay.exe.");
            progress.Report((100, "Engine installed."));
        }
        finally
        {
            try { File.Delete(tempZip); } catch { }
        }
    }

    public static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
