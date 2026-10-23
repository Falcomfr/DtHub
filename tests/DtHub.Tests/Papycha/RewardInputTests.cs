using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class RewardInputTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("250")]
    [InlineData("")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public void Level_is_refused(string text) => Assert.False(RewardInput.TryLevel(text, out _));

    [Fact]
    public void Level_is_accepted_trimmed()
    {
        Assert.True(RewardInput.TryLevel(" 191 ", out var level));
        Assert.Equal(191, level);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1001")]
    [InlineData(null)]
    public void Bonus_is_refused(string? text) => Assert.False(RewardInput.TryBonus(text, out _));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("150", 150)]
    public void Bonus_is_accepted(string text, int expected)
    {
        Assert.True(RewardInput.TryBonus(text, out var percent));
        Assert.Equal(expected, percent);
    }
}
