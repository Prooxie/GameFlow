using GameFlow.App.ViewModels;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Runtime.Templates;
using Moq;
using Xunit;

namespace GameFlow.Infrastructure.Tests.ViewModels;

/// <summary>Checks that native output choices describe the available controller layout.</summary>
public sealed class OutputKindOptionTests
{
    /// <summary>Native output offers one translated standard gamepad.</summary>
    [Fact]
    public void CreateNativeOptionsUsesCurrentTranslation()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(value => value["StyleLabelSimpleGamepad"]).Returns("Standard controller");

        var options = OutputKindOption.Create(localization.Object, isWindows: false);

        var option = Assert.Single(options);
        Assert.Equal(VirtualControllerKind.GenericDirectInput, option.Kind);
        Assert.Equal("Standard controller", option.Label);
    }
}
