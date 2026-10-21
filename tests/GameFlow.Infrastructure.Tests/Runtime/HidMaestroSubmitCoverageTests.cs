using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>
/// A HIDMaestro pad builds one <c>HMGamepadState</c> per frame, and any
/// field it leaves unwritten still goes out on the wire — as zero, which the
/// consuming game believes. That is how every virtual pad once announced
/// itself at ~10% charge: the battery members were simply never set, and an
/// unwritten battery is not the same as no battery.
///
/// <para>
/// These tests cover the conversions for the fields that were in that same
/// state afterwards — motion and the touch surface — so a dropped field
/// fails here rather than in a game. The button table moved into the
/// adapter with the pad that uses it, and <c>HidMaestroStateWriterTests</c>
/// covers it there.
/// </para>
/// </summary>
public sealed class HidMaestroSubmitCoverageTests
{
    // ── Motion ───────────────────────────────────────────────────────────

    [Fact]
    public void A_pad_with_no_gyro_reports_no_motion_rather_than_zeroes()
    {
        // "Perfectly still" and "no sensor here" are different claims. This
        // is the distinction HasGyro exists for, and the one the battery
        // fields got wrong.
        var motion = HidMaestroSubmission.ConvertMotion(ControllerSnapshot.Empty("keyboard"));

        Assert.False(motion.Present);
    }

    [Fact]
    public void Gyro_is_converted_from_radians_to_degrees_per_second()
    {
        // The snapshot carries SDL's rad/s; the SDK's calibrated members are
        // deg/s in that same SDL sensor frame, so this is a pure unit change
        // with no axis shuffling.
        var snapshot = ControllerSnapshot.Empty("pad")
            .WithMotion(gyroPitch: MathF.PI, gyroYaw: -MathF.PI / 2f, gyroRoll: 0f,
                        accelX: 0f, accelY: 0f, accelZ: 0f);

        var motion = HidMaestroSubmission.ConvertMotion(snapshot);

        Assert.True(motion.Present);
        Assert.Equal(180f, motion.GyroDps.X, precision: 3);
        Assert.Equal(-90f, motion.GyroDps.Y, precision: 3);
        Assert.Equal(0f, motion.GyroDps.Z, precision: 3);
    }

    [Fact]
    public void Acceleration_is_converted_from_metres_per_second_squared_to_g()
    {
        // A pad lying flat reads about 9.81 m/s² on one axis, which is 1 g.
        var snapshot = ControllerSnapshot.Empty("pad")
            .WithMotion(0f, 0f, 0f, accelX: 0f, accelY: 9.80665f, accelZ: -19.6133f);

        var motion = HidMaestroSubmission.ConvertMotion(snapshot);

        Assert.Equal(0f, motion.AccelG.X, precision: 4);
        Assert.Equal(1f, motion.AccelG.Y, precision: 4);
        Assert.Equal(-2f, motion.AccelG.Z, precision: 4);
    }

    // ── Touch surface ────────────────────────────────────────────────────

    [Fact]
    public void No_touch_submits_no_fingers()
    {
        var touch = HidMaestroSubmission.ConvertTouch(ControllerSnapshot.Empty("pad"));

        Assert.False(touch.Finger0Down);
        Assert.False(touch.Finger1Down);
    }

    [Fact]
    public void Normalized_contacts_scale_to_the_Sony_surface_range()
    {
        var snapshot = ControllerSnapshot.Empty("pad").WithTouchContacts(
        [
            new TouchContact(FingerIndex: 0, X: 0f, Y: 0f, Pressure: 1f),
            new TouchContact(FingerIndex: 1, X: 1f, Y: 1f, Pressure: 1f),
        ]);

        var touch = HidMaestroSubmission.ConvertTouch(snapshot);

        Assert.True(touch.Finger0Down);
        Assert.Equal(0, touch.Finger0X);
        Assert.Equal(0, touch.Finger0Y);

        Assert.True(touch.Finger1Down);
        Assert.Equal(1919, touch.Finger1X);
        Assert.Equal(1079, touch.Finger1Y);
    }

    [Fact]
    public void A_centre_touch_lands_in_the_middle_of_the_surface()
    {
        var snapshot = ControllerSnapshot.Empty("pad").WithTouchContacts(
            [new TouchContact(FingerIndex: 0, X: 0.5f, Y: 0.5f, Pressure: 1f)]);

        var touch = HidMaestroSubmission.ConvertTouch(snapshot);

        Assert.Equal(960, touch.Finger0X);
        Assert.Equal(540, touch.Finger0Y);
        Assert.False(touch.Finger1Down);
    }

    [Fact]
    public void A_source_that_reports_only_a_primary_point_still_submits_it()
    {
        // Sources that track a single contact (the web controller, the
        // on-screen overlay) fill TouchX/TouchY without a contact list.
        // Dropping them would make the touch surface work on a DualSense and
        // silently not on a phone.
        var snapshot = ControllerSnapshot.Empty("phone").WithTouch(down: true, x: 0.25f, y: 0.75f);

        var touch = HidMaestroSubmission.ConvertTouch(snapshot);

        Assert.True(touch.Finger0Down);
        Assert.Equal(480, touch.Finger0X);
        Assert.Equal(809, touch.Finger0Y);
        Assert.False(touch.Finger1Down);
    }

    [Fact]
    public void Contact_ids_stay_within_the_protocol_seven_bit_field()
    {
        // Bit 7 of the id byte is the firmware "lifted" flag; letting a
        // finger index run into it would report a live contact as lifted.
        var snapshot = ControllerSnapshot.Empty("pad").WithTouchContacts(
            [new TouchContact(FingerIndex: 200, X: 0.5f, Y: 0.5f, Pressure: 1f)]);

        var touch = HidMaestroSubmission.ConvertTouch(snapshot);

        Assert.True(touch.Finger0Id < 0x80);
    }

    [Fact]
    public void A_non_finite_coordinate_cannot_reach_the_wire()
    {
        var snapshot = ControllerSnapshot.Empty("pad") with
        {
            TouchDown = true,
            TouchX = float.NaN,
            TouchY = float.PositiveInfinity,
        };

        var touch = HidMaestroSubmission.ConvertTouch(snapshot);

        Assert.Equal(0, touch.Finger0X);
        Assert.Equal(0, touch.Finger0Y);
    }
}
