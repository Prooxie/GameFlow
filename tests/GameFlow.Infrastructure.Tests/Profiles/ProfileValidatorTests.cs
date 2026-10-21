using System.Text.Json;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Infrastructure.Profiles;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Profiles;

public sealed class ProfileValidatorTests
{
    [Fact]
    public void RejectsNullRulesAndNestedElements()
    {
        Assert.Throws<JsonException>(() => ProfileValidator.Validate(new ProfileDocument { Rules = [null!] }));
        Assert.Throws<JsonException>(() => ProfileValidator.Validate(new ProfileDocument { Rules = [new ButtonComboRule { Steps = [null!] }] }));
    }

    [Fact]
    public void RejectsUndefinedEnumsNonFiniteNumbersAndDuplicateIds()
    {
        Assert.Throws<JsonException>(() => ProfileValidator.Validate(new ProfileDocument { Rules = [new ButtonRemapRule { TargetButton = (ButtonId)900 }] }));
        Assert.Throws<JsonException>(() => ProfileValidator.Validate(new ProfileDocument { Rules = [new GyroMapRule { SensitivityX = float.NaN }] }));
        Assert.Throws<JsonException>(() => ProfileValidator.Validate(new ProfileDocument { Rules = [new ButtonRemapRule { Id = "x" }, new ButtonRemapRule { Id = "x" }] }));
    }
}
