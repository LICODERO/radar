namespace Radar.Scanner.Tests;

/// <summary>Builds throw-away directory trees (repos cannot be committed inside a repo, so fixtures are generated).</summary>
public sealed class FixtureTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "radar-tests-" + Guid.NewGuid().ToString("N"));

    public FixtureTree() => Directory.CreateDirectory(Root);

    public string Path_(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public string Dir(string relative)
    {
        var p = Path_(relative);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relative, string content = "")
    {
        var p = Path_(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public string Repo(string relative)
    {
        Dir(relative + "/.git");
        return Path_(relative);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { /* best effort */ }
    }
}
