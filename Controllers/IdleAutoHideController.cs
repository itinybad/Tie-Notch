using System;
using System.Windows.Threading;
using VNotch.Models;
using VNotch.Services;

namespace VNotch.Controllers;

public sealed class IdleAutoHideController : IDisposable
{
    private const int IdleHidePollIntervalMs = 500;

    private readonly NotchShellState _shellState;
    private readonly Func<NotchSettings> _getSettings;
    private readonly Func<bool> _isNotchEmptyAndIdle;
    private readonly Action _applyVisibilityState;
    private readonly Action _stopHoverTimers;
    private readonly Action _onRevealed;
    private readonly Func<bool> _shouldSliver;
    private readonly Func<bool, bool> _applySliverState;

    private readonly DispatcherTimer _timer;
    private DateTime _idleSinceUtc = DateTime.MinValue;
    private bool _isSlivered;
    private bool _disposed;

    public IdleAutoHideController(
        NotchShellState shellState,
        Func<NotchSettings> getSettings,
        Func<bool> isNotchEmptyAndIdle,
        Action applyVisibilityState,
        Action stopHoverTimers,
        Action onRevealed,
        Func<bool> shouldSliver,
        Func<bool, bool> applySliverState)
    {
        _shellState = shellState ?? throw new ArgumentNullException(nameof(shellState));
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _isNotchEmptyAndIdle = isNotchEmptyAndIdle ?? throw new ArgumentNullException(nameof(isNotchEmptyAndIdle));
        _applyVisibilityState = applyVisibilityState ?? throw new ArgumentNullException(nameof(applyVisibilityState));
        _stopHoverTimers = stopHoverTimers ?? throw new ArgumentNullException(nameof(stopHoverTimers));
        _onRevealed = onRevealed ?? throw new ArgumentNullException(nameof(onRevealed));
        _shouldSliver = shouldSliver ?? throw new ArgumentNullException(nameof(shouldSliver));
        _applySliverState = applySliverState ?? throw new ArgumentNullException(nameof(applySliverState));

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(IdleHidePollIntervalMs)
        };
        _timer.Tick += IdleHideTimer_Tick;
    }

    public DispatcherTimer Timer => _timer;

    public bool IsSlivered => _isSlivered;

    private bool IsHiddenByIdle
    {
        get => _shellState.IsHiddenByIdle;
        set => _shellState.IsHiddenByIdle = value;
    }

    public void ApplySettings()
    {
        var settings = _getSettings();

        if (settings.EnableIdleAutoHide || settings.EnableSliverMode)
        {
            _idleSinceUtc = DateTime.MinValue;
            if (!_timer.IsEnabled)
                _timer.Start();
        }
        else
        {
            _timer.Stop();
            _idleSinceUtc = DateTime.MinValue;

            if (IsHiddenByIdle)
            {
                IsHiddenByIdle = false;
                _applyVisibilityState();
                if (_shellState.IsEffectivelyNotchVisible)
                    _onRevealed();
            }
        }

        if (!settings.EnableSliverMode)
            LeaveSliver();
    }

    private void IdleHideTimer_Tick(object? sender, EventArgs e)
    {
        var settings = _getSettings();

        if (!settings.EnableIdleAutoHide && !settings.EnableSliverMode)
        {
            _timer.Stop();
            return;
        }

        bool idle = _isNotchEmptyAndIdle();

        if (!idle)
        {
            _idleSinceUtc = DateTime.MinValue;
            if (IsHiddenByIdle || _isSlivered)
                Wake();
            return;
        }

        if (IsHiddenByIdle) return;

        if (_idleSinceUtc == DateTime.MinValue)
        {
            _idleSinceUtc = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _idleSinceUtc).TotalMilliseconds >= settings.IdleAutoHideDelay)
        {
            if (_shouldSliver())
                EnterSliver();
            else if (settings.EnableIdleAutoHide && !_isSlivered)
                HideForIdle();
        }
    }

    private void HideForIdle()
    {
        if (IsHiddenByIdle) return;

        LeaveSliver();

        IsHiddenByIdle = true;
        RuntimeLog.Log("IDLE-HIDE", $"Notch hidden after {_getSettings().IdleAutoHideDelay}ms idle");

        _stopHoverTimers();

        _applyVisibilityState();
    }

    private void EnterSliver()
    {
        if (_isSlivered) return;

        // 展开动画等场景会拒绝收缩，此时保持原状，下一轮空闲再试。
        if (!_applySliverState(true)) return;

        _isSlivered = true;
        RuntimeLog.Log("IDLE-SLIVER", $"Notch shrank to the thin bar after {_getSettings().IdleAutoHideDelay}ms idle");

        _stopHoverTimers();
    }

    private void LeaveSliver()
    {
        if (!_isSlivered) return;

        _isSlivered = false;
        RuntimeLog.Log("IDLE-SLIVER", "Notch restored from the thin bar");
        _applySliverState(false);
    }

    public void Wake()
    {
        var settings = _getSettings();

        if (!settings.EnableIdleAutoHide && !settings.EnableSliverMode) return;

        _idleSinceUtc = DateTime.MinValue;

        LeaveSliver();

        if (!IsHiddenByIdle) return;

        IsHiddenByIdle = false;
        RuntimeLog.Log("IDLE-HIDE", "Notch revealed from idle (activity detected)");

        _applyVisibilityState();

        if (_shellState.IsEffectivelyNotchVisible)
            _onRevealed();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= IdleHideTimer_Tick;
    }
}
