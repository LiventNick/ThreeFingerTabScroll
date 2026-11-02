namespace ThreeFingerTabScroll;

/// <summary>
/// Turns touch frames into tab steps. While exactly three fingers are down, horizontal movement of
/// their average position is accumulated, and every <see cref="StepFraction"/> of touchpad width
/// produces one step (+1 = next tab, -1 = previous tab).
/// </summary>
internal sealed class GestureDetector
{
    private enum Axis { Undecided, Horizontal, Vertical }

    /// <summary>Horizontal travel per tab, as a fraction of the touchpad width (0.10 = 10%).</summary>
    public double StepFraction { get; set; } = 0.10;

    /// <summary>Movement (fraction of width) needed before deciding whether the gesture is horizontal or vertical.</summary>
    public double AxisLockFraction { get; set; } = 0.025;

    /// <summary>Swap directions (slide right = previous tab).</summary>
    public bool Invert { get; set; }

    /// <summary>Raised for each tab step: +1 next, -1 previous.</summary>
    public event Action<int>? Step;

    /// <summary>Exactly three fingers just touched down (direction not known yet).</summary>
    public event Action? Started;

    /// <summary>The gesture was recognized as horizontal; steps may follow.</summary>
    public event Action? Locked;

    /// <summary>The gesture finished (fingers lifted, or a finger was added).</summary>
    public event Action? Ended;

    /// <summary>True while three fingers are down.</summary>
    public bool IsActive => _active;

    /// <summary>Optional diagnostic log.</summary>
    public Action<string>? Log { get; set; }

    private const int RequiredFingers = 3;
    private const int MaxStepsPerFrame = 3;     // guards against a glitchy jump producing a burst of switches
    private const long FrameGapResetMs = 250;   // if frames stop this long, re-baseline instead of applying a jump

    private bool _active;
    private Axis _axis;
    private double _accumulator;   // fraction of width, signed
    private double _lockDx, _lockDy;
    private int _stepsThisGesture;

    // Baseline for the next delta: which fingers, where they were, and when.
    private readonly int[] _ids = new int[RequiredFingers];
    private double _lastX, _lastY;
    private long _lastTimeMs;
    private TouchpadDevice? _lastDevice;

    public void Reset()
    {
        bool wasActive = _active;
        if (wasActive)
            Log?.Invoke($"gesture end ({_stepsThisGesture} step(s))");
        _active = false;
        _axis = Axis.Undecided;
        _accumulator = 0;
        _lockDx = _lockDy = 0;
        _stepsThisGesture = 0;
        _lastDevice = null;
        if (wasActive)
            Ended?.Invoke();
    }

    public void OnFrame(TouchFrame frame)
    {
        // Collect the fingers that are actually touching (tip down, not a palm).
        Span<int> ids = stackalloc int[RequiredFingers];
        double sumX = 0, sumY = 0;
        int down = 0;
        foreach (var c in frame.Contacts)
        {
            if (!c.IsDown) continue;
            if (down < RequiredFingers) ids[down] = c.Id;
            down++;
            sumX += c.X;
            sumY += c.Y;
        }

        if (down != RequiredFingers)
        {
            Reset();
            return;
        }

        ids.Sort();
        var dev = frame.Device;
        double width = dev.XMax - dev.XMin;
        if (width <= 0) return;

        // Average position as a fraction of width; Y is scaled so equal physical distances compare equally.
        double x = sumX / RequiredFingers / width;
        double y = sumY / RequiredFingers * dev.YToXScale / width;

        bool sameFingers = _lastDevice == dev && ids.SequenceEqual(_ids);
        bool gap = frame.TimestampMs - _lastTimeMs > FrameGapResetMs;

        if (!_active)
        {
            _active = true;
            Log?.Invoke("gesture start (3 fingers)");
            Started?.Invoke();
        }
        else if (sameFingers && !gap)
        {
            Apply(x - _lastX, y - _lastY);
        }
        // Otherwise a finger was swapped or frames were lost: just take a new baseline.
        // This avoids the jump in average position when the set of fingers changes.

        ids.CopyTo(_ids);
        _lastX = x;
        _lastY = y;
        _lastTimeMs = frame.TimestampMs;
        _lastDevice = dev;
    }

    private void Apply(double dx, double dy)
    {
        switch (_axis)
        {
            case Axis.Vertical:
                return; // mostly-vertical gesture: ignored until the fingers lift

            case Axis.Undecided:
                _lockDx += dx;
                _lockDy += dy;
                if (Math.Sqrt(_lockDx * _lockDx + _lockDy * _lockDy) < AxisLockFraction)
                    return;
                if (Math.Abs(_lockDy) > Math.Abs(_lockDx))
                {
                    _axis = Axis.Vertical;
                    Log?.Invoke("axis: vertical (ignored)");
                    return;
                }
                _axis = Axis.Horizontal;
                Log?.Invoke("axis: horizontal");
                Locked?.Invoke();
                dx = _lockDx; // the movement used to decide still counts toward the first step
                break;
        }

        _accumulator += dx;
        double step = Math.Max(StepFraction, 0.01);
        int steps = 0;

        while (_accumulator >= step && steps < MaxStepsPerFrame)
        {
            _accumulator -= step;
            steps++;
            Emit(+1);
        }
        while (_accumulator <= -step && steps < MaxStepsPerFrame)
        {
            _accumulator += step;
            steps++;
            Emit(-1);
        }

        // If the per-frame cap was hit, drop the excess rather than carrying a backlog.
        _accumulator = Math.Clamp(_accumulator, -step, step);
    }

    private void Emit(int direction)
    {
        _stepsThisGesture++;
        Step?.Invoke(Invert ? -direction : direction);
    }
}
