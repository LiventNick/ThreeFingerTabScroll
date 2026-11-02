using System.Runtime.InteropServices;
using System.Text;
using static ThreeFingerTabScroll.Native;

namespace ThreeFingerTabScroll;

/// <summary>One finger contact from a touchpad frame. X/Y are raw logical units.</summary>
internal readonly record struct Contact(int Id, bool Tip, bool Confident, int X, int Y)
{
    /// <summary>True when this contact is a finger actually touching (not a lift report or a palm).</summary>
    public bool IsDown => Tip && Confident;
}

/// <summary>A complete touchpad frame: every contact for one scan, possibly assembled from several reports.</summary>
internal sealed class TouchFrame
{
    public required TouchpadDevice Device { get; init; }
    public required IReadOnlyList<Contact> Contacts { get; init; }
    public required long TimestampMs { get; init; }
    /// <summary>How many HID reports this frame was assembled from (more than 1 means hybrid reporting).</summary>
    public required int ReportCount { get; init; }

    public int DownCount
    {
        get
        {
            int n = 0;
            foreach (var c in Contacts) if (c.IsDown) n++;
            return n;
        }
    }
}

/// <summary>
/// Wraps one precision touchpad: reads its HID capabilities once, then parses input reports
/// and assembles them into complete frames.
/// </summary>
internal sealed class TouchpadDevice : IDisposable
{
    /// <summary>One finger "slot" in the report = one logical link collection containing X.</summary>
    private sealed class Slot
    {
        public ushort LinkCollection;
        public bool HasY;
        public bool HasContactId;
        public bool HasConfidence;
    }

    private IntPtr _preparsed;
    private readonly List<Slot> _slots = new();
    private readonly bool _hasContactCount;
    private readonly ushort _contactCountLink;
    private readonly ushort[] _usageBuffer = new ushort[64];
    private byte[] _report = Array.Empty<byte>();

    // Hybrid-mode frame assembly state.
    private readonly List<Contact> _pending = new();
    private int _expected;
    private int _pendingReports;

    public IntPtr Handle { get; }
    public string Name { get; }

    public int XMin { get; }
    public int XMax { get; }
    public int YMin { get; }
    public int YMax { get; }

    /// <summary>Physical width/height in the device's physical units (0 if unknown).</summary>
    public double PhysicalWidth { get; }
    public double PhysicalHeight { get; }

    /// <summary>Multiply a Y delta (logical units) by this to express it in X logical units of equal physical length.</summary>
    public double YToXScale { get; } = 1.0;

    public int SlotCount => _slots.Count;
    public bool HasContactCount => _hasContactCount;

