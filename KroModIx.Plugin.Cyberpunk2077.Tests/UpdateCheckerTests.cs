using FluentAssertions;
using KroModIx.Plugin.Cyberpunk2077.Services;
using Xunit;

namespace KroModIx.Plugin.Cyberpunk2077.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.0.0", "2.0.0", true, true)]
    [InlineData("2.0.0", "1.0.0", true, false)]
    [InlineData("2.0.0", "2.0.0", true, false)]
    [InlineData("v2.4.1", "2.5.0", true, true)]
    [InlineData("1.0.0-beta", "1.0.0", true, false)] // pre-release strippt zu 1.0.0
    [InlineData("garbage", "1.0.0", false, false)]
    [InlineData("1.0", "2.0", true, true)]
    public void TryCompareVersions_typische_Faelle(string installed, string nexus,
        bool expectedParseOk, bool expectedIsNewer)
    {
        var ok = CyberpunkUpdateChecker.TryCompareVersions(installed, nexus, out var isNewer);
        ok.Should().Be(expectedParseOk);
        if (expectedParseOk) isNewer.Should().Be(expectedIsNewer);
    }
}
