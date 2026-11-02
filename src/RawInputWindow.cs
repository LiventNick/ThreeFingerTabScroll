using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static ThreeFingerTabScroll.Native;

namespace ThreeFingerTabScroll;

/// <summary>
/// Hidden window that registers for precision touchpad raw input (works in the background via
/// RIDEV_INPUTSINK) and turns WM_INPUT messages into complete touch frames.
/// </summary>
internal sealed class RawInputWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<IntPtr, TouchpadDevice?> _devices = new();
    private readonly Action<string>? _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
    private IntPtr _buffer;
    private uint _bufferSize;

    public event Action<TouchFrame>? FrameReceived;

    /// <summary>True if at least one precision touchpad is connected.</summary>
    public bool HasTouchpad => _devices.Values.Any(d => d != null);

    public RawInputWindow(Action<string>? log = null)
    {
        _log = log;

        // A plain top-level window that is never shown. (A message-only window would also work,
        // but a normal hidden window is the most reliable target for RIDEV_INPUTSINK.)
        CreateHandle(new CreateParams { Caption = "ThreeFingerTabScroll.RawInput" });

        var rid = new[]
        {
            new RAWINPUTDEVICE
            {
                UsagePage = HID_USAGE_PAGE_DIGITIZER,
                Usage = HID_USAGE_DIGITIZER_TOUCH_PAD,
                Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                Target = Handle,
            },
        };
        if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed");

        EnumerateTouchpads();
    }

    /// <summary>Opens every touchpad that is already connected so its details can be logged up front.</summary>
    private void EnumerateTouchpads()
    {
        uint count = 0;
        uint itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, itemSize);
        if (count == 0) return;

        var list = new RAWINPUTDEVICELIST[count];
        if (GetRawInputDeviceList(list, ref count, itemSize) == uint.MaxValue) return;

        foreach (var d in list.Take((int)count))
        {
            if (d.Type == RIM_TYPEHID)
                GetDevice(d.Device, logIfNew: true);
        }

        if (!_devices.Values.Any(v => v != null))
            _log?.Invoke("No precision touchpad found. (Is this a Precision Touchpad? Check Settings > Bluetooth & devices > Touchpad.)");
    }

    private TouchpadDevice? GetDevice(IntPtr hDevice, bool logIfNew)
    {
        if (_devices.TryGetValue(hDevice, out var existing))
            return existing;

        var device = TouchpadDevice.TryOpen(hDevice);
        _devices[hDevice] = device;
        if (device != null && logIfNew)
            _log?.Invoke(device.Describe());
        return device;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_INPUT:
                try
                {
                    HandleInput(m.LParam);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Error handling input: {ex}");
                }
                break;

            case WM_INPUT_DEVICE_CHANGE:
                if ((int)m.WParam == GIDC_REMOVAL && _devices.Remove(m.LParam, out var removed))
                    removed?.Dispose();
                else if ((int)m.WParam == GIDC_ARRIVAL)
                    GetDevice(m.LParam, logIfNew: true);
                break;
        }

        // DefWindowProc must still run for WM_INPUT so the system can clean up.
        base.WndProc(ref m);
    }

    private void HandleInput(IntPtr hRawInput)
    {
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, (uint)_headerSize);
        if (size == 0) return;

        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferSize = size;
        }

        if (GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, (uint)_headerSize) == uint.MaxValue)
            return;

        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
        if (header.Type != RIM_TYPEHID) return;

        var device = GetDevice(header.Device, logIfNew: true);
        if (device == null) return;

        // RAWHID: DWORD dwSizeHid; DWORD dwCount; BYTE bRawData[dwSizeHid * dwCount]
        int sizeHid = Marshal.ReadInt32(_buffer, _headerSize);
        int countHid = Marshal.ReadInt32(_buffer, _headerSize + 4);
        IntPtr data = _buffer + _headerSize + 8;
        long now = _clock.ElapsedMilliseconds;

        // One WM_INPUT can carry several reports back to back.
        for (int i = 0; i < countHid; i++)
        {
            var frame = device.ProcessReport(data + i * sizeHid, sizeHid, now);
            if (frame != null)
                FrameReceived?.Invoke(frame);
        }
    }

    public void Dispose()
    {
        DestroyHandle();
        foreach (var d in _devices.Values) d?.Dispose();
        _devices.Clear();
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}
