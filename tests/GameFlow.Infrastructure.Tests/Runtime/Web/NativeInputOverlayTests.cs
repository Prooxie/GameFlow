using System.Text.Json;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Input;
using GameFlow.Infrastructure.Runtime.Slots;
using GameFlow.Infrastructure.Runtime.Templates;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

public sealed class NativeInputOverlayTests
{
    private static OverlayProgram Program(ControllerVisualStyle style) => new()
    {
        ThemeId = "native", ThemeName = "native", Width = 1096, Height = 300,
        Nodes = [], ImagePaths = [], ValueSlots = [], TrailPads = [], MissingImages = [],
        InputSurface = InputSurfaceLayout.Create(style, CompiledKeyboardMouseMap.Default, CompiledKeyboardMouseMap.Default)
    };

    [Fact]
    public void SharedLayoutContainsEveryKeyAndSeparateWheelDirectionsWithinItsBounds()
    {
        var layout = Program(ControllerVisualStyle.KeyboardMouse).InputSurface!;
        Assert.Equal(104, layout.Elements.Count(item => item.Key > 0));
        Assert.Contains(layout.Elements, item => item.Key == KeyboardVirtualKeys.NumpadEnter);
        Assert.Single(layout.Elements, item => item.Mouse == MouseInput.WheelUp);
        Assert.Single(layout.Elements, item => item.Mouse == MouseInput.WheelDown);
        Assert.All(layout.Elements, item =>
        {
            Assert.InRange(item.X, 0, layout.Width - item.Width);
            Assert.InRange(item.Y, 0, layout.Height - item.Height);
        });
    }

    [Fact]
    public void FrameKeepsNativeKeysWheelAndFiniteMovementInsteadOfInferringThemFromGamepadButtons()
    {
        var program = Program(ControllerVisualStyle.KeyboardMouse);
        var snapshot = ControllerSnapshot.Empty() with
        {
            PressedKeys = new HashSet<int> { 0x57, KeyboardVirtualKeys.NumpadEnter, -1 },
            MouseButtons = MouseButtonFlags.WheelUp | MouseButtonFlags.Back,
            MouseMotion = new StickVector(float.NaN, -9),
            Buttons = new ButtonMask { [ButtonId.South] = true }
        };
        using var json = JsonDocument.Parse(OverlayProtocol.BuildFrame(program, new ControllerStateSymbols(), snapshot, null));
        var root = json.RootElement;
        Assert.Equal(new[] { 0x57, KeyboardVirtualKeys.NumpadEnter }, root.GetProperty("keys").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal((int)snapshot.MouseButtons, root.GetProperty("mouse").GetInt32());
        Assert.Equal(0, root.GetProperty("motion")[0].GetSingle());
        Assert.Equal(-1, root.GetProperty("motion")[1].GetSingle());
        Assert.Equal((int)ButtonId.South, root.GetProperty("buttons")[0].GetInt32());
    }

    [Fact]
    public void NativeChoiceFollowsPinnedDeviceAndSlotWithoutLeakingToTheOtherSide()
    {
        var file = Path.Combine(Path.GetTempPath(), "gameflow-native-overlay-" + Guid.NewGuid() + ".json");
        try
        {
            var slots = new SlotRegistry(NullLogger<SlotRegistry>.Instance, file);
            var slot = slots.CreateSlot(VirtualControllerKind.Xbox360)!;
            var choices = new OverlayThemeChoices();
            var feed = new OverlayFeed(new ThemeRegistry(), slots, new SlotSnapshotStore(), choices);
            var keyboard = Program(ControllerVisualStyle.Keyboard);
            var mouse = Program(ControllerVisualStyle.Mouse);
            choices.SetInput(slot.Id, true, keyboard);
            choices.SetInput(OverlayFeed.PinnedPanelId("mouse-2"), true, mouse);
            Assert.Same(keyboard, feed.ResolveProgram(new(null, slot.Id, true)));
            Assert.Same(mouse, feed.ResolveProgram(new(null, null, false, "mouse-2")));
            Assert.Null(feed.ResolveProgram(new(null, slot.Id, false)));
            choices.Remove(slot.Id);
            Assert.Null(feed.ResolveProgram(new(null, slot.Id, true)));
            Assert.Same(mouse, feed.ResolveProgram(new(null, null, false, "mouse-2")));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TextAndHoldTimesAreExplicitLinkOptions()
    {
        var plain = OverlayUrl.Build("http://localhost:8080", "slot", null, true);
        Assert.DoesNotContain("text=", plain);
        Assert.DoesNotContain("holds=", plain);
        var annotated = OverlayUrl.Build("http://localhost:8080", "slot", null, true, inputText: true, holdTimes: true);
        Assert.Contains("text=1", annotated);
        Assert.Contains("holds=1", annotated);
    }
}
