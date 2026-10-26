using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>A luma catalog section and zips for the Luma tests. Every zip is built in memory; its sha256 is the hash of the bytes.</summary>
internal static class LumaFixtures
{
    /// <summary>A Luma zip: the add-on, the Luma\ tree, and the ReShade files and DLSS dll the app must not copy.</summary>
    public static byte[] Zip(string addon = "Luma-Prey.addon64", string addonContent = "MZ luma addon v1")
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.ASCII);
                w.Write(content);
            }
            Add(addon, addonContent);
            Add(@"Luma/Shaders/main.hlsl", "SHADER");
            Add(@"Luma/d3dcompiler_47.dll", "MZ compiler");
            Add("dxgi.dll", "RESHADE-FROM-ZIP");
            Add("d3d11.dll", "RESHADE-D3D11-FROM-ZIP");
            Add("ReShade.ini", "[GENERAL]\nFromZip=1\n");
            Add("ReShade.log", "log");
            Add("nvngx_dlss.dll", "DLSS-FROM-ZIP");
        }
        return ms.ToArray();
    }

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static readonly byte[] PreyZip = Zip();
    public static readonly byte[] PreyZipV2 = Zip(addonContent: "MZ luma addon v2");
    public static readonly byte[] UnrealZip = Zip("Luma-Unreal_Engine.addon64", "MZ luma unreal");
    public static readonly byte[] Bl2Zip = Zip("Luma-Borderlands_2.addon32", "MZ luma bl2");

    private static string File(string name, string arch, byte[] bytes) =>
        $$"""{ "name": "{{name}}", "arch": "{{arch}}", "url": "https://author.test/{{name}}", "sha256": "{{Sha(bytes)}}", "size": {{bytes.Length}}, "firstSeen": "2026-10-21T08:00:00Z", "history": [] }""";

    public static string SectionJson(byte[]? prey = null) => $$"""
        {
          "release": "latest-755",
          "unrealPackage": "Unreal_Engine",
          "packages": [
            { "id": "Prey", "files": [ {{File("Luma-Prey.zip", "x64", prey ?? PreyZip)}} ] },
            { "id": "Unreal_Engine", "files": [ {{File("Luma-Unreal_Engine.zip", "x64", UnrealZip)}} ] },
            { "id": "Borderlands_2", "files": [ {{File("Luma-Borderlands_2-x32.zip", "x86", Bl2Zip)}} ] }
          ],
          "mods": [
            { "id": "prey", "name": "Prey", "authors": ["Pumbo"], "status": "working", "notes": "Needs the 2017 build.", "package": "Prey",
              "links": { "discussion": "https://github.com/Filoppi/Luma-Framework/wiki" } },
            { "id": "borderlands-2", "name": "Borderlands 2", "status": "in-progress", "package": "Borderlands_2",
              "requiresDgVoodoo": true, "dgVoodooVersions": ["2.82.3"], "dgVoodooSetup": ["Set the output API to DirectX 11."] },
            { "id": "nexus-mod", "name": "Nexus Mod", "status": "working", "links": { "nexus": "https://nexusmods.com/x" } }
          ],
          "unrealGames": [
            { "name": "Listed Game", "dlss": "yes", "hdr": "yes", "ueVersion": "4.27", "notes": "Disable motion blur.",
              "engineIni": [ { "section": "SystemSettings", "key": "r.LumaTest", "value": "1" } ], "launchArgs": ["-dx11"] },
            { "name": "Blocked Game", "dlss": "blocked", "ueVersion": "4.26" }
          ]
        }
        """;

    public static CatalogLuma Section(byte[]? prey = null) =>
        JsonSerializer.Deserialize($$"""{ "schema": 1, "sections": { "luma": {{SectionJson(prey)}} } }""", JsonCtx.Default.CatalogDocument)!.Sections.Luma!;

    public static LumaPicker Picker(byte[]? prey = null) => new(Section(prey));

    public static GameInfo Unreal(string name, int bits = 64, string version = "4.27.2", GraphicsApis apis = GraphicsApis.Dx11, string? root = null) => new()
    {
        Id = name, Name = name, Root = root ?? @"C:\Games\" + name,
        Tech = new GameTech { Engine = GameEngine.Unreal, EngineVersion = version, Bits = bits, Apis = apis, UeProject = "Proj" },
    };

    public static GameInfo Plain(string name, int bits = 64, GraphicsApis apis = GraphicsApis.Dx11) => new()
    {
        Id = name, Name = name, Root = @"C:\Games\" + name, Tech = new GameTech { Engine = GameEngine.Unknown, Bits = bits, Apis = apis },
    };
}