    private TouchpadDevice(IntPtr handle, string name, IntPtr preparsed, HIDP_CAPS caps)
    {
        Handle = handle;
        Name = name;
        _preparsed = preparsed;

        var valueCaps = new HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
        ushort vlen = caps.NumberInputValueCaps;
        if (vlen > 0 && HidP_GetValueCaps(HidP_Input, valueCaps, ref vlen, preparsed) != HIDP_STATUS_SUCCESS)
            vlen = 0;

        var buttonCaps = new HIDP_BUTTON_CAPS[caps.NumberInputButtonCaps];
        ushort blen = caps.NumberInputButtonCaps;
        if (blen > 0 && HidP_GetButtonCaps(HidP_Input, buttonCaps, ref blen, preparsed) != HIDP_STATUS_SUCCESS)
            blen = 0;

        HIDP_VALUE_CAPS? xCap = null, yCap = null;

        for (int i = 0; i < vlen; i++)
        {
            var vc = valueCaps[i];
            if (vc.Covers(HID_USAGE_PAGE_GENERIC, HID_USAGE_GENERIC_X))
            {
                if (_slots.All(s => s.LinkCollection != vc.LinkCollection))
                    _slots.Add(new Slot { LinkCollection = vc.LinkCollection });
                xCap ??= vc;
            }
            else if (vc.Covers(HID_USAGE_PAGE_GENERIC, HID_USAGE_GENERIC_Y))
            {
                yCap ??= vc;
            }
            else if (vc.Covers(HID_USAGE_PAGE_DIGITIZER, HID_USAGE_DIGITIZER_CONTACT_COUNT))
            {
                _hasContactCount = true;
                _contactCountLink = vc.LinkCollection;
            }
        }

        // Fill in per-slot details now that we know the slots.
        foreach (var slot in _slots)
        {
            for (int i = 0; i < vlen; i++)
            {
                var vc = valueCaps[i];
                if (vc.LinkCollection != slot.LinkCollection) continue;
                if (vc.Covers(HID_USAGE_PAGE_GENERIC, HID_USAGE_GENERIC_Y)) slot.HasY = true;
                if (vc.Covers(HID_USAGE_PAGE_DIGITIZER, HID_USAGE_DIGITIZER_CONTACT_ID)) slot.HasContactId = true;
            }
            for (int i = 0; i < blen; i++)
            {
                var bc = buttonCaps[i];
                if (bc.LinkCollection == slot.LinkCollection && bc.Covers(HID_USAGE_PAGE_DIGITIZER, HID_USAGE_DIGITIZER_CONFIDENCE))
                    slot.HasConfidence = true;
            }
        }

        // Slots are reported in link-collection order; valid contacts fill the first slots.
        _slots.Sort((a, b) => a.LinkCollection.CompareTo(b.LinkCollection));

        (XMin, XMax) = LogicalRange(xCap);
        (YMin, YMax) = LogicalRange(yCap);
        PhysicalWidth = PhysicalRange(xCap);
        PhysicalHeight = PhysicalRange(yCap);

        // Physical size per logical unit, so vertical movement can be compared fairly with horizontal.
        if (PhysicalWidth > 0 && PhysicalHeight > 0 && XMax > XMin && YMax > YMin)
        {
            double xUnit = PhysicalWidth / (XMax - XMin);
            double yUnit = PhysicalHeight / (YMax - YMin);
            YToXScale = yUnit / xUnit;
        }
    }

    private static (int min, int max) LogicalRange(HIDP_VALUE_CAPS? cap)
    {
        if (cap is not { } c) return (0, 0);
        int min = c.LogicalMin, max = c.LogicalMax;
        // Some descriptors declare an unsigned max that reads back as negative (e.g. 0xFFFF as -1).
        if (max <= min && c.BitSize is > 0 and < 32)
        {
            min = 0;
            max = (1 << c.BitSize) - 1;
        }
        return (min, max);
    }

    private static double PhysicalRange(HIDP_VALUE_CAPS? cap) =>
        cap is { } c && c.PhysicalMax > c.PhysicalMin ? c.PhysicalMax - c.PhysicalMin : 0;

