using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// Combines the snapshots of a slot's multiple input devices into one
/// controller snapshot. Each stick takes whichever device's vector has the
/// larger magnitude (so a keyboard driving the left stick and a mouse
/// driving the right stick merge without fighting), triggers take the max,
/// and buttons are OR'd (any device pressing it counts). This is what lets
/// e.g. WASD + mouse-aim act as a single virtual pad.
///
/// <para>
/// Fields only one kind of device has — motion, touch, the pad's identity
/// and battery, the keyboard's keys and the mouse's buttons — come from the
/// device that has them. They used to be dropped, so adding a keyboard to a
/// DualSense slot silently switched off its gyro and touchpad, and a
/// keyboard + mouse slot drew a keyboard that never lit up.
/// </para>
/// </summary>
public static class ControllerSnapshotMerger
{
    public static ControllerSnapshot Merge(string deviceName, IReadOnlyList<ControllerSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return ControllerSnapshot.Empty(deviceName);
        }
        if (snapshots.Count == 1)
        {
            return snapshots[0];
        }

        StickVector left = new(0, 0);
        StickVector right = new(0, 0);
        StickVector mouseMotion = new(0, 0);
        float leftTrigger = 0, rightTrigger = 0;
        var buttons = ButtonMask.Empty;
        var mouseButtons = MouseButtonFlags.None;
        IReadOnlySet<int>? pressedKeys = null;
        HashSet<int>? keyUnion = null;
        ControllerSnapshot? motionSource = null;
        ControllerSnapshot? touchSource = null;
        ControllerSnapshot? identitySource = null;

        foreach (var snapshot in snapshots)
        {
            if (MagnitudeSquared(snapshot.LeftStick) > MagnitudeSquared(left))
            {
                left = snapshot.LeftStick;
            }
            if (MagnitudeSquared(snapshot.RightStick) > MagnitudeSquared(right))
            {
                right = snapshot.RightStick;
            }
            if (snapshot.LeftTrigger > leftTrigger) leftTrigger = snapshot.LeftTrigger;
            if (snapshot.RightTrigger > rightTrigger) rightTrigger = snapshot.RightTrigger;

            // A button is down on the merged pad if it is down on any
            // contributing one — which is exactly a bitwise OR now, rather
            // than a walk over one dictionary per device per frame.
            buttons |= snapshot.Buttons;

            mouseButtons |= snapshot.MouseButtons;
            if (MagnitudeSquared(snapshot.MouseMotion) > MagnitudeSquared(mouseMotion))
            {
                mouseMotion = snapshot.MouseMotion;
            }

            if (snapshot.PressedKeys.Count > 0)
            {
                if (pressedKeys is null)
                {
                    pressedKeys = snapshot.PressedKeys;
                }
                else
                {
                    keyUnion ??= [.. pressedKeys];
                    keyUnion.UnionWith(snapshot.PressedKeys);
                }
            }

            if (motionSource is null && snapshot.HasGyro)
            {
                motionSource = snapshot;
            }
            if (touchSource is null && (snapshot.TouchDown || snapshot.TouchContactCount > 0))
            {
                touchSource = snapshot;
            }
            if (identitySource is null && (snapshot.VendorId != 0 || snapshot.ProductId != 0))
            {
                identitySource = snapshot;
            }
        }

        var merged = new ControllerSnapshot
        {
            DeviceName = deviceName,
            VendorId = identitySource?.VendorId ?? 0,
            ProductId = identitySource?.ProductId ?? 0,
            BatteryPercent = identitySource?.BatteryPercent,
            BatteryCharging = identitySource?.BatteryCharging ?? false,
            LeftStick = left,
            RightStick = right,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
            Buttons = buttons,
            MouseButtons = mouseButtons,
            MouseMotion = mouseMotion,
            PressedKeys = (IReadOnlySet<int>?)keyUnion ?? pressedKeys ?? EmptyKeys,
        };

        if (motionSource is not null)
        {
            merged = merged.WithMotion(
                motionSource.GyroPitch, motionSource.GyroYaw, motionSource.GyroRoll,
                motionSource.AccelX, motionSource.AccelY, motionSource.AccelZ);
        }

        if (touchSource is not null)
        {
            merged = merged with
            {
                TouchContacts = touchSource.TouchContacts,
                TouchContactCount = touchSource.TouchContactCount,
                TouchDown = touchSource.TouchDown,
                TouchX = touchSource.TouchX,
                TouchY = touchSource.TouchY,
            };
        }

        return merged;
    }

    private static readonly IReadOnlySet<int> EmptyKeys = new HashSet<int>();

    private static float MagnitudeSquared(StickVector v) => (v.X * v.X) + (v.Y * v.Y);
}
