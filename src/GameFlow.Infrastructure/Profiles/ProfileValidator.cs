using System.Collections;
using System.Reflection;
using System.Text.Json;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Profiles;

/// <summary>Validates persisted/imported data before it reaches a live mapping pipeline.</summary>
internal static class ProfileValidator
{
    internal const int MaximumBytes = 2 * 1024 * 1024;

    internal static void Validate(ProfileDocument profile)
    {
        if (profile.PollingRateHz is < 30 or > 1000) { throw new JsonException("Polling rate must be between 30 and 1000 Hz."); }
        if (string.IsNullOrWhiteSpace(profile.Id)) { throw new JsonException("A profile needs an ID."); }
        if (profile.Rules is null || profile.Rules.Count > 512) { throw new JsonException("A profile can contain at most 512 non-null rules."); }
        var nullability = new NullabilityInfoContext();
        Visit(profile, "Profile", 0, nullability);
        if (profile.Rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != profile.Rules.Count ||
            profile.Rules.Any(rule => string.IsNullOrWhiteSpace(rule.Id)))
        {
            throw new JsonException("Every mapping rule must have a unique, non-empty ID.");
        }
    }

    private static void Visit(object value, string path, int depth, NullabilityInfoContext nullability)
    {
        if (depth > 32) { throw new JsonException($"{path} is nested too deeply."); }
        if (value is string text)
        {
            if (text.Length > 65_536) { throw new JsonException($"{path} exceeds 65,536 characters."); }
            return;
        }
        if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d))
        { throw new JsonException($"{path} must be a finite number."); }
        var type = value.GetType();
        if (type.IsEnum)
        {
            if (!Enum.IsDefined(type, value)) { throw new JsonException($"{path} contains an unsupported {type.Name} value."); }
            return;
        }
        if (value is IEnumerable collection)
        {
            var index = 0;
            foreach (var item in collection)
            {
                if (++index > 1024) { throw new JsonException($"{path} contains too many items."); }
                if (item is null) { throw new JsonException($"{path}[{index - 1}] cannot be null."); }
                Visit(item, $"{path}[{index - 1}]", depth + 1, nullability);
            }
            return;
        }
        if (type.Namespace?.StartsWith("GameFlow.Core.", StringComparison.Ordinal) != true) { return; }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod is not null))
        {
            var member = property.GetValue(value);
            if (member is null)
            {
                if (nullability.Create(property).WriteState == NullabilityState.NotNull)
                { throw new JsonException($"{path}.{property.Name} cannot be null."); }
            }
            else { Visit(member, path + "." + property.Name, depth + 1, nullability); }
        }
    }
}
