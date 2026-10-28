using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace AirGlass.Link;

/// <summary>
/// Supplies the HTTPS certificate the phone's browser must already trust (no root CA install is assumed).
/// Concrete providers are chosen by the user; none ships enabled by default.
/// </summary>
public interface ICertificateProvider
{
    /// <summary>Plain-language status shown in the UI.</summary>
    string Description { get; }

    /// <summary>Host name the phone should open (must match the certificate), or null if unknown.</summary>
    string? Host { get; }

    /// <summary>Address to listen on, or null for all interfaces. Providers that know a private interface should narrow this.</summary>
    IPAddress? BindAddress { get; }

    /// <summary>Returns a usable certificate with private key, or null with a reason.</summary>
    X509Certificate2? TryGetCertificate(out string? problem);
}

public sealed class NoCertificateProvider : ICertificateProvider
{
    public string Description => "No trusted HTTPS certificate is set up yet, so Link cannot start. Plain HTTP is not offered.";
    public string? Host => null;
    public IPAddress? BindAddress => null;
    public X509Certificate2? TryGetCertificate(out string? problem)
    {
        problem = Description;
        return null;
    }
}
