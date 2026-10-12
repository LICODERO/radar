using Radar.Scanner;

namespace Radar.Scanner.Tests;

public class FrontmatterTests
{
    [Fact]
    public void Parses_scalars_and_inline_lists()
    {
        var fm = Frontmatter.Parse("---\nname: code-reviewer\ndescription: \"Review: strict\"\ntools: Read, Grep, Bash\nmodel: sonnet\n---\n# body");
        Assert.True(fm.Present);
        Assert.True(fm.Valid);
        Assert.Equal("code-reviewer", fm.Get("name"));
        Assert.Equal("Review: strict", fm.Get("description"));
        Assert.Equal(["Read", "Grep", "Bash"], fm.GetList("tools"));
        Assert.Equal("sonnet", fm.Get("model"));
    }

    [Theory]
    [InlineData(">")]
    [InlineData("|")]
    [InlineData(">-")]
    [InlineData("|+")]
    public void Block_scalars_give_the_whole_text_not_the_marker(string marker)
    {
        var fm = Frontmatter.Parse($"---\nname: x\ndescription: {marker}\n  Use when the user wants a review.\n  Also covers follow-ups.\ntools: Read\n---\nbody");
        Assert.Equal("Use when the user wants a review. Also covers follow-ups.", fm.Get("description"));
        Assert.Equal("Read", fm.Get("tools"));
        Assert.Equal("x", fm.Get("name"));
    }

    [Fact]
    public void Plain_and_quoted_values_may_continue_on_the_next_lines()
    {
        var fm = Frontmatter.Parse("---\ndescription: Use when the user asks for a review,\n  including tests and docs.\nwhen: \"before a release,\n  after a freeze\"\n---\n");
        Assert.Equal("Use when the user asks for a review, including tests and docs.", fm.Get("description"));
        Assert.Equal("before a release, after a freeze", fm.Get("when"));
    }

    [Fact]
    public void An_indented_map_under_a_key_is_not_taken_for_a_continuation()
    {
        var fm = Frontmatter.Parse("---\nname: x\nmetadata:\n  type: user\ndescription: A description long enough.\n---\n");
        Assert.Equal("A description long enough.", fm.Get("description"));
        Assert.Null(fm.Get("type"));
    }

    [Fact]
    public void A_lone_carriage_return_still_separates_lines()
    {
        var fm = Frontmatter.Parse("---\rname: x\rdescription: Some description here.\r---\r# Body\r");
        Assert.True(fm.Present);
        Assert.Equal("Some description here.", fm.Get("description"));
        Assert.Contains("# Body", Frontmatter.BodyOf("---\rname: x\r---\r# Body\r"));
    }

    [Fact]
    public void Parses_bracket_and_block_lists()
    {
        var fm = Frontmatter.Parse("---\nagents: [a, b]\nsteps:\n  - one\n  - two\n---\n");
        Assert.Equal(["a", "b"], fm.GetList("agents"));
        Assert.Equal(["one", "two"], fm.GetList("steps"));
    }

    [Fact]
    public void Handles_missing_and_unterminated_frontmatter()
    {
        Assert.False(Frontmatter.Parse("# just markdown").Present);
        Assert.False(Frontmatter.Parse(null).Present);
        var broken = Frontmatter.Parse("---\nname: x\nno end");
        Assert.True(broken.Present);
        Assert.False(broken.Valid);
    }

    [Fact]
    public void Handles_crlf_and_bom_free_keys_case_insensitively()
    {
        var fm = Frontmatter.Parse("---\r\nName: X\r\n---\r\n");
        Assert.Equal("X", fm.Get("name"));
        Assert.Empty(fm.GetList("missing"));
    }
}
