using AirGlass.Core.Link;

namespace AirGlass.Link;

/// <summary>Applies validated phone input on the PC. Implemented by the Windows app.</summary>
public interface IInputSink
{
    void Apply(InputMessage msg);
}

/// <summary>Clipboard access, only ever invoked by an explicit tap on a paired phone.</summary>
public interface IClipboardSink
{
    void SetText(string text);
    string? GetText();
}
