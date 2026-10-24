using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Input;

public sealed class KeyboardMouseMapTests
{
    [Fact]
    public void DeviceMapsPersistIndependentlyAndSharedDefaultsRemainCompatible()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gameflow-device-maps-{Guid.NewGuid():N}.json");
        var store = new KeyboardMouseMapStore(NullLogger<KeyboardMouseMapStore>.Instance, path);
        var left = KeyboardMouseMap.Default with { MouseMotion = MouseMotionTarget.LeftStick, MouseSensitivity = 2 };
        var right = KeyboardMouseMap.Default with { MouseMotion = MouseMotionTarget.RightStick, InvertMouseY = true };
        store.SaveFor(left, "mouse-1");
        store.SaveFor(right, "mouse-2");
        var loaded = new KeyboardMouseMapStore(NullLogger<KeyboardMouseMapStore>.Instance, path);
        Assert.Equal(MouseMotionTarget.LeftStick, loaded.CurrentFor("mouse-1").MouseMotion);
        Assert.True(loaded.CurrentFor("mouse-2").InvertMouseY);
        Assert.False(loaded.CurrentFor("mouse-1").InvertMouseY);
        Assert.Equal(KeyboardMouseMap.Default.MouseMotion, loaded.Current.MouseMotion);
        Assert.Same(loaded.Compiled, loaded.CompiledFor("another mouse"));

