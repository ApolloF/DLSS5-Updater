using System.Text.Json;
using DLSSUpdater.Core;

namespace DLSSUpdater.Scan;

/// <summary>What the folder walk of <see cref="GameInspector"/> saw besides exes and DLSS dlls.</summary>
internal sealed class GameSignals
{
    public bool D3D12Core { get; set; }
    public bool Ue3File { get; set; }
    public UpscalerSignals Upscalers { get; set; }
}

/// <summary>
/// Works out graphics API, bitness, engine and Unreal version of one game from its files. Pure file inspection: it never
/// loads or launches anything, and anything it cannot read leaves that part Unknown instead of throwing.
/// </summary>
internal static class GameDetector
{
    private static readonly string[] UnrealPlatforms = ["Win64", "WinGDK", "Win32"];
    private static readonly string[] BinFolders = ["Win64", "WinGDK", "Bin64", "x64"];
    private const int MaxDllsNextToExe = 24;
    private const int MaxOtherExes = 6;

    // dlls beside the exe that say nothing about the game's own renderer: runtimes, system copies, middleware.
    private static readonly string[] SystemDllPrefixes =
    [
        "api-ms-", "msvc", "vcruntime", "concrt", "vcomp", "ucrtbase", "d3dcompiler_", "d3dx", "xinput", "xaudio", "x3daudio",
        "steam_api", "nvngx", "nvapi", "sl.", "winmm", "version", "dinput", "dsound", "dxgi", "d3d", "ddraw", "opengl32", "vulkan-1",
        "reshade", "libxess", "amd_fidelityfx", "ffx_", "eossdk", "galaxy", "discord",
    ];

