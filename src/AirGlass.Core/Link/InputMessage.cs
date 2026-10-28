using System.Text.Json;

namespace AirGlass.Core.Link;

public enum InputKind { Move, Click, Scroll, Text, Key }

public sealed record InputMessage(InputKind Kind, int A = 0, int B = 0, string? Text = null);

/// <summary>Strict parser for phone input. Anything unexpected is rejected, never guessed.</summary>
public static class InputParser
{
    public const int MaxMove = 400;
    public const int MaxScroll = 50;
    public const int MaxText = 500;
    public const int MaxMessageBytes = 2048;

    private static readonly HashSet<string> Keys = new()
    {
        "enter","backspace","tab","escape","left","right","up","down","delete","home","end",
    };

    public static bool TryParse(string json, out InputMessage? msg)
    {
        msg = null;
        if (json is null || json.Length > MaxMessageBytes) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String)
                return false;
            switch (t.GetString())
            {
                case "move":
                    if (!Int(r, "dx", out var dx) || !Int(r, "dy", out var dy)) return false;
                    msg = new InputMessage(InputKind.Move, Math.Clamp(dx, -MaxMove, MaxMove), Math.Clamp(dy, -MaxMove, MaxMove));
                    return true;
                case "click":
                    if (!r.TryGetProperty("b", out var b) || b.ValueKind != JsonValueKind.String) return false;
                    var bs = b.GetString();
                    var btn = bs == "left" ? 0 : bs == "right" ? 1 : bs == "double" ? 2 : -1;
                    if (btn < 0) return false;
                    msg = new InputMessage(InputKind.Click, btn);
                    return true;
                case "scroll":
                    if (!Int(r, "dy", out var sy)) return false;
                    msg = new InputMessage(InputKind.Scroll, Math.Clamp(sy, -MaxScroll, MaxScroll));
                    return true;
                case "text":
                    if (!r.TryGetProperty("s", out var s) || s.ValueKind != JsonValueKind.String) return false;
                    var str = s.GetString() ?? "";
                    if (str.Length == 0 || str.Length > MaxText || str.Any(c => char.IsControl(c) && c != '\n')) return false;
                    msg = new InputMessage(InputKind.Text, Text: str);
                    return true;
                case "key":
                    if (!r.TryGetProperty("k", out var k) || k.ValueKind != JsonValueKind.String) return false;
                    var key = k.GetString() ?? "";
                    if (!Keys.Contains(key)) return false;
                    msg = new InputMessage(InputKind.Key, Text: key);
                    return true;
                default: return false;
            }
        }
        catch (JsonException) { return false; }
    }

    private static bool Int(JsonElement r, string name, out int v)
    {
        v = 0;
        return r.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out v);
    }
}
