using Xunit;

namespace GameFlow.Infrastructure.Tests.Theming;

/// <summary>
/// Test classes that read or clear the static image-path cache of
/// <see cref="GameFlow.Infrastructure.Theming.ThemeAssetResolver"/>. A theme
/// registry refresh clears that cache, so these classes must not run in
/// parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ThemeFileCacheCollection
{
    /// <summary>The collection name for <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Theme file caches";
}