        var first = MouseGamepadSynthesizer.Synthesize("one", default, new StickVector(.5f, 0), MouseButtonFlags.None, loaded.CompiledFor("mouse-1"));
        var second = MouseGamepadSynthesizer.Synthesize("two", default, new StickVector(0, .75f), MouseButtonFlags.None, loaded.CompiledFor("mouse-2"));
        var merged = ControllerSnapshotMerger.Merge("two mice", [first, second]);
        Assert.Equal(.5f, merged.LeftStick.X);
        Assert.Equal(.75f, Math.Abs(merged.RightStick.Y));
        loaded.ResetFor("mouse-1");
        Assert.Same(loaded.Compiled, loaded.CompiledFor("mouse-1"));
        Assert.True(loaded.CurrentFor("mouse-2").InvertMouseY);
        File.Delete(path);
    }

    [Fact]
    public void LegacySharedMapIsTheFallbackForEveryDevice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gameflow-legacy-map-{Guid.NewGuid():N}.json");
        var legacy = KeyboardMouseMap.Default with { MouseMotion = MouseMotionTarget.LeftStick, MouseSensitivity = 3 };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(legacy, GameFlow.Infrastructure.Profiles.ProfileJsonOptions.Default));
        var store = new KeyboardMouseMapStore(NullLogger<KeyboardMouseMapStore>.Instance, path);
        Assert.Equal(3, store.CurrentFor("mouse-1").MouseSensitivity);
        Assert.Equal(MouseMotionTarget.LeftStick, store.CurrentFor("mouse-2").MouseMotion);
        store.SaveFor(KeyboardMouseMap.Default, "mouse-2");
        var reloaded = new KeyboardMouseMapStore(NullLogger<KeyboardMouseMapStore>.Instance, path);
        Assert.Equal(3, reloaded.CurrentFor("mouse-1").MouseSensitivity);
        Assert.Equal(1, reloaded.CurrentFor("mouse-2").MouseSensitivity);
        File.Delete(path);
    }

    private const int W = 0x57;
    private const int Space = 0x20;
    private const int LeftShift = 0xA0;
    private const int F = 0x46;

    [Fact]
    public void DefaultMap_KeepsTheShippedKeyboardLayout()
    {
        var snapshot = KeyboardGamepadSynthesizer.Synthesize("kb", new HashSet<int> { W, Space });

        Assert.Equal(1f, snapshot.LeftStick.Y);
        Assert.True(snapshot.Buttons[ButtonId.South]);
    }

    [Fact]
    public void Synthesize_CarriesEveryHeldKeyForTheKeyboardPicture()
    {
        var pressed = new HashSet<int> { F, 0x70 }; // F and F1, neither bound

        var snapshot = KeyboardGamepadSynthesizer.Synthesize("kb", pressed);

        Assert.True(snapshot.PressedKeys.SetEquals(pressed));
    }

    [Fact]
    public void CustomBinding_DrivesTheBoundControl()
    {
        var map = new KeyboardMouseMap
        {
            Bindings = [new KeyboardMouseBinding(GamepadAction.North, Key: F)],
        }.Compile();

        var snapshot = KeyboardGamepadSynthesizer.Synthesize("kb", new HashSet<int> { F }, map);

        Assert.True(snapshot.Buttons[ButtonId.North]);
    }

    [Fact]
    public void OneKey_CanDriveSeveralControls()
    {
        var map = new KeyboardMouseMap
        {
            Bindings =
            [
                new KeyboardMouseBinding(GamepadAction.South, Key: F),
                new KeyboardMouseBinding(GamepadAction.RightShoulder, Key: F),
            ],
        }.Compile();

        var snapshot = KeyboardGamepadSynthesizer.Synthesize("kb", new HashSet<int> { F }, map);

        Assert.True(snapshot.Buttons[ButtonId.South]);
        Assert.True(snapshot.Buttons[ButtonId.RightShoulder]);
    }

    [Fact]
    public void WalkKey_HalvesTheLeftStick()
    {
        var map = new KeyboardMouseMap
        {
            Bindings =
            [
                new KeyboardMouseBinding(GamepadAction.LeftStickUp, Key: W),
                new KeyboardMouseBinding(GamepadAction.LeftStickWalk, Key: LeftShift),
            ],
        }.Compile();

        var snapshot = KeyboardGamepadSynthesizer.Synthesize("kb", new HashSet<int> { W, LeftShift }, map);

        Assert.Equal(0.5f, snapshot.LeftStick.Y, 3);
    }

    [Fact]
    public void DigitalTrigger_AlsoClosesTheTriggerButton()
    {
        var snapshot = KeyboardGamepadSynthesizer.Synthesize("kb", new HashSet<int> { 0x33 }); // 3 = RT

        Assert.Equal(1f, snapshot.RightTrigger);
        Assert.True(snapshot.Buttons[ButtonId.RightTriggerButton]);
    }

    [Fact]
    public void Normalize_DropsInvalidAndDuplicateBindingsAndClampsNumbers()
    {
        var map = new KeyboardMouseMap
        {
            Bindings =
            [
                new KeyboardMouseBinding(GamepadAction.South, Key: Space),
                new KeyboardMouseBinding(GamepadAction.South, Key: Space),
                new KeyboardMouseBinding(GamepadAction.None, Key: Space),
                new KeyboardMouseBinding(GamepadAction.East),
                new KeyboardMouseBinding(GamepadAction.West, Key: 999),
            ],
            MouseSensitivity = double.NaN,
            MouseAntiDeadzone = 4,
        }.Normalize();

        Assert.Single(map.Bindings);
        Assert.Equal(1.0, map.MouseSensitivity);
        Assert.Equal(0.5, map.MouseAntiDeadzone);
    }

    [Fact]
    public void Mouse_ButtonsFollowTheMapAndAreReportedForThePicture()
    {
        var frame = new MouseFrame(0, 0, Left: true, Right: false, Middle: false, Button4: true, Button5: false);

        var snapshot = MouseGamepadSynthesizer.Synthesize(
            "mouse", frame, StickVector.Zero, MouseButtonFlags.None, CompiledKeyboardMouseMap.Default);

        Assert.Equal(1f, snapshot.RightTrigger);          // left click = RT by default
        Assert.True(snapshot.Buttons[ButtonId.LeftShoulder]); // side button 4 = LB
        Assert.Equal(MouseButtonFlags.Left | MouseButtonFlags.Back, snapshot.MouseButtons);
    }

    [Fact]
    public void Mouse_WheelNotchCanBeBound()
    {
        var map = new KeyboardMouseMap
        {
            Bindings = [new KeyboardMouseBinding(GamepadAction.DpadUp, Mouse: MouseInput.WheelUp)],
        }.Compile();

        var snapshot = MouseGamepadSynthesizer.Synthesize(
            "mouse", default, StickVector.Zero, MouseButtonFlags.WheelUp, map);

        Assert.True(snapshot.Buttons[ButtonId.DpadUp]);
    }

    [Fact]
    public void Mouse_MovementGoesToTheChosenStick()
    {
        var map = (KeyboardMouseMap.Default with { MouseMotion = MouseMotionTarget.LeftStick }).Compile();
        var motion = new StickVector(0.4f, -0.2f);

        var snapshot = MouseGamepadSynthesizer.Synthesize("mouse", default, motion, MouseButtonFlags.None, map);

        Assert.Equal(motion, snapshot.LeftStick);
        Assert.Equal(StickVector.Zero, snapshot.RightStick);
    }

    [Fact]
    public void MotionFilter_DependsOnSpeedNotOnHowOftenItIsRead()
    {
        // The same hand speed (3000 counts/s) read at 1000 Hz and at 125 Hz
        // must settle on the same deflection: the old per-read conversion
        // barely moved the stick at 1000 Hz.
        var fast = new MouseMotionFilter();
        var slow = new MouseMotionFilter();
        var map = KeyboardMouseMap.Default with { MouseAntiDeadzone = 0 };
        StickVector fastResult = default, slowResult = default;

        for (var i = 0; i < 400; i++)
        {
            fastResult = fast.Update(3, 0, 0.001, map);
        }
        for (var i = 0; i < 50; i++)
        {
            slowResult = slow.Update(24, 0, 0.008, map);
        }

        Assert.Equal(0.5f, fastResult.X, 2);
        Assert.Equal(0.5f, slowResult.X, 2);
    }

    [Fact]
    public void MotionFilter_RestingMouseCentresTheStick()
    {
        var filter = new MouseMotionFilter();
        var map = KeyboardMouseMap.Default;
        filter.Update(100, 0, 0.01, map);

        StickVector result = default;
        for (var i = 0; i < 100; i++)
        {
            result = filter.Update(0, 0, 0.01, map);
        }

        Assert.Equal(StickVector.Zero, result);
    }

    [Fact]
    public void MotionFilter_AntiDeadzoneLiftsSmallMovement()
    {
        var filter = new MouseMotionFilter();
        var map = KeyboardMouseMap.Default with { MouseAntiDeadzone = 0.2 };

        StickVector result = default;
        for (var i = 0; i < 200; i++)
        {
            result = filter.Update(0, -1, 0.01, map); // slow upward movement
        }

        Assert.True(result.Y >= 0.2f, $"expected at least the anti-deadzone, got {result.Y}");
    }

    [Fact]
    public void Store_PersistsAndReloadsTheMap()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gameflow-kbm-{Guid.NewGuid():N}.json");
        try
        {
            var store = new KeyboardMouseMapStore(NullLogger<KeyboardMouseMapStore>.Instance, path);
            store.Save(new KeyboardMouseMap
            {
                Bindings = [new KeyboardMouseBinding(GamepadAction.Start, Key: F)],
                InvertMouseY = true,
            });

            var reloaded = new KeyboardMouseMapStore(NullLogger<KeyboardMouseMapStore>.Instance, path);

            Assert.Single(reloaded.Current.Bindings);
            Assert.True(reloaded.Current.InvertMouseY);
            Assert.Contains(GamepadAction.Start, reloaded.Compiled.ActionsFor(F));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Store_DamagedFileFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gameflow-kbm-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");

            var store = new KeyboardMouseMapStore(NullLogger<KeyboardMouseMapStore>.Instance, path);

            Assert.Equal(KeyboardMouseMap.DefaultBindings.Count, store.Current.Bindings.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Merger_KeepsKeysClicksMotionAndTouchFromTheDevicesThatHaveThem()
    {
        var keyboard = new ControllerSnapshot { PressedKeys = new HashSet<int> { W } };
        var mouse = new ControllerSnapshot
        {
            MouseButtons = MouseButtonFlags.Right,
            MouseMotion = new StickVector(0.3f, 0f),
        };
        var pad = new ControllerSnapshot { VendorId = 0x054C, ProductId = 0x0CE6 }
            .WithMotion(1, 2, 3, 0, 9.8f, 0)
            .WithTouchContacts([new TouchContact(0, 0.5f, 0.5f, 1f)]);

        var merged = ControllerSnapshotMerger.Merge("Merged", [keyboard, mouse, pad]);

        Assert.Contains(W, merged.PressedKeys);
        Assert.Equal(MouseButtonFlags.Right, merged.MouseButtons);
        Assert.Equal(0.3f, merged.MouseMotion.X);
        Assert.True(merged.HasGyro);
        Assert.True(merged.TouchDown);
        Assert.Equal(0x054C, merged.VendorId);
    }
}
