using Autofire.Core.Models;

namespace Autofire.Core.Pipeline;

/// <summary>
/// Schedules autofire pulses for a stick axis.
///
/// Issue #11 fixes:
///   Delay  — removed the Settle guard that blocked the first pulse; the scheduler
///            now starts the hold phase immediately on the first active frame.
///   Jitter — added a short hysteresis window (ZeroHysteresisFrames) before actually
///            resetting the pulse cycle. A 1-frame spike through near-zero (e.g. a
///            1-unit direction change or controller noise) no longer interrupts ongoing
///            autofire.
/// </summary>
public sealed class StickPulseScheduler(PulseTimingOptions timing)
{
    // Number of consecutive zero-desire frames required before the scheduler resets.
    // At 250 Hz polling this is ~12 ms – enough to absorb a single noisy frame.
    private const int ZeroHysteresisFrames = 3;

    private readonly PulseTimingOptions timing = timing;
    private StickVector desiredValue = StickVector.Zero;
    private bool isHoldingPhase;
    private DateTimeOffset nextPhaseAt;
    private int zeroFrameCount;

    public void SetDesired(StickVector desired, DateTimeOffset now)
    {
        var normalizedDesired = desired.Clamp();
        var desiredActive = !normalizedDesired.IsNearZero();

        if (!desiredActive)
        {
            zeroFrameCount++;

            // Issue #11: Wait for ZeroHysteresisFrames of sustained zero input before
            // actually stopping the scheduler. A single frame of near-zero (caused by
            // a 1-unit direction jitter or diagonal transition) is absorbed silently.
            if (zeroFrameCount < ZeroHysteresisFrames)
            {
                return; // keep the current desire / phase alive for a bit longer
            }

            // Sustained zero: properly reset.
            desiredValue = StickVector.Zero;
            isHoldingPhase = false;
            nextPhaseAt = now;
            return;
        }

        // Active desire: reset the hysteresis counter.
        zeroFrameCount = 0;

        if (!desiredValue.IsNearZero())
        {
            // Already active: just update the direction without disturbing the phase.
            desiredValue = normalizedDesired;
            return;
        }

        // Transitioning from idle to active: start the hold phase immediately.
        // Issue #11: The old code entered a Settle/PauseAfterRelease blocking period
        // here, which users experienced as a noticeable startup delay. The timing
        // options still exist for advanced users but default to 0.
        desiredValue = normalizedDesired;
        isHoldingPhase = false;
        nextPhaseAt = now;

        if (timing.Settle > TimeSpan.Zero)
        {
            // Optional settle delay (not used in default profiles).
            // Implemented as an immediate centre-frame followed by a pause.
            nextPhaseAt = now + timing.Settle;
        }
    }

    public void Clear()
    {
        desiredValue = StickVector.Zero;
        isHoldingPhase = false;
        nextPhaseAt = default;
        zeroFrameCount = 0;
    }

    public StickPulseResult Tick(DateTimeOffset now)
    {
        if (desiredValue.IsNearZero())
        {
            isHoldingPhase = false;
            return new StickPulseResult(StickVector.Zero, false);
        }

        if (now < nextPhaseAt)
        {
            // Still waiting for the current phase to expire.
            return new StickPulseResult(isHoldingPhase ? desiredValue : StickVector.Zero, false);
        }

        if (!isHoldingPhase)
        {
            isHoldingPhase = true;
            nextPhaseAt = now + timing.Hold;
            return new StickPulseResult(desiredValue, false);
        }

        isHoldingPhase = false;
        nextPhaseAt = now + timing.Release;
        return new StickPulseResult(StickVector.Zero, false);
    }
}
