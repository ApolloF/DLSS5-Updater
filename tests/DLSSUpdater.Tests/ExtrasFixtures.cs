using System.IO.Compression;
using System.Net;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// A temp game with ReShade, a catalog of fake add-ons and shader packs served by a fake web, and the <see cref="ReShadeExtras"/>
/// service on top. Nothing touches a real game folder or the network.
/// </summary>
public abstract class ExtrasTestBase : IDisposable
{
    protected readonly string Tmp = Path.Combine(Path.GetTempPath(), "dlssu-extras-" + Guid.NewGuid().ToString("N")[..8]);
    protected readonly string Root;
    protected readonly string Target;
    protected readonly AppSettings Settings = new();
    protected readonly Dictionary<string, byte[]> Web = new();
    protected readonly List<string> Requests = [];
    protected readonly ReShadeExtras Extras;
    protected CatalogDocument Doc = new() { Schema = 1 };

    private sealed class FakeWeb(ExtrasTestBase t) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            t.Requests.Add(url);
            return Task.FromResult(t.Web.TryGetValue(url, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    protected ExtrasTestBase()
    {
        AppPaths.Root = Path.Combine(Tmp, "appdata");
        AppPaths.Ensure();
        Root = Path.Combine(Tmp, "Game");
        Target = Root;
        Directory.CreateDirectory(Target);
        File.WriteAllBytes(Path.Combine(Root, "Game.exe"), SyntheticPe.Build(true, ["d3d11.dll"]));
        Write(Path.Combine(Target, "ReShade.ini"), "[GENERAL]\r\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\r\nTextureSearchPaths=.\\reshade-shaders\\Textures\\**\r\n");
        var store = new ComponentStore(new GitHubClient(), () => true);
        Extras = new ReShadeExtras(new Installer(store), Settings, new CatalogDownloader(new GitHubClient(new FakeWeb(this))));
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Tmp, true);
        }
        catch (IOException) { }
    }

    protected static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    protected GameInfo Game(int bits = 64, string? antiCheat = null)
    {
        var g = GameScanner.Inspect(new GameEntry("Game", Root, "Manual"));
        g.Tech.Bits = bits;
        g.AntiCheat = antiCheat;
        return g;
    }

    protected InstallManifest? Manifest() => InstallManifest.Read(Target);

    protected string Ini(string section, string key) => IniFile.Load(Path.Combine(Target, "ReShade.ini")).Get(section, key) ?? "";

    protected Dictionary<string, string> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(InstallManifest.DirName))
            .ToDictionary(f => Path.GetRelativePath(dir, f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    // ---- catalog building ----

    protected static byte[] Zip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        return ms.ToArray();
    }

    protected static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    protected static string Sha(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Registers the bytes at a URL and returns the catalog entry that points to them.</summary>
    protected CatalogBinary Serve(string name, byte[] bytes, string arch = "x64", params CatalogPayloadItem[] payload)
    {
        var url = "https://files.test/" + Sha(bytes)[..8] + "/" + name;
        Web[url] = bytes;
        return new CatalogBinary { Name = name, Arch = arch, Url = url, Sha256 = Sha(bytes), Size = bytes.Length, Payload = payload.Length == 0 ? null : [.. payload] };
    }

    protected static CatalogPayloadItem Item(string entry, string kind = "addon", string? arch = null, string? dest = null) =>
        new() { Entry = entry, Kind = kind, Arch = arch, Dest = dest };

    protected CatalogAddon AddonOf(string id, params CatalogBinary[] files) => new() { Id = id, Name = id, Files = [.. files] };

    protected CatalogShaderPack PackOf(string id, byte[] zip, string shaders, string? textures, string[] effects, bool isDefault = false)
    {
        var file = Serve(id + ".zip", zip, "both");
        file.Layout = new CatalogPackLayout { ShadersDir = shaders, TexturesDir = textures, Effects = [.. effects] };
        return new CatalogShaderPack { Id = id, Name = id, Category = "general", Default = isDefault, Source = "branch", Version = "abc1234", Repo = "o/" + id, File = file };
    }

    /// <summary>Makes the service use a catalog with these entries.</summary>
    protected void Publish(IEnumerable<CatalogAddon>? addons = null, IEnumerable<CatalogShaderPack>? packs = null)
    {
        Doc = new CatalogDocument
        {
            Schema = 1,
            Sections = new CatalogSections
            {
                Addons = new CatalogAddons { Addons = [.. addons ?? []] },
                ShaderPacks = new CatalogShaderPacks { Packs = [.. packs ?? []] },
            },
        };
        Extras.Reload(Doc);
    }

    // ---- the standard fake catalog ----

    protected const string Lilium = "lilium-hdr";
    protected const string Sweet = "sweetfx";

    protected CatalogShaderPack LiliumPack(string fx = "LILIUM-A") => PackOf(Lilium,
        Zip(("Repo-abc/Shaders/lilium__a.fx", fx), ("Repo-abc/Shaders/Lilium/b.fxh", "B"), ("Repo-abc/Shaders/evil.dll", "MZ"),
            ("Repo-abc/Shaders/.hidden", "h"), ("Repo-abc/Textures/n.png", "PNG"), ("Repo-abc/README.md", "readme")),
        "Repo-abc/Shaders/", "Repo-abc/Textures/", ["lilium__a.fx"], isDefault: true);

    protected CatalogShaderPack SweetPack(string bloom = "BLOOM", string lilium = "OTHER-A") => PackOf(Sweet,
        Zip(("Sweet-1/Shaders/Bloom.fx", bloom), ("Sweet-1/Shaders/lilium__a.fx", lilium)),
        "Sweet-1/Shaders/", null, ["Bloom.fx", "lilium__a.fx"]);

    protected CatalogAddon SwapChain(string v = "1") => AddonOf("swapchain",
        Serve("swapchain_override.addon32", Bytes("SWAP32-" + v), "x86"),
        Serve("swapchain_override.addon64", Bytes("SWAP64-" + v), "x64"));

    protected CatalogAddon XinpZip(string v = "1") => AddonOf("xinp",
        Serve("Xinp.zip", Zip(("Xinp.addon32", "X32-" + v), ("Xinp.addon64", "X64-" + v), ("Xinp.fx", "XFX-" + v)), "both",
            Item("Xinp.addon32", arch: "x86"), Item("Xinp.addon64", arch: "x64"), Item("Xinp.fx", "effect", dest: "Xinp/Xinp.fx")));
}
