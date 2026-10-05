using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GetOverHere;

/// <summary>
/// Reads the touch screen's reports directly (Raw Input, HID page "Digitizer / Touch Screen").
/// Unlike the mouse events Windows only generates when the finger is lifted, these reports arrive as soon
/// as the finger lands: this is what allows remembering the right window before the touched one takes the focus.
/// </summary>
internal sealed class RawTouchInput : NativeWindow, IDisposable
{
    /// <summary>Argument: time of the report (same clock as GetTickCount).</summary>
    private readonly Action<int> _onReport;

    public bool IsRegistered { get; }

    public RawTouchInput(Action<int> onReport)
    {
        _onReport = onReport;
        CreateHandle(new CreateParams { Caption = $"{AppInfo.Id}.RawInput" });

        var devices = new[]
        {
            new Native.RAWINPUTDEVICE
            {
                usUsagePage = Native.HID_USAGE_PAGE_DIGITIZER,
                usUsage = Native.HID_USAGE_DIGITIZER_TOUCH_SCREEN,
                dwFlags = Native.RIDEV_INPUTSINK, // receive reports even when we do not have the focus
                hwndTarget = Handle,
            },
        };
        IsRegistered = Native.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
        DiagnosticLog.Write(IsRegistered
            ? "Lecture directe de l'écran tactile : active"
            : $"Lecture directe de l'écran tactile : ÉCHEC ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_INPUT)
        {
            try
            {
                _onReport(Native.GetMessageTime());
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"ERREUR raw input : {ex}");
            }
        }
        base.WndProc(ref m); // DefWindowProc releases the report data
    }

    public void Dispose() => DestroyHandle();
}
