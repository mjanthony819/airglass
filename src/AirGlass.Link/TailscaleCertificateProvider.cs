using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace AirGlass.Link;

public interface ICommandRunner
{
    /// <summary>Runs a program and returns exit code plus combined output.</summary>
    (int ExitCode, string Output) Run(string file, params string[] args);
    string? FindTailscale();
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public string? FindTailscale()
    {
        foreach (var c in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale.exe"),
        })
            if (File.Exists(c)) return c;
        return null;
    }

    public (int, string) Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000)) { try { p.Kill(true); } catch { } return (-1, "timed out"); }
        return (p.ExitCode, o.Result + e.Result);
    }
}

public sealed record TailscaleState(bool Running, string? CertDomain, IPAddress? Ip4, string? Problem);

/// <summary>
/// Uses the user's own Tailscale install: a publicly trusted certificate for the device's tailnet name
/// (issued by Tailscale's `cert` command through Let's Encrypt, so Safari trusts it with no profile),
/// and the server listens only on the Tailscale address, not on the local Wi-Fi.
/// Requires the user to install Tailscale on both devices, sign in, and enable HTTPS certificates.
/// </summary>
public sealed class TailscaleCertificateProvider : ICertificateProvider
{
    private readonly ICommandRunner _run;
    private readonly string _dir;
    private TailscaleState? _state;

    public TailscaleCertificateProvider(ICommandRunner? runner = null, string? certDir = null)
    {
        _run = runner ?? new ProcessCommandRunner();
        _dir = certDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirGlass", "link-cert");
    }

    public string Description => Refresh().Problem ??
        $"Using your Tailscale name {_state!.CertDomain}. Link listens only on your Tailscale address.";
    public string? Host => Refresh().CertDomain;
    public IPAddress? BindAddress => Refresh().Ip4;

    private TailscaleState Refresh() => _state ??= Inspect();

    public void Invalidate() => _state = null;

    private TailscaleState Inspect()
    {
        var exe = _run.FindTailscale();
        if (exe is null)
            return new(false, null, null, "Tailscale is not installed on this PC. Install it from tailscale.com, sign in, then try again.");
        var (code, output) = _run.Run(exe, "status", "--json");
        if (code != 0) return new(false, null, null, "Tailscale did not answer. Open the Tailscale app and sign in.");
        return ParseStatus(output);
    }

    public static TailscaleState ParseStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var backend = r.TryGetProperty("BackendState", out var b) ? b.GetString() : null;
            if (backend != "Running")
                return new(false, null, null, "Tailscale is not connected. Open the Tailscale app and sign in.");
            string? domain = null;
            if (r.TryGetProperty("CertDomains", out var cd) && cd.ValueKind == JsonValueKind.Array && cd.GetArrayLength() > 0)
                domain = cd[0].GetString();
            IPAddress? ip4 = null;
            if (r.TryGetProperty("Self", out var self) && self.TryGetProperty("TailscaleIPs", out var ips) && ips.ValueKind == JsonValueKind.Array)
                foreach (var e in ips.EnumerateArray())
                    if (IPAddress.TryParse(e.GetString(), out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip4 = ip; break; }
            if (string.IsNullOrWhiteSpace(domain))
                return new(true, null, ip4, "HTTPS certificates are not enabled for your tailnet. In the Tailscale admin console, DNS page, turn on HTTPS Certificates.");
            domain = domain.TrimEnd('.');
            if (!IsSafeHostName(domain))
                return new(true, null, ip4, "Tailscale returned an unexpected name.");
            if (ip4 is null)
                return new(true, domain, null, "Tailscale has no IPv4 address for this PC yet.");
            return new(true, domain, ip4, null);
        }
        catch (JsonException)
        {
            return new(false, null, null, "Could not read Tailscale's status.");
        }
    }

    /// <summary>Only letters, digits, dots and hyphens: this value is passed to a command.</summary>
    public static bool IsSafeHostName(string s) =>
        s.Length is > 3 and <= 253 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-') && !s.StartsWith('-') && s.Contains('.');

    public X509Certificate2? TryGetCertificate(out string? problem)
    {
        Invalidate();
        var st = Refresh();
        if (st.Problem is not null) { problem = st.Problem; return null; }
        var exe = _run.FindTailscale()!;
        Directory.CreateDirectory(_dir);
        var certFile = Path.Combine(_dir, "link.crt");
        var keyFile = Path.Combine(_dir, "link.key");
        var (code, output) = _run.Run(exe, "cert", "--cert-file", certFile, "--key-file", keyFile, st.CertDomain!);
        if (code != 0)
        {
            problem = "Tailscale could not get a certificate: " + output.Trim();
            return null;
        }
        try
        {
            using var pem = X509Certificate2.CreateFromPemFile(certFile, keyFile);
            problem = null;
            // Re-import so the private key is usable by the Windows TLS stack.
            return new X509Certificate2(pem.Export(X509ContentType.Pfx));
        }
        catch (Exception ex)
        {
            problem = "Could not read the certificate: " + ex.Message;
            return null;
        }
    }
}
