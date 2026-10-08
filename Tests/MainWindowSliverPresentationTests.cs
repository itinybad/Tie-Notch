using System;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class MainWindowSliverPresentationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void ThinBarShrinksGeometryHidesContentAndRestoresTheCapsule() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var fixture = CreateFixture(sliverHeight: 10);
        var window = fixture.Window;
        double collapsedHeight = Field<double>(window, "_collapsedHeight");

        Assert.True(ApplySliver(window, true));
        Assert.True(window.IsSliver);

        await WpfFrameWaiter.UntilAsync(() => Math.Abs(window.NotchBorder.Height - 10) < 0.01, "细条高度", ct);
        await WpfFrameWaiter.UntilAsync(() => window.NotchLiveContent.Opacity < 0.01, "细条内容淡出", ct);
        Assert.Equal(Visibility.Collapsed, window.LeftEar.Visibility);
        Assert.Equal(Visibility.Collapsed, window.RightEar.Visibility);

        Assert.True(ApplySliver(window, false));
        Assert.False(window.IsSliver);

        await WpfFrameWaiter.UntilAsync(() => Math.Abs(window.NotchBorder.Height - collapsedHeight) < 0.01, "胶囊高度", ct);
        await WpfFrameWaiter.UntilAsync(() => window.NotchLiveContent.Opacity > 0.99, "胶囊内容淡入", ct);
        Assert.Equal(Visibility.Visible, window.LeftEar.Visibility);
    });

    [Fact]
    public void ThinBarClampsHeightAndRadiusToTheSliderRange() => SharedStaTestRunner.Run(() =>
    {
        using var fixture = CreateFixture(sliverHeight: 2);
        bool previous = AnimationConfig.ReduceMotion;
        try
        {
            AnimationConfig.SetReduceMotion(true);

            Assert.True(ApplySliver(fixture.Window, true));

            Assert.Equal(4d, fixture.Window.NotchBorder.Height);
            Assert.Equal(2d, fixture.Window.NotchBorder.CornerRadius.BottomLeft);
            Assert.Equal(0d, fixture.Window.NotchBorder.CornerRadius.TopLeft);
        }
        finally { AnimationConfig.SetReduceMotion(previous); }
    });

    private static GreetingAcceptanceTests.MainWindowFixture CreateFixture(int sliverHeight) =>
        new("en", greeting: false,
            configureSettings: settings =>
            {
                settings.EnableSliverMode = true;
                settings.SliverHeight = sliverHeight;
            },
            configureServices: services =>
                services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));

    private static bool ApplySliver(MainWindow window, bool sliver) =>
        (bool)typeof(MainWindow).GetMethod("ApplySliverState", Private)!.Invoke(window, new object[] { sliver })!;

    private static T Field<T>(MainWindow window, string name) =>
        (T)(typeof(MainWindow).GetField(name, Private)?.GetValue(window)
            ?? typeof(MainWindow).GetProperty(name, Private)!.GetValue(window))!;
}
