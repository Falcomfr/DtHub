using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class TrackerCharacterMessageTests
{
    [Fact]
    public void Reads_the_character_picked_on_the_site() =>
        Assert.Equal("26878cd2", TrackerCharacterMessage.Parse("""{"kind":"trackerCharacter","id":"26878cd2"}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"kind":"loaded"}""")]
    [InlineData("""{"kind":"trackerCharacter"}""")]
    [InlineData("""{"kind":"trackerCharacter","id":""}""")]
    [InlineData("""{"kind":"trackerCharacter","id":42}""")]
    [InlineData("[1,2]")]
    public void Anything_else_is_ignored(string? payload) =>
        Assert.Null(TrackerCharacterMessage.Parse(payload));
}
