using System;
using System.Threading;
using System.Threading.Tasks;
using VNotch.Controllers;
using VNotch.Models;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests;

public sealed class IdleSliverControllerTests
{
    [Fact]
    public void ThinBarAppearsWhenIdleAndLeavesOnActivity() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var harness = new Harness();
        harness.Controller.ApplySettings();

        await WaitUntilAsync(() => harness.Controller.IsSlivered, ct);
        Assert.True(harness.LastSliverState);

        // 光标靠近时 IsNotchEmptyAndIdle 转为 false，轮询应把凹槽唤醒回胶囊。
        harness.Idle = false;
        await WaitUntilAsync(() => !harness.Controller.IsSlivered, ct);
        Assert.False(harness.LastSliverState);
    });

    [Fact]
    public void WakeRestoresTheCapsuleImmediately() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var harness = new Harness();
        harness.Controller.ApplySettings();
        await WaitUntilAsync(() => harness.Controller.IsSlivered, ct);

        harness.Controller.Wake();

        Assert.False(harness.Controller.IsSlivered);
        Assert.False(harness.LastSliverState);
        Assert.False(harness.Shell.IsHiddenByIdle);
    });

    [Fact]
    public void TurningTheSettingOffRestoresTheCapsule() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var harness = new Harness();
        harness.Controller.ApplySettings();
        await WaitUntilAsync(() => harness.Controller.IsSlivered, ct);

        harness.Settings.EnableSliverMode = false;
        harness.Controller.ApplySettings();

        Assert.False(harness.Controller.IsSlivered);
        Assert.False(harness.LastSliverState);
        Assert.False(harness.Controller.Timer.IsEnabled);
    });

    [Fact]
    public void CapsuleIsKeptWhileTheThinBarIsRefused() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var harness = new Harness();
        harness.SliverAllowed = false;
        harness.Controller.ApplySettings();

        await Task.Delay(1400, ct);

        Assert.False(harness.Controller.IsSlivered);
        Assert.Equal(0, harness.SliverTransitions);
        Assert.False(harness.Shell.IsHiddenByIdle);
    });

    [Fact]
    public void AutoHideStillHidesWhenTheThinBarIsNotAllowed() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var harness = new Harness();
        harness.SliverAllowed = false;
        harness.Settings.EnableIdleAutoHide = true;
        harness.Controller.ApplySettings();

        await WaitUntilAsync(() => harness.Shell.IsHiddenByIdle, ct);

        Assert.False(harness.Controller.IsSlivered);
        Assert.True(harness.VisibilityApplies >= 1);
    });

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("条件在超时前未满足。");

            await Task.Delay(40, ct);
        }
    }

    private sealed class Harness : IDisposable
    {
        internal NotchSettings Settings { get; } = new()
        {
            EnableIdleAutoHide = false,
            EnableSliverMode = true,
            IdleAutoHideDelay = 400
        };

        internal NotchShellState Shell { get; } = new();

        internal bool Idle { get; set; } = true;

        internal bool SliverAllowed { get; set; } = true;

        internal bool LastSliverState { get; private set; }

        internal int SliverTransitions { get; private set; }

        internal int VisibilityApplies { get; private set; }

        internal IdleAutoHideController Controller { get; }

        internal Harness()
        {
            Controller = new IdleAutoHideController(
                Shell,
                getSettings: () => Settings,
                isNotchEmptyAndIdle: () => Idle,
                applyVisibilityState: () => VisibilityApplies++,
                stopHoverTimers: () => { },
                onRevealed: () => { },
                shouldSliver: () => Settings.EnableSliverMode && SliverAllowed,
                applySliverState: state =>
                {
                    LastSliverState = state;
                    SliverTransitions++;
                    return true;
                });
        }

        public void Dispose() => Controller.Dispose();
    }
}
