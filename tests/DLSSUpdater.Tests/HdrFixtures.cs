using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>A catalog and games for the RenoDX HDR tests. File contents are tiny strings; every sha256 is the hash of its content.</summary>
internal static class HdrFixtures
{
    public const string UeExtContent = "MZ ue-extended build 1";
    public const string UeContent = "MZ unreal generic build";
    public const string Unity64Content = "MZ unity 64";
    public const string Unity32Content = "MZ unity 32";
    public const string HadesContent = "MZ hades";
    public const string AlienContent = "MZ alien 32";

    private static string File(string name, string arch, string content, string version) =>
        $$"""{ "name": "{{name}}", "arch": "{{arch}}", "url": "https://author.test/{{name}}", "sha256": "{{CatalogFixtures.Sha(content)}}", "size": {{System.Text.Encoding.ASCII.GetByteCount(content)}}, "version": "{{version}}", "firstSeen": "2026-10-21T08:00:00Z", "license": "MIT", "history": [] }""";

    public static string Json(string ueExtContent = UeExtContent, string ueExtVersion = "2026-10-21-aaaaaaa") => $$"""
        {
          "schema": 1,
          "repository": "ApolloF/DLSS5-Updater-catalog",
          "generated": "2026-10-21T12:00:00Z",
          "sections": {
            "renodx": {
              "generic": [
                { "id": "unreal", "name": "Unreal Engine (generic)", "files": [ {{File("renodx-unrealengine.addon64", "x64", UeContent, "2026-10-20-uuuuuuu")}} ] },
                { "id": "unity", "name": "Unity Engine (generic)", "files": [ {{File("renodx-unityengine.addon64", "x64", Unity64Content, "2026-10-20-1111111")}}, {{File("renodx-unityengine.addon32", "x86", Unity32Content, "2026-10-20-2222222")}} ] },
                { "id": "ue-extended", "name": "Unreal Engine Extended", "files": [ {{File("renodx-ue-extended.addon64", "x64", ueExtContent, ueExtVersion)}} ] }
              ],
              "mods": [
                { "id": "hades", "name": "Hades", "steamAppIds": [1145360], "listing": "main", "compat": "working", "links": { "discussion": "https://github.com/clshortfuse/renodx/discussions/1" },
                  "files": [ {{File("renodx-hades.addon64", "x64", HadesContent, "2026-10-19-hhhhhhh")}} ] },
                { "id": "alien-isolation", "name": "Alien: Isolation", "aliases": ["Alien Isolation"], "steamAppIds": [214490], "listing": "main", "compat": "in-progress",
                  "files": [ {{File("renodx-alienisolation.addon32", "x86", AlienContent, "2026-10-19-aaaaaaa")}} ] },
                { "id": "deaths-door", "name": "Death's Door", "steamAppIds": [894020], "listing": "main", "generic": "unity", "files": [] },
                { "id": "ue-game", "name": "Named Unreal Game", "steamAppIds": [555], "listing": "main", "generic": "unreal", "files": [] },
                { "id": "nexus-only", "name": "Nexus Only", "steamAppIds": [556], "listing": "main", "links": { "nexus": "https://nexusmods.com/x" }, "files": [] },
                { "id": "crashy", "name": "Crashy", "steamAppIds": [777], "listing": "main", "blocked": true, "blockReason": "crashes on start", "files": [] }
              ],
              "engineGames": [
                { "engine": "ue-extended", "name": "Native Game", "compat": "working", "method": "native" },
                { "engine": "ue-extended", "name": "Ini Game", "compat": "working", "method": "ini" },
                { "engine": "ue-extended", "name": "Wukong Like", "compat": "working", "method": "ini", "engineIni": [ { "section": "SystemSettings", "key": "r.HDR.EnableHDROutput", "value": "1" } ] },
                { "engine": "ue-extended", "name": "Upgrade Game", "compat": "in-progress", "method": "upgrade", "copyDestinations": false,
                  "upgrades": [ { "format": "B8G8R8A8_TYPELESS", "mode": "any-size" } ] },
                { "engine": "unreal", "name": "Plain Listed", "compat": "working" },
                { "engine": "unity", "name": "Unity Listed", "compat": "in-progress", "notes": "Enable Swapchain Proxy." }
              ],
              "overrides": {
                "nameFixes": [], "appIdFixes": [],
                "blocklist": [ { "name": "Blocked By Name", "reason": "anti-cheat driver" } ],
                "ueExtendedOptOut": [ { "steamAppId": 4242, "reason": "no HDR" }, { "name": "Opted Out Game" } ],
                "engineIniPaths": [ { "steamAppId": 4343, "path": "Hinted\\Saved\\Config\\Windows\\Engine.ini" } ]
              }
            }
          }
        }
        """;

    public static CatalogRenodx Renodx(string? json = null) => CatalogFixtures.Renodx(json ?? Json());

    public static GameInfo Unreal(string name, int bits = 64, string version = "5.3.2", int? appId = null, string? root = null, string project = "Proj") => new()
    {
        Id = name, Name = name, Root = root ?? @"C:\Games\" + name, SteamAppId = appId,
        Tech = new GameTech
        {
            Engine = GameEngine.Unreal, EngineVersion = version, Bits = bits, UeProject = project,
            ModFolder = (root ?? @"C:\Games\" + name) + $@"\{project}\Binaries\Win64", Apis = GraphicsApis.Dx12,
        },
    };

    public static GameInfo Unity(string name, int bits = 64, int? appId = null) => new()
    {
        Id = name, Name = name, Root = @"C:\Games\" + name, SteamAppId = appId, Tech = new GameTech { Engine = GameEngine.Unity, Bits = bits, Apis = GraphicsApis.Dx11 },
    };

    public static GameInfo Other(string name, GameEngine engine, int bits = 64, int? appId = null) => new()
    {
        Id = name, Name = name, Root = @"C:\Games\" + name, SteamAppId = appId, Tech = new GameTech { Engine = engine, Bits = bits },
    };
}