    /// <summary>Opens a raw input device; returns null if it isn't a usable precision touchpad.</summary>
    public static TouchpadDevice? TryOpen(IntPtr hDevice)
    {
        uint size = 0;
        if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size) != 0 || size == 0)
            return null;

        IntPtr preparsed = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, preparsed, ref size) == uint.MaxValue)
                return null;
            if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS)
                return null;
            if (caps.UsagePage != HID_USAGE_PAGE_DIGITIZER || caps.Usage != HID_USAGE_DIGITIZER_TOUCH_PAD)
                return null;

            var device = new TouchpadDevice(hDevice, GetDeviceName(hDevice), preparsed, caps);
            if (device.SlotCount == 0)
            {
                device.Dispose();
                return null;
            }
            preparsed = IntPtr.Zero; // ownership moved to device
            return device;
        }
        finally
        {
            if (preparsed != IntPtr.Zero) Marshal.FreeHGlobal(preparsed);
        }
    }

    private static string GetDeviceName(IntPtr hDevice)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0) return "(unknown)";
        IntPtr buf = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            return GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buf, ref chars) == uint.MaxValue
                ? "(unknown)"
                : Marshal.PtrToStringUni(buf) ?? "(unknown)";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Touchpad: {Name}");
        sb.AppendLine($"  finger slots per report: {SlotCount}  (contact count field: {(HasContactCount ? "yes" : "NO")})");
        sb.AppendLine($"  X logical range: {XMin}..{XMax}   Y logical range: {YMin}..{YMax}");
        sb.AppendLine($"  physical size: {PhysicalWidth} x {PhysicalHeight} (device units)   Y-to-X scale: {YToXScale:F3}");
        sb.Append($"  confidence bit: {(_slots.All(s => s.HasConfidence) ? "yes" : "no")}   contact id: {(_slots.All(s => s.HasContactId) ? "yes" : "no")}");
        return sb.ToString();
    }

    /// <summary>Normalizes a raw X value to 0..1 across the touchpad width.</summary>
    public double NormalizeX(double x) => XMax > XMin ? (x - XMin) / (XMax - XMin) : 0;

    /// <summary>
    /// Parses one HID input report. Returns a frame once all of its contacts have arrived, otherwise null.
    ///
    /// Hybrid reporting: the first report of a frame carries the total Contact Count and fills as many
    /// slots as it has; follow-up reports carry Contact Count = 0 and fill the rest, in order.
    /// In parallel mode the whole frame fits in one report, which is just the single-report case.
    /// </summary>
    public TouchFrame? ProcessReport(IntPtr data, int length, long timestampMs)
    {
        if (_report.Length != length) _report = new byte[length];
        Marshal.Copy(data, _report, 0, length);
        uint len = (uint)length;

        // Read every slot present in this report.
        var slotContacts = new List<Contact>(_slots.Count);
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_GENERIC, slot.LinkCollection, HID_USAGE_GENERIC_X,
                    out uint x, _preparsed, _report, len) != HIDP_STATUS_SUCCESS)
                continue; // slot not part of this report (different report ID)

            uint y = 0, id = (uint)i;
            if (slot.HasY)
                HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_GENERIC, slot.LinkCollection, HID_USAGE_GENERIC_Y, out y, _preparsed, _report, len);
            if (slot.HasContactId)
                HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_DIGITIZER, slot.LinkCollection, HID_USAGE_DIGITIZER_CONTACT_ID, out id, _preparsed, _report, len);

            bool tip = false, confident = !slot.HasConfidence;
            uint ulen = (uint)_usageBuffer.Length;
            if (HidP_GetUsages(HidP_Input, HID_USAGE_PAGE_DIGITIZER, slot.LinkCollection, _usageBuffer, ref ulen,
                    _preparsed, _report, len) == HIDP_STATUS_SUCCESS)
            {
                for (int u = 0; u < ulen; u++)
                {
                    if (_usageBuffer[u] == HID_USAGE_DIGITIZER_TIP_SWITCH) tip = true;
                    else if (_usageBuffer[u] == HID_USAGE_DIGITIZER_CONFIDENCE) confident = true;
                }
            }

            slotContacts.Add(new Contact((int)id, tip, confident, (int)x, (int)y));
        }

        if (slotContacts.Count == 0)
            return null;

        if (!_hasContactCount)
        {
            // No contact count: treat every report as a complete frame of the touching contacts.
            return new TouchFrame { Device = this, Contacts = slotContacts, TimestampMs = timestampMs, ReportCount = 1 };
        }

        HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_DIGITIZER, _contactCountLink, HID_USAGE_DIGITIZER_CONTACT_COUNT,
            out uint count, _preparsed, _report, len);

        if (count > 0)
        {
            // First report of a new frame. Any unfinished previous frame is dropped.
            _pending.Clear();
            _expected = (int)count;
            _pendingReports = 0;
        }
        else if (_expected == 0)
        {
            return null; // continuation report without a frame start; ignore
        }

        _pendingReports++;
        int take = Math.Min(slotContacts.Count, _expected - _pending.Count);
        for (int i = 0; i < take; i++)
            _pending.Add(slotContacts[i]);

        if (_pending.Count < _expected)
            return null;

        var frame = new TouchFrame
        {
            Device = this,
            Contacts = _pending.ToArray(),
            TimestampMs = timestampMs,
            ReportCount = _pendingReports,
        };
        _pending.Clear();
        _expected = 0;
        _pendingReports = 0;
        return frame;
    }

    public void Dispose()
    {
        if (_preparsed != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_preparsed);
            _preparsed = IntPtr.Zero;
        }
    }
}
