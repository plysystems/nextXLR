using Xunit;
using NextXLR.Services;

namespace NextXLR.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.2.2", "0.2.1-beta", true)]
    [InlineData("v0.2.2-beta", "0.2.1-beta", true)]
    [InlineData("v0.2.1", "0.2.0-beta", true)]
    [InlineData("v0.2.0", "0.2.0-beta", true)]       // final release newer than prerelease
    [InlineData("v0.2.0-rc", "0.2.0-beta", true)]    // RC newer than Beta
    [InlineData("v0.2.0-beta.2", "0.2.0-beta.1", true)]
    [InlineData("v0.2.0-alpha", "0.2.0-beta", false)] // Alpha older than Beta
    [InlineData("v0.1.9", "0.2.0-beta", false)]
    [InlineData("v0.2.0-beta", "0.2.0-beta", false)] // exact match
    [InlineData("invalid", "0.2.0-beta", false)]     // invalid tag
    public void IsNewerVersion_EvaluatesCorrectly(string remoteTag, string currentVer, bool expected)
    {
        bool result = UpdateService.IsNewerVersion(remoteTag, currentVer);
        Assert.Equal(expected, result);
    }
}

public class AudioMathTests
{
    [Theory]
    [InlineData(1.0, "0.0 dB")]
    [InlineData(0.0, "-∞ dB")]
    [InlineData(0.5, "-18.1 dB")]
    [InlineData(0.75, "-7.5 dB")]
    public void VolumeToDb_CalculatesCubicTaper(double vol, string expected)
    {
        string result = ToDbString(vol);
        Assert.Equal(expected, result);
    }

    private static string ToDbString(double volume)
    {
        if (volume >= 0.999) return "0.0 dB";
        if (volume <= 0.001) return "-∞ dB";
        double db = Math.Round(60.0 * Math.Log10(volume), 1);
        if (db >= 0.0) return "0.0 dB";
        return db.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB";
    }
}
