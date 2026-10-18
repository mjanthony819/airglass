using System.Text.RegularExpressions;

namespace AirGlass.Core;

public enum MirrorStatus { Stopped, Starting, Waiting, Connected }

/// <summary>
/// Best-effort state machine fed with uxplay stdout/stderr lines.
/// UxPlay logs (INFO level): "Initialized server socket(s)" when listening,
/// "Accepted Mirror data client ..." when a client connects, "Remote: &lt;ip&gt;"
/// for the client address, "registered new client: &lt;name&gt;" for the device
/// name, and "Removing connection"/"Connection closed" on teardown.
/// </summary>
public sealed partial class StatusParser
{
    private int _openConnections;

    public MirrorStatus Status { get; private set; } = MirrorStatus.Starting;
    public string? ClientAddress { get; private set; }
    public string? ClientName { get; private set; }

    public void Reset()
    {
        Status = MirrorStatus.Starting;
        ClientAddress = null;
        ClientName = null;
        _openConnections = 0;
    }

    /// <summary>Returns true when the visible state changed.</summary>
    public bool Feed(string line)
    {
        var before = (Status, ClientAddress, ClientName);

        if (line.Contains("Initialized server socket", StringComparison.OrdinalIgnoreCase))
        {
            Status = MirrorStatus.Waiting;
        }
        else if (line.Contains("Accepted ", StringComparison.OrdinalIgnoreCase) &&
                 line.Contains("client", StringComparison.OrdinalIgnoreCase))
        {
            _openConnections++;
            Status = MirrorStatus.Connected;
        }
        else if (line.StartsWith("Remote:", StringComparison.OrdinalIgnoreCase))
        {
            ClientAddress = line["Remote:".Length..].Trim();
            if (_openConnections > 0) Status = MirrorStatus.Connected;
        }
        else if (line.Contains("registered new client:", StringComparison.OrdinalIgnoreCase))
        {
            var m = ClientNameRegex().Match(line);
            if (m.Success) ClientName = m.Groups[1].Value.Trim();
        }
        else if (line.Contains("Removing connection", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Connection closed", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Destroying connection", StringComparison.OrdinalIgnoreCase))
        {
            _openConnections = Math.Max(0, _openConnections - 1);
            if (_openConnections == 0 && Status == MirrorStatus.Connected)
            {
                Status = MirrorStatus.Waiting;
                ClientAddress = null;
                ClientName = null;
            }
        }

        return before != (Status, ClientAddress, ClientName);
    }

    [GeneratedRegex(@"registered new client:\s*(.+?)\s+DeviceID", RegexOptions.IgnoreCase)]
    private static partial Regex ClientNameRegex();
}
