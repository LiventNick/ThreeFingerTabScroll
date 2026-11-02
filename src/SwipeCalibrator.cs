namespace ThreeFingerTabScroll;

/// <summary>
/// Measures how far the center of three fingers travels in one swipe, as a fraction of touchpad width.
/// Reports once per swipe, when the fingers lift.
/// </summary>
internal sealed class SwipeCalibrator
{
    public event Action<double>? Measured;

    private bool _tracking;
    private double _min, _max;
    private readonly int[] _ids = new int[3];

    public void OnFrame(TouchFrame frame)
    {
        Span<int> ids = stackalloc int[3];
        double sumX = 0;
        int down = 0;
        foreach (var c in frame.Contacts)
        {
            if (!c.IsDown) continue;
            if (down < 3) ids[down] = c.Id;
            down++;
            sumX += c.X;
        }

        if (down != 3)
        {
            if (_tracking)
            {
                _tracking = false;
                Measured?.Invoke(_max - _min);
            }
            return;
        }

        ids.Sort();
        double x = frame.Device.NormalizeX(sumX / 3);
        if (!_tracking || !ids.SequenceEqual(_ids))
        {
            // New swipe, or a finger was swapped: start measuring from here.
            _tracking = true;
            _min = _max = x;
            ids.CopyTo(_ids);
            return;
        }
        _min = Math.Min(_min, x);
        _max = Math.Max(_max, x);
    }
}
