using System.Text;

namespace Radar.Server.Features.Vault;

/// <summary>Texts the app writes into the vault and the skill. Embedded in the server: the client never supplies them.</summary>
public static class VaultTemplates
{
    public static string Read(string file)
    {
        var asm = typeof(VaultTemplates).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("." + file, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Normalize(reader.ReadToEnd());
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

    public static string IndexMain() => Read("index-main.md");

    public static string ProjectIndex(string repoId, string name) =>
        Read("project-index.md").Replace("{{id}}", Uri.EscapeDataString(repoId)).Replace("{{name}}", name);
}
