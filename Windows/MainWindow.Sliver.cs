using System;
using System.Windows;
using System.Windows.Media.Animation;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

/// <summary>
/// 收缩态（细条）：空闲时把凹槽压成一条很细的横条，鼠标靠上来再长回胶囊。
/// 该形态只改变表现层几何与内容透明度，不影响 NotchTransitionCoordinator 的状态机。
/// </summary>
public partial class MainWindow
{
    private const string SliverLogTag = "IDLE-SLIVER";
    private const double SliverMinHeight = 4;
    private const double SliverMaxHeight = 20;

    private bool _isSliver;
    private int _sliverFadeVersion;

    internal bool IsSliver => _isSliver;

    private double SliverHeightDip => Math.Clamp(_settings.SliverHeight, SliverMinHeight, SliverMaxHeight);

    /// <summary>
    /// 应用细条形态；返回后调用方可用返回值判断该形态是否已经生效（展开动画中会拒绝收缩）。
    /// </summary>
    internal bool ApplySliverState(bool sliver)
    {
        if (NotchBorder == null) return false;

        if (sliver)
        {
            if (!_settings.EnableSliverMode || _isSliver) return _isSliver;
            if (_isExpanded || _isMusicExpanded || _isAnimating) return false;

            _isSliver = true;
            AnimateSliverGeometry(true);
            FadeSliverContent(0);
            UpdateEarVisibility();
            VNotch.Services.RuntimeLog.Log(SliverLogTag, $"Thin bar entered (height={SliverHeightDip:F0})");
            return true;
        }

        if (!_isSliver) return true;

        _isSliver = false;
        AnimateSliverGeometry(false);
        FadeSliverContent(1);
        UpdateEarVisibility();
        VNotch.Services.RuntimeLog.Log(SliverLogTag, "Thin bar left");
        return true;
    }

    /// <summary>
    /// 设置变更后重新贴合当前形态（细条高度、开关或胶囊尺寸变化时调用）。
    /// </summary>
    internal void RefreshSliverPresentation()
    {
        if (!_isSliver) return;

        if (!_settings.EnableSliverMode)
        {
            ApplySliverState(false);
            return;
        }

        AnimateSliverGeometry(true);
    }

    private void AnimateSliverGeometry(bool toSliver)
    {
        double targetHeight = toSliver ? SliverHeightDip : _collapsedHeight;
        double targetRadius = toSliver
            ? Math.Min(_cornerRadiusCollapsed, SliverHeightDip / 2.0)
            : _cornerRadiusCollapsed;
        var duration = toSliver ? _dur400 : _dur350;

        if (VNotch.Services.AnimationConfig.ReduceMotion)
        {
            NotchBorder.BeginAnimation(HeightProperty, null);
            NotchBorder.Height = targetHeight;
            this.BeginAnimation(CurrentCornerRadiusProperty, null);
            CurrentCornerRadius = targetRadius;
            return;
        }

        double fromHeight = NotchBorder.ActualHeight > 0 ? NotchBorder.ActualHeight : NotchBorder.Height;
        if (double.IsNaN(fromHeight) || double.IsInfinity(fromHeight) || fromHeight <= 0)
            fromHeight = targetHeight;

        var heightAnim = MakeAnim(fromHeight, targetHeight, duration,
            toSliver ? (IEasingFunction)_easeExpOut6 : _easeSoftSpring);
        heightAnim.Completed += (_, _) =>
        {
            // 先把目标值落到基值上，再摘掉时钟，避免动画结束瞬间回跳。
            NotchBorder.Height = targetHeight;
            NotchBorder.BeginAnimation(HeightProperty, null);
        };
        NotchBorder.BeginAnimation(HeightProperty, heightAnim, HandoffBehavior.SnapshotAndReplace);

        AnimateCornerRadius(targetRadius, duration.TimeSpan);
    }

    private void FadeSliverContent(double target)
    {
        int version = ++_sliverFadeVersion;

        foreach (FrameworkElement? host in new FrameworkElement?[] { NotchLiveContent, NotchContent })
        {
            if (host == null) continue;

            var fade = MakeAnim(host.Opacity, target, _dur250, _easeQuadOut);
            fade.Completed += (_, _) =>
            {
                if (version != _sliverFadeVersion) return;

                host.Opacity = target;
                host.BeginAnimation(OpacityProperty, null);
            };
            host.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
