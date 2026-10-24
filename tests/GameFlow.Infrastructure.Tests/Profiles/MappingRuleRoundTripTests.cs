using GameFlow.App.ViewModels;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Profiles;

/// <summary>
/// The rule editor rebuilds EVERY rule of the profile from its view models on
/// any edit, so whatever a view model fails to carry is silently deleted from
/// the saved profile. These pin the fields that used to go missing.
/// </summary>
public sealed class MappingRuleRoundTripTests
{
    private static MappingRule OnALayer(string kind) => kind switch
    {
        "remap" => new ButtonRemapRule { Id = kind, LayerId = "aim" },
        "turbo" => new ButtonAutofireRule { Id = kind, LayerId = "aim" },
        "threshold" => new StickThresholdRule { Id = kind, LayerId = "aim" },
        "stick-autofire" => new StickAutofireRule { Id = kind, LayerId = "aim" },
        "freeze" => new FreezeLastDirectionRule { Id = kind, LayerId = "aim" },
        "multi" => new MultiButtonAutofireRule { Id = kind, LayerId = "aim" },
        "toggle" => new RuleToggleRule { Id = kind, LayerId = "aim" },
        "script" => new ControlScriptRule { Id = kind, LayerId = "aim", ControlKey = "South" },
        "invert" => new AxisInvertRule { Id = kind, LayerId = "aim" },
        _ => new GyroMapRule { Id = kind, LayerId = "aim" } // no editor of its own: kept verbatim
    };

    [Theory]
    [InlineData("remap")]
    [InlineData("turbo")]
    [InlineData("threshold")]
    [InlineData("stick-autofire")]
    [InlineData("freeze")]
    [InlineData("multi")]
    [InlineData("toggle")]
    [InlineData("script")]
    [InlineData("invert")]
    [InlineData("preserved")]
    public void EveryRuleKind_KeepsItsLayerAndIdentity(string kind)
    {
        var rule = OnALayer(kind);
        var rebuilt = MappingRuleViewModel.FromRule(rule with { Name = "Named", Enabled = false, Mode = RuleMode.Passthrough }).ToRule();

        Assert.Equal("aim", rebuilt.LayerId);
        Assert.Equal(rule.Id, rebuilt.Id);
        Assert.Equal("Named", rebuilt.Name);
        Assert.False(rebuilt.Enabled);
        Assert.Equal(RuleMode.Passthrough, rebuilt.Mode);
        Assert.Equal(rule.GetType(), rebuilt.GetType());
    }

    [Fact]
    public void EditingOneRule_DoesNotMoveTheOthersOntoBase()
    {
        using var editor = new MappingEditorViewModel(NullLogger<MappingEditorViewModel>.Instance);
        editor.LoadFromProfile(new ProfileDocument
        {
            Rules =
            [
                new ButtonRemapRule { Id = "base", SourceButton = ButtonId.South, TargetButton = ButtonId.West },
                new ButtonRemapRule { Id = "layer", LayerId = "aim", SourceButton = ButtonId.South, TargetButton = ButtonId.North }
            ]
        });
        IReadOnlyList<MappingRule>? saved = null;
        editor.RulesChanged += (_, rules) => saved = rules;

        editor.Rules[0].Enabled = false;

        Assert.NotNull(saved);
        Assert.Equal("aim", saved.Single(r => r.Id == "layer").LayerId);
    }

    [Fact]
    public void InversionCheckboxes_ChangeOnlyTheAxisTicked_AndKeepUntouchedAxesUnset()
    {
        var viewModel = MappingRuleViewModel.FromRule(new AxisInvertRule
        {
            Id = "invert",
            Axes = new AxisInvert { RightY = true, LeftX = false }
        });

        // A binding echoing what the box already shows must not turn "no
        // opinion" into an explicit off.
        viewModel.InvertLeftY = false;
        viewModel.InvertRightTrigger = true;

        var axes = Assert.IsType<AxisInvertRule>(viewModel.ToRule()).Axes;
        Assert.True(axes.RightY);
        Assert.False(axes.LeftX);
        Assert.Null(axes.LeftY);
        Assert.True(axes.RightTrigger);
        Assert.Null(axes.LeftTrigger);
    }
}
