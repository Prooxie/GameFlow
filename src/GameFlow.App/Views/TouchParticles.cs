using GameFlow.Core.Models;

namespace GameFlow.App.Views;

/// <summary>
/// One smoke puff from a fingertip. Motion is stored as an origin plus a
/// velocity rather than a mutable position, so a particle's whole life is
/// a function of its age — nothing has to be stepped, frames can be
/// skipped without the animation drifting, and the maths is testable
/// without a renderer.
/// </summary>
/// <param name="FingerIndex">Hardware finger slot, which decides the colour.</param>
/// <param name="OriginX">Spawn position in normalized surface units.</param>
/// <param name="OriginY">Spawn position in normalized surface units.</param>
/// <param name="VelocityX">Drift per second, normalized units.</param>
/// <param name="VelocityY">Drift per second, normalized units.</param>
/// <param name="BornAt">Emitter clock reading at spawn.</param>
/// <param name="Lifetime">Seconds until it disappears.</param>
/// <param name="Scale">Per-particle size multiplier, so a burst is not uniform.</param>
public readonly record struct TouchParticle(
    int FingerIndex,
    double OriginX,
    double OriginY,
    double VelocityX,
    double VelocityY,
    double BornAt,
    double Lifetime,
    double Scale)
{
    /// <summary>0 at spawn, 1 at death. Clamped, so a stale particle cannot render inverted.</summary>
    public double Age(double nowSeconds) =>
        Lifetime <= 0 ? 1 : Math.Clamp((nowSeconds - BornAt) / Lifetime, 0d, 1d);

    public double XAt(double nowSeconds) => OriginX + (VelocityX * (nowSeconds - BornAt));

    public double YAt(double nowSeconds) => OriginY + (VelocityY * (nowSeconds - BornAt));
}

/// <summary>
/// Emits and ages the smoke puffs drawn at each touch contact.
///
/// <para>
/// Each puff drifts a little to the side, rises, grows and fades. The
/// renderer draws it as a soft radial gradient, not as a dot, so the
/// result reads as smoke. The OBS overlay page uses the same constants.
/// </para>
///
/// <para>
/// Kept away from Avalonia and driven
/// by an injected clock and seed so the emission rate, the population cap
/// and the ageing are testable: an effect that quietly emits ten times too
/// many particles looks fine in a screenshot and costs frames in motion,
/// which is the failure worth catching here rather than on a user's
/// machine.
/// </para>
/// </summary>
public sealed class TouchParticles
{
    /// <summary>
    /// Longer than the trail's: a puff needs time to grow and rise before it
    /// fades, or it reads as a dot.
    /// </summary>
    public const double LifetimeSeconds = 0.9;

    /// <summary>
    /// Gap between emissions per finger. Independent of tick rate on
    /// purpose — the dashboard now runs at 60 Hz and may run faster, and
    /// an emitter tied to the tick would emit twice the puffs on a
    /// faster machine and cost twice as much to draw.
    /// </summary>
    private const double EmitIntervalSeconds = 0.04;

    /// <summary>Puffs per emission.</summary>
    private const int ParticlesPerEmit = 1;

    /// <summary>
    /// Hard population cap. Bounds the per-frame draw cost no matter how
    /// many fingers arrive or how long they stay down; oldest go first.
    /// </summary>
    private const int MaximumParticles = 120;

    /// <summary>Sideways drift speed range, in normalized surface units per second.</summary>
    private const double MinimumSpeed = 0.01;
    private const double MaximumSpeed = 0.06;

    /// <summary>
    /// Upward speed added to each puff, in normalized surface units per second.
    /// More than the fastest drift at full pressure (0.06 × 1.4), so each puff rises.
    /// </summary>
    private const double RiseSpeed = 0.1;

    private readonly List<TouchParticle> particles = [];
    private readonly Dictionary<int, double> lastEmitByFinger = [];
    internal int TrackedFingerCount => lastEmitByFinger.Count;
    private readonly Random random;

    public TouchParticles(int? seed = null) =>
        random = seed is { } value ? new Random(value) : new Random();

    /// <summary>True while any particle is alive, so the surface keeps animating.</summary>
    public bool HasParticles => particles.Count > 0;

    public IReadOnlyList<TouchParticle> Particles => particles;

    /// <summary>Emits new puffs from each contact and retires faded ones.</summary>
    public void Emit(IReadOnlyList<TouchContact> contacts, double nowSeconds)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        foreach (var contact in contacts)
        {
            if (!float.IsFinite(contact.X) || !float.IsFinite(contact.Y) || !float.IsFinite(contact.Pressure)) { continue; }
            if (lastEmitByFinger.TryGetValue(contact.FingerIndex, out var last)
                && nowSeconds - last < EmitIntervalSeconds)
            {
                continue;
            }

            lastEmitByFinger[contact.FingerIndex] = nowSeconds;

            for (var i = 0; i < ParticlesPerEmit; i++)
            {
                particles.Add(Spawn(contact, nowSeconds));
            }
        }

        Prune(nowSeconds);

    }

    private TouchParticle Spawn(TouchContact contact, double nowSeconds)
    {
        // A small drift in a random direction, so the puffs spread and do
        // not stack into one column.
        var angle = random.NextDouble() * Math.PI * 2;
        var speed = MinimumSpeed + (random.NextDouble() * (MaximumSpeed - MinimumSpeed));

        // Pressing harder spreads them wider, so the effect responds to
        // the one analog value a touch surface reports.
        var push = 0.6 + (0.8 * Math.Clamp(contact.Pressure, 0f, 1f));

        return new TouchParticle(
            contact.FingerIndex,
            contact.X,
            contact.Y,
            Math.Cos(angle) * speed * push,
            (Math.Sin(angle) * speed * push) - RiseSpeed,
            nowSeconds,
            LifetimeSeconds * (0.65 + (random.NextDouble() * 0.7)),
            0.6 + (random.NextDouble() * 0.9));
    }

    /// <summary>
    /// Retires expired particles. Called by <see cref="Emit"/>, and
    /// separately on frames with no contact — a lifted finger's smoke
    /// still has to fade, and nothing would be ageing it.
    /// </summary>
    public void Prune(double nowSeconds)
    {
        _ = particles.RemoveAll(p =>
            p.Age(nowSeconds) >= 1
            // A clock that jumped backwards would otherwise strand
            // particles in the future and keep the effect alive forever.
            || p.BornAt > nowSeconds + LifetimeSeconds);

        List<int>? expired = null;
        foreach (var (finger, emittedAt) in lastEmitByFinger)
        {
            if (nowSeconds - emittedAt > LifetimeSeconds || emittedAt > nowSeconds + LifetimeSeconds)
            {
                (expired ??= []).Add(finger);
            }
        }
        if (expired is not null)
        {
            foreach (var finger in expired) { lastEmitByFinger.Remove(finger); }
        }

        if (particles.Count > MaximumParticles)
        {
            particles.RemoveRange(0, particles.Count - MaximumParticles);
        }
    }

    public void Clear()
    {
        particles.Clear();
        lastEmitByFinger.Clear();
    }
}
