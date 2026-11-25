using SimTrade.Services;

namespace SimTrade.Tests;

public class AppUpdateServiceTests
{
    [Theory]
    [InlineData("v1.0.70", 1, 0, 70)]
    [InlineData("1.0.70", 1, 0, 70)]
    [InlineData("SimTrade-Setup-1.0.71.exe", 1, 0, 71)]
    public void TryParseVersion_AcceptsTagsAndSetupNames(string text, int major, int minor, int build)
    {
        Assert.True(AppUpdateService.TryParseVersion(text, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Fact]
    public void TryParseVersion_RejectsGarbage()
    {
        Assert.False(AppUpdateService.TryParseVersion("latest", out _));
        Assert.False(AppUpdateService.TryParseVersion("", out _));
    }
}
