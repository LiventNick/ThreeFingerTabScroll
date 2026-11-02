using System.Runtime.InteropServices;
using static ThreeFingerTabScroll.Native;

namespace ThreeFingerTabScroll;

/// <summary>
/// Sends tab-switch shortcuts with SendInput: Ctrl+Tab / Ctrl+Shift+Tab, or Ctrl+PageDown / Ctrl+PageUp
/// for browsers where Ctrl+Tab may not follow tab-strip order. Modifiers are never left held down.
/// </summary>
internal static class TabSwitcher
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public enum Result { Sent, ModifierHeld, Blocked }

    /// <summary>Moves <paramref name="count"/> tabs right (forward) or left.</summary>
    public static Result Switch(bool forward, int count = 1, bool usePageKeys = false)
    {
        // If the user is physically holding a modifier, our keystroke would combine with it
        // (e.g. Shift held turns "next" into "previous"), and releasing Ctrl would fight their hold.
        if (IsDown(VK_CONTROL) || IsDown(VK_SHIFT) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN))
            return Result.ModifierHeld;

        // The whole sequence goes in one SendInput call, so it can't be interleaved with other input
        // and the key-ups are always queued together with the key-downs.
        count = Math.Clamp(count, 1, 200);
        bool shift = !forward && !usePageKeys;
        ushort key = usePageKeys ? (forward ? VK_NEXT : VK_PRIOR) : VK_TAB;
        bool extended = usePageKeys; // Page Up/Down on the navigation cluster are "extended" keys

        var inputs = new List<INPUT>(4 + count * 2) { Key(VK_CONTROL, false) };
        if (shift) inputs.Add(Key(VK_SHIFT, false));
        for (int i = 0; i < count; i++)
        {
            inputs.Add(Key(key, false, extended));
            inputs.Add(Key(key, true, extended));
        }
        if (shift) inputs.Add(Key(VK_SHIFT, true));
        inputs.Add(Key(VK_CONTROL, true));
        return Send(inputs.ToArray());
    }

    private static Result Send(INPUT[] inputs)
    {
        uint sent = 0;
        try
        {
            sent = SendInput((uint)inputs.Length, inputs, InputSize);
        }
        finally
        {
            // Partial insert (e.g. blocked by UIPI) or an exception: make sure nothing stays down.
            if (sent != inputs.Length)
                ReleaseModifiers();
        }
        return sent == inputs.Length ? Result.Sent : Result.Blocked;
    }

    /// <summary>Best-effort release of every key this class presses. Safe to call any time.</summary>
    public static void ReleaseModifiers()
    {
        try
        {
            var ups = new[]
            {
                Key(VK_TAB, true), Key(VK_NEXT, true, extended: true), Key(VK_PRIOR, true, extended: true),
                Key(VK_SHIFT, true), Key(VK_CONTROL, true),
            };
            SendInput((uint)ups.Length, ups, InputSize);
        }
        catch
        {
            // Nothing more we can do.
        }
    }

    private static bool IsDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static INPUT Key(ushort vk, bool up, bool extended = false) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)MapVirtualKey(vk, 0), // MAPVK_VK_TO_VSC
                dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
            },
        },
    };
}
