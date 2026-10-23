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

    [Fact]
    public void Saved_values_are_range_checked()
    {
        Assert.True(RewardInput.IsLevel(1) && RewardInput.IsLevel(200));
        Assert.False(RewardInput.IsLevel(0) || RewardInput.IsLevel(201));
        Assert.True(RewardInput.IsBonus(0) && RewardInput.IsBonus(1000));
        Assert.False(RewardInput.IsBonus(-1) || RewardInput.IsBonus(1001));
    }
}
