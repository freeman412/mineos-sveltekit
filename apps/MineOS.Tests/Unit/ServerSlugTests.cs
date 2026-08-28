using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers ServerService.Slugify and GenerateServerDirectoryName, which decide the
/// on-disk directory for a newly created server.
///
/// The typed name used to become the directory name verbatim and permanently, so a
/// label like "Server Loco" produced "servers/Server Loco" forever and every consumer
/// of that path had to survive whatever a label may contain — spaces alone broke screen
/// session detection. Now that a server carries a mutable display name (#180), the
/// on-disk identity is a slug and the label is stored separately.
///
/// Existing servers are untouched; this governs new ones only.
/// </summary>
public class ServerSlugTests
{
    [Theory]
    [InlineData("Server Loco", "server-loco")]
    [InlineData("Create Mod Trains", "create-mod-trains")]
    [InlineData("Mystical Magical", "mystical-magical")]
    [InlineData("LIFESTEAL", "lifesteal")]
    [InlineData("lobby", "lobby")]
    // Runs of anything unusable collapse to a single hyphen, never a run of them.
    [InlineData("Airships   Creative", "airships-creative")]
    [InlineData("my_server.name", "my-server-name")]
    [InlineData("a---b", "a-b")]
    // Leading and trailing junk is trimmed rather than becoming edge hyphens.
    [InlineData("  spaced  ", "spaced")]
    [InlineData("--dashes--", "dashes")]
    [InlineData("...", "")]
    // Nothing usable at all: the caller substitutes a name.
    [InlineData("日本語", "")]
    [InlineData("", "")]
    public void SlugifyReducesALabelToAPathSafeSlug(string label, string expected)
    {
        Assert.Equal(expected, ServerService.Slugify(label));
    }

    [Fact]
    public void SlugifyCapsLengthWithoutLeavingATrailingHyphen()
    {
        var slug = ServerService.Slugify(new string('a', 30) + " " + new string('b', 30));
        Assert.True(slug.Length <= 40, $"slug was {slug.Length} chars");
        Assert.False(slug.EndsWith('-'), "a truncated slug must not end on the hyphen it cut at");
    }

    [Theory]
    [InlineData("Server Loco", "server-loco")]
    [InlineData("lobby", "lobby")]
    public void DirectoryNameIsTheSlugPlusAShortSuffix(string label, string expectedSlug)
    {
        var directory = ServerService.GenerateServerDirectoryName(label);

        Assert.StartsWith(expectedSlug + "-", directory);
        Assert.Equal(expectedSlug.Length + 5, directory.Length);
        Assert.Matches("^[a-z0-9-]+$", directory);
    }

    [Fact]
    public void DirectoryNameFallsBackWhenTheLabelHasNothingUsable()
    {
        // A label of entirely non-Latin characters still has to produce a usable path.
        var directory = ServerService.GenerateServerDirectoryName("日本語");

        Assert.StartsWith("server-", directory);
        Assert.Matches("^[a-z0-9-]+$", directory);
    }

    [Fact]
    public void DirectoryNamesAreUniqueForTheSameLabel()
    {
        // The suffix is unconditional rather than collision-triggered, so two creates
        // racing on the same label cannot land on the same directory.
        var names = Enumerable.Range(0, 50)
            .Select(_ => ServerService.GenerateServerDirectoryName("Server Loco"))
            .ToHashSet();

        Assert.Equal(50, names.Count);
    }

    [Theory]
    [InlineData("Server Loco")]
    [InlineData("Create Mod Trains")]
    [InlineData("  weird...name__here  ")]
    public void DirectoryNamesNeverContainCharactersThatBrokeThingsBefore(string label)
    {
        var directory = ServerService.GenerateServerDirectoryName(label);

        Assert.DoesNotContain(' ', directory);
        Assert.DoesNotContain("..", directory);
        Assert.DoesNotContain('/', directory);
        Assert.False(directory.StartsWith('-'), "must not start with a hyphen");
    }
}
