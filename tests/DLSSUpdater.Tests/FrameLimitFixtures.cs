using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>A catalog with the frameLimiters section and games for the frame limiter tests. File contents are tiny strings; every sha256 is the hash of its content.</summary>
internal static class FrameLimitFixtures
{
    public const string Re64 = "MZ relimiter 64 v3.3.5", Re32 = "MZ relimiter 32 v3.3.5", Dc64 = "MZ display commander 64", Dc32 = "MZ display commander 32";

    private static string File(string name, string arch, string content, string version, bool mirror) =>
        $$"""{ "name": "{{name}}", "arch": "{{arch}}", "url": "https://author.test/{{name}}", "sha256": "{{CatalogFixtures.Sha(content)}}", "size": {{System.Text.Encoding.ASCII.GetByteCount(content)}}, "version": "{{version}}", "firstSeen": "2026-10-21T08:00:00Z", {{(mirror ? "\"license\": \"MIT\", \"mirrorUrl\": \"https://mirror.test/" + name + "\"," : "")}} "history": [] }""";

    public static string Json(bool relimiter32 = true, string re64 = Re64) => $$"""
        {
          "schema": 1,
          "repository": "ApolloF/DLSS5-Updater-catalog",
          "generated": "2026-10-21T12:00:00Z",
          "sections": {
            "frameLimiters": {
              "updated": "2026-10-21T12:00:00Z",
              "sources": [],
              "limiters": [
                { "id": "relimiter", "name": "ReLimiter", "repository": "RankFTW/ReLimiter", "release": "v3.3.5", "license": "MIT",
                  "files": [ {{File("relimiter.addon64", "x64", re64, "2026-10-21-aaaaaaa", true)}}{{(relimiter32 ? ", " + File("relimiter.addon32", "x86", Re32, "2026-10-21-bbbbbbb", true) : "")}} ] },
                { "id": "display-commander", "name": "Display Commander", "repository": "pmnoxx/display-commander", "release": "latest",
                  "files": [ {{File("zzz_display_commander.addon64", "x64", Dc64, "2026-10-21-ccccccc", false)}}, {{File("zzz_display_commander.addon32", "x86", Dc32, "2026-10-21-ddddddd", false)}} ] }
              ]
            }
          }
        }
        """;

    public static CatalogFrameLimiters Section(string? json = null) =>
        JsonSerializer.Deserialize(json ?? Json(), JsonCtx.Default.CatalogDocument)!.Sections.FrameLimiters!;

    public static GameInfo Game(string name, GameEngine engine = GameEngine.Unknown, int bits = 64, string? antiCheat = null, string project = "Proj", string? root = null) => new()
    {
        Id = name, Name = name, Root = root ?? @"C:\Games\" + name, AntiCheat = antiCheat,
        Tech = new GameTech
        {
            Engine = engine, EngineVersion = engine == GameEngine.Unreal ? "5.3.2" : null, Bits = bits,
            UeProject = engine == GameEngine.Unreal ? project : null, Apis = GraphicsApis.Dx12,
        },
    };

    public static LimitEnvironment Env(bool nvidia = true, bool has64 = true, bool has32 = true, bool engineIni = true) => new(nvidia,
        new Dictionary<LimitMethod, (bool, bool)> { [LimitMethod.ReLimiter] = (has64, has32), [LimitMethod.DisplayCommander] = (has64, has32) }, engineIni);
}
