using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GameFlow.App.Converters;
using GameFlow.App.Bootstrap;
using GameFlow.App.Services;
using GameFlow.App.ViewModels;
using GameFlow.App.Views;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Slots;
using GameFlow.Infrastructure.Runtime.Input;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Theming;
using GameFlow.Infrastructure.Theming.Flee;
using GameFlow.Infrastructure.Theming.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace GameFlow.Ui.Tests;

public sealed class DeviceFlowTests(UiFixture fixture) : IClassFixture<UiFixture>
{
    private async Task Run(Func<IServiceProvider, Task> action)
    {
        Environment.SetEnvironmentVariable("GAMEFLOW_DATA_DIRECTORY",
            Path.Combine(Path.GetTempPath(), "gameflow-ui-test-" + Guid.NewGuid().ToString("N")));
        await fixture.Session.Dispatch(async () =>
        {
            var host = HostBuilderFactory.Create([]).Build();
            await host.Services.GetRequiredService<IUserSettingsService>().InitializeAsync();
            var localizer = host.Services.GetRequiredService<ILocalizationService>();
            localizer.SetCulture("en");
            UiTextResources.Attach(localizer);
            KeyboardMouseLegend.Attach(host.Services.GetRequiredService<KeyboardMouseMapStore>());
            try { await action(host.Services); }
            finally
            {
                UiTextResources.Detach();
                KeyboardMouseLegend.Detach();
                await Task.Run(host.Dispose).WaitAsync(TimeSpan.FromSeconds(10));
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public Task EmptySelectionDoesNotSelectTheFirstController() => Run(services =>
    {
        var catalog = services.GetRequiredService<InputDeviceCatalog>();
        catalog.ReplaceDevices("test", [Pad("one"), Pad("two"), Pad("three")]);
        var devices = services.GetRequiredService<DevicesViewModel>();
        devices.SelectedDevice = devices.Devices.Single(device => device.Id == "two");
        var selected = new List<string?>();
        catalog.Updated += (_, _) => selected.Add(catalog.SelectedDeviceId);
        devices.SelectedDevice = null;
        devices.SelectedDevice = devices.Devices.Single(device => device.Id == "three");
        Assert.DoesNotContain("one", selected);
        Assert.Equal("three", catalog.SelectedDeviceId);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SetupAssignsTheSelectedMiceAndLeavesOtherInputsUnassigned() => Run(services =>
    {
        var catalog = services.GetRequiredService<InputDeviceCatalog>();
        catalog.ReplaceDevices("test", [Input("keyboard", DeviceCategory.Keyboard), Input("mouse-1", DeviceCategory.Mouse), Input("mouse-2", DeviceCategory.Mouse)]);
        using var setup = services.GetRequiredService<SetupWalkthroughViewModel>();
        setup.NextCommand.Execute(null);
        Assert.False(setup.NextCommand.CanExecute(null));
        var window = new SetupWalkthroughWindow { DataContext = setup };
        window.Show();
        var list = window.FindControl<ListBox>("InputDevicesList")!;
        list.SelectedItems!.Add(setup.DetectedDevices.Single(device => device.Id == "mouse-1"));
        list.SelectedItems.Add(setup.DetectedDevices.Single(device => device.Id == "mouse-2"));
        Assert.Equal(2, setup.SelectedInputs.Count);
        Assert.True(setup.NextCommand.CanExecute(null));
        setup.NextCommand.Execute(null);
        Assert.True(setup.IsKeyboardMouseStep);
        setup.NextCommand.Execute(null);
        setup.NextCommand.Execute(null);
        setup.NextCommand.Execute(null);
        var slot = services.GetRequiredService<SlotRegistry>().GetSlot(setup.CreatedSlotId!);
        Assert.NotNull(slot);
        Assert.Equal(["mouse-1", "mouse-2"], slot.InputDeviceIds.Order().ToArray());
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task RulePickerKeepsAUsableWidth() => Run(_ =>
    {
        var view = new MappingEditorView { DataContext = new MappingEditorViewModel(NullLogger<MappingEditorViewModel>.Instance) };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var picker = view.FindControl<ComboBox>("RuleKindPicker")!;
        Assert.True(picker.Bounds.Width >= 200, $"Rule picker width: {picker.Bounds.Width}");
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task SkinPickerDoesNotOpenControllerTuning() => Run(_ =>
    {
        ThemeBootstrap.EnsureBundledThemesInstalled();
        var registry = new ThemeRegistry(); registry.Refresh();
        var model = new ControllerVisualStateViewModel(_ => { }) { ThemeRegistry = registry };
        model.SetPanelKind(false);
        model.Update("test:virtual", "Virtual output", new ControllerSnapshot(), ControllerVisualStyle.PlayStation5);
        var opens = 0; model.EditRequested += (_, _) => opens++;
        var view = new ControllerSurface { DataContext = model };
        var window = new Window { Width = 600, Height = 450, Content = view };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var picker = view.GetVisualDescendants().OfType<ComboBox>().Single();
        var point = picker.TranslatePoint(new Point(picker.Bounds.Width / 2, picker.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Assert.Equal(0, opens);
        Assert.True(picker.IsDropDownOpen);
        picker.IsDropDownOpen = false;
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClickingAKeyKeepsItsDeviceAndCanBindNumpadEnter() => Run(services =>
    {
        services.GetRequiredService<InputDeviceCatalog>().ReplaceDevices("test", [Input("keyboard-2", DeviceCategory.Keyboard)]);
        var model = new ControllerVisualStateViewModel(_ => { });
        model.SetInputDeviceIds("keyboard-2", null);
        KeyboardMouseMapRequest? request = null;
        model.InputMappingRequested += (_, value) => request = value;
        var view = new KeyboardSurface { DataContext = model };
        var window = new Window { Width = 850, Height = 350, Content = view };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var cap = view.GetVisualDescendants().OfType<Border>().Single(control => control.Tag is int key && key == KeyboardVirtualKeys.NumpadEnter);
        var point = cap.TranslatePoint(new Point(cap.Bounds.Width / 2, cap.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Assert.NotNull(request);
        Assert.Equal("keyboard-2", request.DeviceId);
        Assert.Equal(KeyboardVirtualKeys.NumpadEnter, request.Key);
        using var editor = services.GetRequiredService<KeyboardMouseMapEditorViewModel>();
        editor.SelectDevice(request.DeviceId);
        editor.PrepareInput(request.Key, request.Mouse);
        editor.Rows.Single(row => row.Action == GamepadAction.South).AddCommand.Execute(null);
        var store = services.GetRequiredService<KeyboardMouseMapStore>();
        Assert.Contains(store.CurrentFor("keyboard-2").Bindings, binding => binding.Key == KeyboardVirtualKeys.NumpadEnter && binding.Action == GamepadAction.South);
        Assert.DoesNotContain(store.Current.Bindings, binding => binding.Key == KeyboardVirtualKeys.NumpadEnter);
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClickingWheelUpRequestsThatMouseAndDirection() => Run(_ =>
    {
        var model = new ControllerVisualStateViewModel(_ => { });
        model.SetInputDeviceIds(null, "mouse-2");
        KeyboardMouseMapRequest? request = null;
        model.InputMappingRequested += (_, value) => request = value;
        var view = new MouseSurface { DataContext = model };
        var window = new Window { Width = 500, Height = 350, Content = view };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var button = view.GetVisualDescendants().OfType<Border>().Single(control => control.Tag as string == "WheelUp");
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Assert.NotNull(request);
        Assert.Equal("mouse-2", request.DeviceId);
        Assert.Equal(MouseInput.WheelUp, request.Mouse);
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpenPickersAndNativeOverlayFollowEverySupportedLanguage() => Run(services =>
    {
        var localizer = services.GetRequiredService<ILocalizationService>();
        using var editor = services.GetRequiredService<KeyboardMouseMapEditorViewModel>();
        var button = new ButtonIdOption(ButtonId.South);
        var mode = new RuleModeOption(RuleMode.Modify);
        var kind = new RuleKindOption(RuleKind.ButtonAutofire, "#ffffff");
        var buttonText = new TextBlock { DataContext = button };
        buttonText.Bind(TextBlock.TextProperty, new Binding(nameof(ButtonIdOption.Label)));
        var kindText = new TextBlock { DataContext = kind };
        kindText.Bind(TextBlock.TextProperty, new Binding(nameof(RuleKindOption.Label)));
        var mapText = new TextBlock { DataContext = editor.Rows.Single(row => row.Action == GamepadAction.LeftStickWalk) };
        mapText.Bind(TextBlock.TextProperty, new Binding(nameof(KeyboardMouseActionRow.Label)));
        var window = new Window { Content = new StackPanel { Children = { buttonText, kindText, mapText } } };
        window.Show();
        var model = new ControllerVisualStateViewModel(_ => { });
        model.Update("keyboard", "input", new ControllerSnapshot(), ControllerVisualStyle.KeyboardMouse);
        var choices = new OverlayThemeChoices();
        using var panel = new DashboardControllerPanelViewModel("slot", "test", model,
            new ControllerVisualStateViewModel(_ => { }), "", choices);
        foreach (var language in localizer.SupportedLanguages)
        {
            localizer.SetCulture(language.Code);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(localizer["ButtonId.South"], buttonText.Text);
            Assert.Equal(localizer["RuleKindButtonAutofireLabel"], kindText.Text);
            Assert.Equal(kindText.Text, MappingRuleViewModel.CreateDefault(RuleKind.ButtonAutofire).Name);
            Assert.Equal(localizer["GamepadAction.LeftStickWalk"], mapText.Text);
            Assert.Equal(localizer["RuleMode.Modify"], mode.Label);
            Assert.Equal(localizer["InputMapDefaults"], editor.Devices[0].Label);
            Assert.Equal(localizer["MotionCaption.RightStick"], choices.GetInput("slot", true)!.InputSurface!.Elements.Single(item => item.Kind == "label").Label);
            Assert.All(Enum.GetValues<ButtonId>(), value => Assert.DoesNotContain("ButtonId.", UiTextResources.EnumValue(value)));
            Assert.All(Enum.GetValues<GamepadAction>(), value => Assert.DoesNotContain("GamepadAction.", UiTextResources.EnumValue(value)));
        }
        localizer.SetCulture("cs");
        Assert.Equal("Dolní (A / ×)", buttonText.Text);
        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>Checks live status text in each supported language.</summary>
    [Fact]
    public Task StatusAndControlTextFollowsEverySupportedLanguageAsync() => Run(services =>
    {
        var localizer = services.GetRequiredService<ILocalizationService>();
        var device = new DeviceRowViewModel(Pad("battery") with
        {
            BatteryPercentage = 42,
            BatteryState = DeviceBatteryState.Charging
        });
        var model = new ControllerVisualStateViewModel(_ => { });
        var connection = new TextBlock { DataContext = model };
        connection.Bind(TextBlock.TextProperty, new Binding(nameof(ControllerVisualStateViewModel.ConnectionLabel)));

        foreach (var language in localizer.SupportedLanguages)
        {
            localizer.SetCulture(language.Code);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Format(localizer["UiBatteryCharging"], 42), device.BatteryStatusText);
            Assert.Equal(localizer["UiIdle"], connection.Text);
            Assert.False(model.IsConnected);
            Assert.Equal(localizer["UiLeftTriggerAnalog"], ControlRuleMatcher.GetTitle("physical:LeftTrigger.Analog"));
            var rule = MappingRuleViewModel.CreateDefault(RuleKind.StickThreshold);
            Assert.Equal(ControlRuleMatcher.CreateEntry(rule.ToRule()).Value, rule.SummaryText);
        }

        return Task.CompletedTask;
    });

    [Fact]
    public Task BackgroundRequestsUseTheSelectedLanguageAfterTheirContextWasCaptured() => Run(async services =>
    {
        var localizer = services.GetRequiredService<ILocalizationService>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = Task.Run(async () =>
        {
            await release.Task;
            return localizer["ButtonId.South"];
        });
        localizer.SetCulture("cs");
        release.SetResult();
        Assert.Equal("Dolní (A / ×)", await request.WaitAsync(TimeSpan.FromSeconds(5)));
    });

    /// <summary>Checks every embedded prompt through the same converter as the mapping picker.</summary>
    [Fact]
    public Task ImportedButtonAndControllerImagesDecodeAndRemainCachedAsync() => Run(_ =>
    {
        using var converter = new InputAssetImageConverter();
        foreach (var button in Enum.GetValues<ButtonId>().Where(button => button != ButtonId.None))
        {
            var image = converter.Convert(button, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);

            Assert.IsType<Bitmap>(image);
            Assert.Same(image, converter.Convert(button, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var style in Enum.GetValues<ControllerVisualStyle>())
        {
            Assert.IsType<Bitmap>(converter.Convert(style, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture));
        }

        return Task.CompletedTask;
    });

    /// <summary>Checks that every imported vector has a usable embedded raster image.</summary>
    [Fact]
    public Task ImportedAssetManifestResolvesEveryEmbeddedImageAsync() => Run(_ =>
    {
        using var manifestStream = AssetLoader.Open(new Uri("avares://GameFlow/Assets/InputAssets/manifest.json"));
        using var manifest = System.Text.Json.JsonDocument.Parse(manifestStream);
        Assert.Equal(243, manifest.RootElement.GetArrayLength());

        foreach (var entry in manifest.RootElement.EnumerateArray())
        {
            using var stream = AssetLoader.Open(new Uri("avares://GameFlow/Assets/InputAssets/" + entry.GetProperty("resource").GetString()));
            using var image = new Bitmap(stream);

            Assert.True(image.PixelSize.Width > 0);
            Assert.True(image.PixelSize.Height > 0);
            Assert.Equal(64, entry.GetProperty("sha256").GetString()!.Length);
        }

        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(-4f, 9f)]
    public Task TouchMarkersAtPhysicalEdgesStayInsideTheTouchpad(float x, float y) => Run(async _ =>
    {
        var document = new ThemeDocument
        {
            Width = 400, Height = 300,
            Children = [new ShowHideNode
            {
                Input = FleeParser.Parse("touch_center:click"),
                Children = [new ImageNode { X = 100, Y = 50, Width = 80, Height = 40 }]
            }]
        };
        var surface = new ThemeSurface
        {
            ActiveTheme = new InstalledTheme("edge-" + x + "-" + y, "edge", "", document, ControllerVisualStyle.PlayStation5)
        };
        var window = new Window { Width = 400, Height = 300, Content = surface, Background = Avalonia.Media.Brushes.Black };
        window.Show();
        surface.UpdateState(new ControllerSnapshot().WithTouchContacts([new TouchContact(0, x, y)]));
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        var size = frame.PixelSize;
        Assert.Equal(new PixelSize(400, 300), size);
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(size.Width * size.Height * 4);
        try
        {
            frame.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), buffer, size.Width * size.Height * 4, size.Width * 4);
            var bytes = new byte[size.Width * size.Height * 4];
            System.Runtime.InteropServices.Marshal.Copy(buffer, bytes, 0, bytes.Length);
            var outside = 0;
            var inside = 0;
            for (var py = 0; py < size.Height; py++)
            {
                for (var px = 0; px < size.Width; px++)
                {
                    var offset = (py * size.Width + px) * 4;
                    if (bytes[offset] == 0 && bytes[offset + 1] == 0 && bytes[offset + 2] == 0) { continue; }
                    if (px >= 100 && px < 180 && py >= 50 && py < 90) { inside++; }
                    else { outside++; }
                }
            }
            Assert.Equal(0, outside);
            Assert.True(inside > 0, "The edge contact must still be visible within the pad.");
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            window.Close();
        }
    });

    private static InputDeviceInfo Pad(string id) => new(id, id, IsConnected: true, IsGamepad: true, Category: DeviceCategory.Gamepad);
    private static InputDeviceInfo Input(string id, DeviceCategory category) => new(id, id, IsConnected: true, Category: category);
}

public sealed class UiFixture : IAsyncLifetime
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(UiBootstrap));
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

public static class UiBootstrap
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<GameFlow.App.App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
