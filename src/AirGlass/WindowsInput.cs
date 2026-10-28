using System.Runtime.InteropServices;
using AirGlass.Core.Link;
using AirGlass.Link;

namespace AirGlass;

/// <summary>Applies validated phone input with SendInput and handles clipboard on the UI thread.</summary>
public sealed class WindowsInput : IInputSink, IClipboardSink
{
    private readonly Control _ui;
    public WindowsInput(Control ui) => _ui = ui;

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x1, LEFTDOWN = 0x2, LEFTUP = 0x4, RIGHTDOWN = 0x8, RIGHTUP = 0x10, WHEEL = 0x800;
    private const uint KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    private static void Send(params INPUT[] i) => SendInput((uint)i.Length, i, Marshal.SizeOf<INPUT>());

    private static INPUT Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, mouseData = data } },
    };

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private static readonly Dictionary<string, ushort> Vk = new()
    {
        ["enter"] = 0x0D, ["backspace"] = 0x08, ["tab"] = 0x09, ["escape"] = 0x1B,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["delete"] = 0x2E, ["home"] = 0x24, ["end"] = 0x23,
    };

    public void Apply(InputMessage m)
    {
        switch (m.Kind)
        {
            case InputKind.Move:
                Send(Mouse(MOUSEEVENTF_MOVE, m.A, m.B));
                break;
            case InputKind.Click:
                if (m.A == 0) Send(Mouse(LEFTDOWN), Mouse(LEFTUP));
                else if (m.A == 1) Send(Mouse(RIGHTDOWN), Mouse(RIGHTUP));
                else Send(Mouse(LEFTDOWN), Mouse(LEFTUP), Mouse(LEFTDOWN), Mouse(LEFTUP));
                break;
            case InputKind.Scroll:
                Send(Mouse(WHEEL, data: unchecked((uint)(m.A * 40))));
                break;
            case InputKind.Key:
                if (m.Text is not null && Vk.TryGetValue(m.Text, out var vk))
                    Send(Key(vk, 0, 0), Key(vk, 0, KEYEVENTF_KEYUP));
                break;
            case InputKind.Text:
                foreach (var ch in m.Text ?? "")
                {
                    if (ch == '\n') { Send(Key(0x0D, 0, 0), Key(0x0D, 0, KEYEVENTF_KEYUP)); continue; }
                    Send(Key(0, ch, KEYEVENTF_UNICODE), Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
                }
                break;
        }
    }

    public void SetText(string text)
    {
        _ui.Invoke(() => { try { Clipboard.SetText(text); } catch (ExternalException) { } });
    }

    public string? GetText()
    {
        string? r = null;
        _ui.Invoke(() => { try { r = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch (ExternalException) { } });
        return r is { Length: > 10000 } ? r[..10000] : r;
    }
}