    public static GameTech Detect(string root, IReadOnlyList<string> rankedExes, IReadOnlyList<(string Path, long Size)> allExes,
        GameSignals signals, Func<string, Version?> versionOf)
    {
        var tech = new GameTech { Upscalers = signals.Upscalers };
        var main = rankedExes.FirstOrDefault();
        try { DetectEngine(tech, root, main, allExes, signals, versionOf); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Engine detection for {root} stopped early ({ex.Message})");
        }
        try { DetectApis(tech, main, root, allExes, signals); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"API detection for {root} stopped early ({ex.Message})");
        }
        tech.ModFolder ??= main is null ? null : Path.GetDirectoryName(main);
        return tech;
    }

    // ---------- engine ----------

    private static void DetectEngine(GameTech tech, string root, string? main, IReadOnlyList<(string Path, long Size)> allExes,
        GameSignals signals, Func<string, Version?> versionOf)
    {
        var exeDir = main is null ? null : Path.GetDirectoryName(main);
        var binaries = UnrealBinaries(root, allExes);
        var baseEngineIni = File.Exists(Path.Combine(root, "Engine", "Config", "BaseEngine.ini"));

        if (binaries is { HasShipping: true })
        {
            SetUnreal(tech, binaries, root, versionOf);
        }
        else if (signals.Ue3File || Directory.Exists(Path.Combine(root, "TAGame")) || (binaries is not null && baseEngineIni))
        {
            tech.Engine = GameEngine.UnrealLegacy;
            if (binaries is not null) tech.UeProject = binaries.Project;
        }
        else if (binaries is not null)
        {
            SetUnreal(tech, binaries, root, versionOf);
        }
        else if (IsUnity(root, exeDir))
        {
            tech.Engine = GameEngine.Unity;
        }
        else if (HasFile(root, exeDir, "re_chunk_000.pak"))
        {
            tech.Engine = GameEngine.REEngine;
        }
    }

    private sealed record UnrealBin(string Folder, string Project, bool HasShipping, string? ShippingExe);

    /// <summary>The best Binaries\Win64|WinGDK|Win32 folder that holds a game exe, skipping the shared Engine folder; Shipping builds win.</summary>
    private static UnrealBin? UnrealBinaries(string root, IReadOnlyList<(string Path, long Size)> exes)
    {
        var engineDir = Path.Combine(root, "Engine") + "\\";
        return exes
            .Where(e => !e.Path.StartsWith(engineDir, StringComparison.OrdinalIgnoreCase) && !GameInspector.IsHelperExe(e.Path))
            .Select(e => (e.Path, e.Size, Dir: Path.GetDirectoryName(e.Path)!))
            .Where(e => Array.Exists(UnrealPlatforms, p => Path.GetFileName(e.Dir).Equals(p, StringComparison.OrdinalIgnoreCase)) &&
                        Path.GetFileName(Path.GetDirectoryName(e.Dir)) is { } parent && parent.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Dir, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var shipping = g.Where(e => Path.GetFileName(e.Path).Contains("Shipping", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Size).Select(e => e.Path).FirstOrDefault();
                var project = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(g.Key)!)!);
                return new UnrealBin(g.Key, project, shipping is not null, shipping);
            })
            .OrderByDescending(b => b.HasShipping)
            .ThenBy(b => Array.FindIndex(UnrealPlatforms, p => Path.GetFileName(b.Folder).Equals(p, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(b => b.Folder.Count(c => c == '\\'))
            .FirstOrDefault();
    }

    private static void SetUnreal(GameTech tech, UnrealBin bin, string root, Func<string, Version?> versionOf)
    {
        tech.Engine = GameEngine.Unreal;
        tech.UeProject = bin.Project;
        tech.ModFolder = bin.Folder;
        tech.EngineVersion = UnrealVersion(root, bin.ShippingExe, versionOf);
    }

    private static string? UnrealVersion(string root, string? shippingExe, Func<string, Version?> versionOf)
    {
        var win64 = Path.Combine(root, "Engine", "Binaries", "Win64");
        foreach (var crash in new[] { "CrashReportClient.exe", "CrashReportClient-Win64-Shipping.exe" })
            if (UeVersionOf(Path.Combine(win64, crash), versionOf) is { } fromCrash) return fromCrash;

        if (BuildVersion(Path.Combine(root, "Engine", "Build", "Build.version")) is { } fromBuild) return fromBuild;
        return shippingExe is null ? null : UeVersionOf(shippingExe, versionOf);
    }

    private static string? UeVersionOf(string exe, Func<string, Version?> versionOf)
    {
        if (!File.Exists(exe)) return null;
        Version? v;
        try { v = versionOf(exe); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        return v is { Major: >= 4 and <= 6 } ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : null;
    }

    /// <summary>"5.4.3" from Engine\Build\Build.version (MajorVersion, MinorVersion, PatchVersion), if it names Unreal 4..6.</summary>
    internal static string? BuildVersion(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("MajorVersion", out var major) || !major.TryGetInt32(out var maj) ||
                maj is < 4 or > 6) return null;
            var minor = r.TryGetProperty("MinorVersion", out var mi) && mi.TryGetInt32(out var min) ? min : 0;
            var patch = r.TryGetProperty("PatchVersion", out var pa) && pa.TryGetInt32(out var pat) ? pat : 0;
            return $"{maj}.{minor}.{patch}";
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool HasFile(string root, string? exeDir, string name) =>
        File.Exists(Path.Combine(root, name)) || (exeDir is not null && File.Exists(Path.Combine(exeDir, name)));

    private static bool IsUnity(string root, string? exeDir) =>
        HasFile(root, exeDir, "UnityPlayer.dll") || HasFile(root, exeDir, "GameAssembly.dll") || UnityDataDirs(root, exeDir).Any();

    /// <summary>The &lt;name&gt;_Data folders beside the game: Unity names the data folder after the exe.</summary>
    private static IEnumerable<string> UnityDataDirs(string root, string? exeDir)
    {
        foreach (var dir in new[] { root, exeDir }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var data in Directory.EnumerateDirectories(dir, "*_Data"))
            {
                var stem = Path.GetFileName(data)[..^"_Data".Length];
                if (File.Exists(Path.Combine(dir, stem + ".exe")) || File.Exists(Path.Combine(data, "globalgamemanagers")) ||
                    Directory.Exists(Path.Combine(data, "Managed")) || File.Exists(Path.Combine(data, "boot.config")))
                    yield return data;
            }
        }
    }

    // ---------- graphics API ----------

    private static void DetectApis(GameTech tech, string? main, string root, IReadOnlyList<(string Path, long Size)> allExes, GameSignals signals)
    {
        var scan = main is null ? null : ScanExe(main, allExes);
        tech.Bits = scan?.Bits ?? 0;

        var direct = scan?.Apis ?? GraphicsApis.None;
        var apis = direct;
        if (apis == GraphicsApis.None && scan?.Dxgi == true) apis = GraphicsApis.Dx12; // dxgi.dll alone: a hint, not proof

        // Unity, Unreal and RE Engine link opengl32 statically even when they render with D3D.
        if (apis == GraphicsApis.OpenGL && tech.Engine is GameEngine.Unity or GameEngine.Unreal or GameEngine.REEngine)
        {
            direct = GraphicsApis.None;
            apis = GraphicsApis.Dx11;
        }

        if (tech.Engine == GameEngine.Unity && UnityBootConfigApi(root, main) is { } boot)
        {
            direct = boot;
            apis = boot;
        }

        if (signals.D3D12Core)
        {
            direct |= GraphicsApis.Dx12;
            apis |= GraphicsApis.Dx12;
        }

        // Unreal 5 does not ship a DX11-only renderer, so a DX11 import there is the fallback path, not the game's API.
        if (tech.Engine == GameEngine.Unreal && tech.UeMajor >= 5 && apis == GraphicsApis.Dx11) apis = GraphicsApis.Dx12;

        if (apis == GraphicsApis.None) apis = EngineDefault(tech);

        tech.Apis = apis;
        tech.ApisGuessed = (apis & ~direct) != GraphicsApis.None;
    }

    /// <summary>The API the engine uses unless told otherwise; used only when no file said anything.</summary>
    private static GraphicsApis EngineDefault(GameTech tech) => tech.Engine switch
    {
        GameEngine.Unreal when tech.UeMajor >= 5 => GraphicsApis.Dx12,
        GameEngine.Unreal when tech.UeMajor == 4 => GraphicsApis.Dx11,
        GameEngine.UnrealLegacy => GraphicsApis.Dx9,
        GameEngine.Unity => GraphicsApis.Dx11,
        GameEngine.REEngine => GraphicsApis.Dx12,
        _ => GraphicsApis.None,
    };

    // Unity's GraphicsDeviceType numbers, as written to boot.config by gfx-device-type.
    internal static GraphicsApis UnityDeviceType(int value) => value switch
    {
        0 or 17 => GraphicsApis.OpenGL,
        1 => GraphicsApis.Dx9,
        2 => GraphicsApis.Dx11,
        18 => GraphicsApis.Dx12,
        21 => GraphicsApis.Vulkan,
        _ => GraphicsApis.None,
    };

    private static GraphicsApis? UnityBootConfigApi(string root, string? main)
    {
        var exeDir = main is null ? null : Path.GetDirectoryName(main);
        var candidates = new List<string>();
        if (main is not null && exeDir is not null)
            candidates.Add(Path.Combine(exeDir, Path.GetFileNameWithoutExtension(main) + "_Data", "boot.config"));
        candidates.AddRange(UnityDataDirs(root, exeDir).Select(d => Path.Combine(d, "boot.config")));

        foreach (var file in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    var eq = line.IndexOf('=');
                    if (eq < 0 || !line[..eq].Trim().Equals("gfx-device-type", StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(line[(eq + 1)..].Trim(), out var type) && UnityDeviceType(type) is var api && api != GraphicsApis.None)
                        return api;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>
    /// PE scan of the main exe, then the game dlls beside it, then the other Win64|WinGDK|Bin64|x64 exes, stopping at the first
    /// stage that finds an API. Cached per exe path and last write time.
    /// </summary>
    private static PeScanResult ScanExe(string main, IReadOnlyList<(string Path, long Size)> allExes)
    {
        long ticks;
        try { ticks = File.GetLastWriteTimeUtc(main).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ticks = 0; }
        if (DetectCache.Get(main, ticks) is { } cached) return cached;

        var result = new PeScanResult { WriteTicks = ticks };
        if (PeReader.Read(main) is { } pe)
        {
            result.Bits = pe.Bits;
            Accumulate(result, pe);
        }

        if (result.Apis == GraphicsApis.None)
            foreach (var dll in DllsNextTo(main))
            {
                if (PeReader.Read(dll) is { } d) Accumulate(result, d);
                if (result.Apis != GraphicsApis.None) break;
            }

        if (result.Apis == GraphicsApis.None)
            foreach (var exe in OtherBinExes(main, allExes))
            {
                if (PeReader.Read(exe) is { } e) Accumulate(result, e);
                if (result.Apis != GraphicsApis.None) break;
            }

        // A locked or unreadable exe is not remembered, so the next scan tries again.
        if (result.Bits != 0) DetectCache.Put(main, result);
        return result;
    }

    /// <summary>Adds the APIs one file links. Its delay-load table counts only when it has no DX11+ import, so engines that
    /// delay-load d3d12 as an optional path aren't promoted to DX12.</summary>
    private static void Accumulate(PeScanResult result, PeFile pe)
    {
        var apis = ApisOf(pe.Imports);
        if ((apis & (GraphicsApis.Dx11 | GraphicsApis.Dx12)) == GraphicsApis.None) apis |= ApisOf(pe.DelayImports);
        result.Apis |= apis;
        if (pe.Imports.Contains("dxgi.dll") || pe.DelayImports.Contains("dxgi.dll")) result.Dxgi = true;
    }

    private static GraphicsApis ApisOf(IEnumerable<string> dlls)
    {
        var apis = GraphicsApis.None;
        foreach (var d in dlls) apis |= GraphicsApiInfo.FromDll(d);
        return apis;
    }

    private static IEnumerable<string> DllsNextTo(string exe)
    {
        var dir = Path.GetDirectoryName(exe);
        if (dir is null || !Directory.Exists(dir)) return [];
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*.dll")
                .Where(f => !SystemDllPrefixes.Any(p => f.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.Length)
                .Take(MaxDllsNextToExe)
                .Select(f => f.FullName)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> OtherBinExes(string main, IReadOnlyList<(string Path, long Size)> allExes) =>
        allExes
            .Where(e => !e.Path.Equals(main, StringComparison.OrdinalIgnoreCase) && !GameInspector.IsHelperExe(e.Path) &&
                        Array.Exists(BinFolders, f => string.Equals(Path.GetFileName(Path.GetDirectoryName(e.Path)), f, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Size)
            .Take(MaxOtherExes)
            .Select(e => e.Path)
            .ToList();
}
