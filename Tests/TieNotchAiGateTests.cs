using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VNotch.Models;
using Xunit;

namespace VNotch.Tests;

// Tie-Notch: 联网 AI 未允许时，Tab 与图标点击都不得进入 AI 模式。
[Collection("Localization")]
public sealed class TieNotchAiGateTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void TabDoesNotEnterAiModeWhenOnlineAiIsDisabled() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var fixture = new SpotlightWindowFixture(new NotchSettings { NotchStyle = "default" });
        var window = fixture.Window;
        window.ShowSpotlight();
        await WpfFrameWaiter.UntilAsync(() => !Field<bool>(window, "_entranceActive"), "search entrance", ct);
        PressTab(window);
        Assert.False(Field<bool>(window, "_aiMode"));
        Assert.False(Field<bool>(window, "_escBadgeVisible"));
    });

    [Fact]
    public void TabEntersAiModeWhenOnlineAiIsAllowed() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var fixture = new SpotlightWindowFixture(new NotchSettings { NotchStyle = "default", AllowOnlineAi = true });
        var window = fixture.Window;
        window.ShowSpotlight();
        await WpfFrameWaiter.UntilAsync(() => !Field<bool>(window, "_entranceActive"), "search entrance", ct);
        PressTab(window);
        Assert.True(Field<bool>(window, "_aiMode"));
    });

    private static void PressTab(SpotlightWindow window)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, Key.Tab)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        typeof(SpotlightWindow).GetMethod("Window_PreviewKeyDown", Private)!.Invoke(window, [window, args]);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private)!.GetValue(target)!;

    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}

