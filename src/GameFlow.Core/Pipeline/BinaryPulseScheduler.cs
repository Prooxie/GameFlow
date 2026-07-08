using GameFlow.Core.Models;

namespace GameFlow.Core.Pipeline;

public sealed class BinaryPulseScheduler(PulseTimingOptions timing)
{
    private readonly PulseTimingOptions timing = timing;
    private bool desiredState;
    private bool isHoldingPhase;
    private DateTimeOffset nextPhaseAt;

    public void SetDesired(bool desired, DateTimeOffset now)
    {
        if (desired == desiredState)
        {
            return;
        }

        desiredState = desired;
        isHoldingPhase = false;
        nextPhaseAt = now;
    }

    public void Clear()
    {
        desiredState = false;
        isHoldingPhase = false;
        nextPhaseAt = default;
    }

    public BinaryPulseResult Tick(DateTimeOffset now)
    {
        if (!desiredState)
        {
            isHoldingPhase = false;
            return new BinaryPulseResult(false);
        }

        if (now < nextPhaseAt)
        {
            return new BinaryPulseResult(isHoldingPhase);
        }

        if (!isHoldingPhase)
        {
            isHoldingPhase = true;
            nextPhaseAt = now + timing.Hold;
            return new BinaryPulseResult(true);
        }

        isHoldingPhase = false;
        nextPhaseAt = now + timing.Release;
        return new BinaryPulseResult(false);
    }
}
