using OrderTextTrainer.Core.Services;
using Xunit;

namespace pc.Tests;

public sealed class WearPeriodFixedRulesTests
{
    [Theory]
    [InlineData("日抛", "日抛2片")]
    [InlineData("日抛两片装", "日抛2片")]
    [InlineData("日抛10片", "日抛10片")]
    [InlineData("日抛十片装", "日抛10片")]
    [InlineData("半年抛", "半年抛")]
    [InlineData("半抛", "半年抛")]
    [InlineData("年抛", "年抛")]
    [InlineData("试戴片", "试戴片")]
    [InlineData("试用", "试戴片")]
    [InlineData("lenspop日抛试戴片", "日抛2片")]
    public void MatchExplicitCanonicalWearPeriod_ShouldReturnExpectedCanonicalValue(string source, string expected)
    {
        var actual = WearPeriodFixedRules.MatchExplicitCanonicalWearPeriod(source);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("日抛10片", true)]
    [InlineData("日抛十片装", true)]
    [InlineData("日抛", false)]
    [InlineData("半年抛", false)]
    public void ContainsExplicitTenPieceDailyCue_ShouldDetectOnlyTenPieceDaily(string source, bool expected)
    {
        var actual = WearPeriodFixedRules.ContainsExplicitTenPieceDailyCue(source);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("日抛两片装", "日抛2片")]
    [InlineData("半抛", "半年抛")]
    [InlineData("试用", "试戴片")]
    [InlineData("月抛", "月抛")]
    public void NormalizeConfiguredWearPeriod_ShouldNormalizeKnownAliases(string source, string expected)
    {
        var actual = WearPeriodFixedRules.NormalizeConfiguredWearPeriod(source);

        Assert.Equal(expected, actual);
    }
}
