using Autofire.Core.Enums;

namespace Autofire.Core.Models.Rules;

public sealed record ButtonRemapRule : MappingRule
{
    public ButtonId SourceButton { get; init; } = ButtonId.RightStick;
    public ButtonId TargetButton { get; init; } = ButtonId.North;
    public bool SuppressSourceButton { get; init; } = true;
}
