using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;

namespace GameFlow.Infrastructure.Profiles;

/// <summary>
/// Brings a profile from whatever shape it was stored in up to the current
/// one. The ONLY migrator: the JSON repository runs it on every read, the
/// session runs it on start-up, switch, import and every save, and the slot
/// runtime runs it on every profile it layers onto a slot.
/// </summary>
/// <remarks>
/// <para>
/// One function on purpose. The v6 axis inversion used to be upgraded only
/// on start-up and on a switch, so an imported profile, a profile layered
/// onto a slot, or one loaded straight from the repository each saw a
/// different shape of the same file. Every step here keys on the DATA it
/// finds (is the legacy field there?), never on <see cref="ProfileDocument.Version"/>
/// alone, and is idempotent: running it on an already-current profile
/// returns that same instance, so calling it on a path that may already
/// have been migrated costs nothing and changes nothing.
/// </para>
/// </remarks>
public static class ProfileMigrator
{
    /// <summary>The shape every profile is written in. v7 moved axis inversion from the profile header into <see cref="AxisInvertRule"/>.</summary>
    public const int CurrentVersion = 7;

    /// <summary>Name given to the rule a legacy header inversion becomes.</summary>
    public const string LegacyAxisInvertRuleName = "Axis inversion";

    /// <summary>Polling rate shipped as the default in profile versions up to 4.</summary>
    private const int LegacyDefaultPollingRateHz = 250;

    /// <summary>
    /// Id of the rule a profile's legacy inversion is converted into. Derived
    /// from the PROFILE id, not fixed: profiles stack on a slot, and the
    /// composer treats equal rule ids across layers as the same rule — a
    /// shared id would let one profile's converted inversion silently replace
    /// another's instead of folding with it.
    /// </summary>
    public static string AxisInvertRuleId(string profileId) => $"axis-invert-{profileId}";

    /// <summary>
    /// Returns <paramref name="profile"/> in the current shape, or the same
    /// instance when there was nothing to change.
    /// </summary>
    public static ProfileDocument Migrate(ProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var inputProvider = NormalizeInputProvider(profile.InputProvider);
        var preferredInputDeviceId = profile.PreferredInputDeviceId?.Trim() ?? string.Empty;
        var version = Math.Max(profile.Version, CurrentVersion);

        // v5: the polling-rate default went from 250 Hz to 1000.
        //
        // Only rewritten when it still holds EXACTLY the old default on a
        // profile written before v5 — that value was shipped, not chosen,
        // and leaving it would mean the new default never reaching anyone
        // who already had a profile, which is everyone. Any other value is
        // a deliberate choice and is left alone, including a deliberate
        // 250 set after this migration has stamped the version.
        var pollingRateHz = profile.Version < 5 && profile.PollingRateHz == LegacyDefaultPollingRateHz
            ? 1000
            : profile.PollingRateHz;

        var rules = ConvertLegacyInvert(profile);

        return version == profile.Version &&
            pollingRateHz == profile.PollingRateHz &&
            rules is null && profile.Invert is null &&
            string.Equals(inputProvider, profile.InputProvider, StringComparison.Ordinal) &&
            string.Equals(preferredInputDeviceId, profile.PreferredInputDeviceId, StringComparison.Ordinal)
            ? profile
            : (profile with
            {
                Version = version,
                PollingRateHz = pollingRateHz,
                InputProvider = inputProvider,
                PreferredInputDeviceId = preferredInputDeviceId,
                Rules = rules ?? profile.Rules,
                Invert = null
            });
    }

    /// <summary>
    /// v7: the legacy header inversion becomes one <see cref="AxisInvertRule"/>,
    /// appended to the rules, with every axis copied verbatim — explicit
    /// <c>false</c> and null included, because in a slot's profile stack an
    /// explicit false is what switches a lower profile's inversion back off.
    /// Returns null when the rule list does not change; the caller clears the
    /// legacy field either way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Converted whenever the header holds an opinion about any axis, on OR
    /// explicitly off. Converting only headers that invert something would
    /// drop a top profile's "right Y off" and quietly bring back the bottom
    /// profile's camera inversion in that slot. A header with no axis opinion
    /// at all (empty, or only the show-on-layout flag) does nothing and is
    /// simply cleared. A profile that already HAS its converted rule (an
    /// older build wrote the header again after converting) gets the header
    /// folded into that rule with <see cref="AxisInvert.Overlay"/>.
    /// </para>
    /// <para>
    /// Base layer, enabled, appended last. Position does not change what it
    /// does — inversion runs in its own pass after every rule — and Base is
    /// what the header always was: active whatever shift layer is engaged.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<MappingRule>? ConvertLegacyInvert(ProfileDocument profile)
    {
        var legacy = profile.Invert;
        if (legacy is null)
        {
            return null;
        }

        var rules = profile.Rules;
        var ruleId = AxisInvertRuleId(profile.Id);

        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i] is AxisInvertRule existing && string.Equals(existing.Id, ruleId, StringComparison.Ordinal))
            {
                var merged = existing.Axes.Overlay(legacy);
                if (merged == existing.Axes)
                {
                    return null;
                }

                var updated = rules.ToArray();
                updated[i] = existing with { Axes = merged };
                return updated;
            }
        }

        var hasAxisOpinion = legacy.LeftX is not null || legacy.LeftY is not null
            || legacy.RightX is not null || legacy.RightY is not null
            || legacy.LeftTrigger is not null || legacy.RightTrigger is not null;
        if (!hasAxisOpinion)
        {
            return null;
        }

        return
        [
            .. rules,
            new AxisInvertRule
            {
                Id = ruleId,
                Name = LegacyAxisInvertRuleName,
                Axes = legacy
            }
        ];
    }

    private static string NormalizeInputProvider(string? inputProvider)
    {
        return string.IsNullOrWhiteSpace(inputProvider)
            ? "sdl"
            : inputProvider.Trim().ToLowerInvariant() switch
            {
                "sdlgamepad" => "sdl",
                "sdl3" => "sdl",
                "sdl-unified" => "sdl",
                "sdl-unified-gamepad" => "sdl",
                // Retired providers — profiles that referenced them keep
                // working by migrating to the SDL3 unified source.
                "xinput" => "sdl",
                "openxinput" => "sdl",
                "x360ce" => "sdl",
                "ps3" or "dualshock3" => "sdl",
                "gameinput" => "sdl",
                "windows-midi" or "winmidi" or "midi" => "sdl",
                _ => inputProvider.Trim().ToLowerInvariant()
            };
    }
}
