using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>A small catalog in the shape the generator in ApolloF/DLSS5-Updater-catalog writes (schema 1).</summary>
internal static class CatalogFixtures
{
    public static string Sha(string content) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(content))).ToLowerInvariant();

    public static string Json(string schema = "1") => $$"""
        {
          "schema": {{schema}},
          "repository": "ApolloF/DLSS5-Updater-catalog",
          "generated": "2026-10-21T12:00:00Z",
          "sections": {
            "luma": { "updated": "2026-10-21T12:00:00Z", "something": ["a later workstream's data"] },
            "renodx": {
              "updated": "2026-10-21T12:00:00Z",
              "sources": [ { "url": "https://x.test/games-index.json", "count": 4 } ],
              "generic": [
                { "id": "unreal", "name": "Unreal Engine (generic)", "files": [
                  { "name": "renodx-unrealengine.addon64", "arch": "x64", "url": "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-unrealengine.addon64",
                    "sha256": "{{new string('a', 64)}}", "size": 100, "version": "2026-10-21-aaaaaaa", "firstSeen": "2026-10-21T12:00:00Z", "license": "MIT",
                    "mirrorUrl": "https://github.com/ApolloF/DLSS5-Updater-catalog/releases/download/mirror-2026-10/aaaaaaaaaaaaaaaa-renodx-unrealengine.addon64", "history": [] } ] },
                { "id": "unity", "name": "Unity Engine (generic)", "files": [
                  { "name": "renodx-unityengine.addon64", "arch": "x64", "url": "https://x.test/u64", "sha256": "{{new string('b', 64)}}", "size": 5, "history": [] },
                  { "name": "renodx-unityengine.addon32", "arch": "x86", "url": "https://x.test/u32", "sha256": "{{new string('c', 64)}}", "size": 5, "history": [] } ] }
              ],
              "mods": [
                { "id": "hades", "name": "Hades", "aliases": [], "steamAppIds": [1145360], "authors": ["Voosh"], "listing": "main", "status": "stable", "compat": "working", "blocked": false,
                  "files": [ { "name": "renodx-hades.addon64", "arch": "x64", "url": "https://x.test/hades", "sha256": "{{new string('d', 64)}}", "size": 7, "version": "2026-10-20-ddddddd", "firstSeen": "2026-10-20T08:00:00Z",
                              "history": [ { "version": "2026-10-01-1111111", "sha256": "{{new string('1', 64)}}", "size": 6, "firstSeen": "2026-10-01T08:00:00Z" } ] } ] },
                { "id": "hades-ii", "name": "Hades II", "aliases": [], "steamAppIds": [1145350], "authors": [], "listing": "main", "blocked": false,
                  "files": [ { "name": "renodx-hades2.addon64", "arch": "x64", "url": "https://x.test/hades2", "sha256": "{{new string('e', 64)}}", "size": 8, "history": [] } ] },
                { "id": "alien-isolation", "name": "Alien: Isolation", "aliases": ["Alien Isolation"], "steamAppIds": [214490], "authors": ["Musa"], "listing": "main", "blocked": false,
                  "files": [ { "name": "renodx-alienisolation.addon32", "arch": "x86", "url": "https://x.test/alien", "sha256": "{{new string('f', 64)}}", "size": 9, "history": [] } ] },
                { "id": "deaths-door", "name": "Death's Door", "aliases": [], "steamAppIds": [894020], "authors": [], "listing": "main", "generic": "unity", "blocked": false, "files": [] },
                { "id": "old-mod", "name": "Old Mod", "aliases": [], "steamAppIds": [999], "authors": [], "listing": "deprecated", "blocked": false, "files": [] },
                { "id": "crashy", "name": "Crashy", "aliases": [], "steamAppIds": [777], "authors": [], "listing": "main", "blocked": true, "blockReason": "crashes on start", "files": [] }
              ],
              "engineGames": [ { "engine": "ue-extended", "name": "Abiotic Factor", "compat": "working", "notes": "Engine.ini" } ],
              "overrides": {
                "nameFixes": [ { "id": "alien-isolation", "aliases": ["Alien: Isolation GOTY"] } ],
                "appIdFixes": [], "blocklist": [],
                "ueExtendedOptOut": [ { "steamAppId": 4242, "reason": "no HDR" }, { "name": "Some Game" } ],
                "engineIniPaths": [ { "steamAppId": 4242, "path": "Game\\Saved\\Config\\Windows\\Engine.ini" } ]
              }
            }
          }
        }
        """;

    public static CatalogRenodx Renodx(string? json = null) =>
        JsonSerializer.Deserialize(json ?? Json(), JsonCtx.Default.CatalogDocument)!.Sections.Renodx!;
}
