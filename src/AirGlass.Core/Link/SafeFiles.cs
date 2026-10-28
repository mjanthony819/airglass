namespace AirGlass.Core.Link;

public static class SafeFiles
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
    };
    private static readonly char[] WindowsInvalid = "<>:\"/\\|?*".ToCharArray();
    private static readonly HashSet<string> RiskyExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",".bat",".cmd",".com",".scr",".msi",".ps1",".vbs",".js",".jse",".wsf",".lnk",".hta",".dll",".reg",".jar",
    };

    /// <summary>Reduce an untrusted file name to a safe single path component.</summary>
    public static string CleanName(string? raw)
    {
        var name = raw ?? "";
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var invalid = WindowsInvalid;
        name = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) name = "file";
        var stem = Path.GetFileNameWithoutExtension(name);
        if (Reserved.Contains(stem)) name = "_" + name;
        if (name.Length > 120)
        {
            var ext = Path.GetExtension(name);
            if (ext.Length > 20) ext = "";
            name = name[..(120 - ext.Length)] + ext;
        }
        return name;
    }

    /// <summary>Executable types are saved with a ".blocked" suffix so a tap cannot run them.</summary>
    public static string NeutraliseRisky(string name) =>
        RiskyExt.Contains(Path.GetExtension(name)) ? name + ".blocked" : name;

    /// <summary>Pick a non-existing path inside <paramref name="dir"/>; never overwrites.</summary>
    public static string UniquePath(string dir, string rawName)
    {
        Directory.CreateDirectory(dir);
        var name = NeutraliseRisky(CleanName(rawName));
        var full = Path.Combine(dir, name);
        EnsureInside(dir, full);
        if (!File.Exists(full)) return full;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; i < 10000; i++)
        {
            var cand = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(cand)) return cand;
        }
        throw new IOException("Too many files with the same name.");
    }

    /// <summary>Resolve a file in the shared outbox by name only; null if absent or outside it.</summary>
    public static string? ResolveShared(string dir, string? rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return null;
        if (rawName.Contains('/') || rawName.Contains('\\') || rawName.Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(dir, rawName));
        if (!IsInside(dir, full)) return null;
        var fi = new FileInfo(full);
        if (!fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) != 0) return null;
        return full;
    }

    public static bool IsInside(string dir, string full)
    {
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(full).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureInside(string dir, string full)
    {
        if (!IsInside(dir, full)) throw new InvalidOperationException("Path escapes the inbox.");
    }
}
