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
