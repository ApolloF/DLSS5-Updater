using System.Text.Json.Serialization;

namespace DLSSUpdater.Scan;

/// <summary>NVIDIA features a game ships the runtime for (from its nvngx_* and Streamline sl.* files).</summary>
[Flags]
public enum GameFeatures
{
    None = 0,
    /// <summary>DLSS Super Resolution (nvngx_dlss.dll / sl.dlss.dll).</summary>
    SR = 1,
    /// <summary>DLSS Ray Reconstruction (nvngx_dlssd.dll / sl.dlss_d.dll).</summary>
    RR = 2,
    /// <summary>DLSS Frame Generation (nvngx_dlssg.dll / sl.dlss_g.dll).</summary>
    FG = 4,
    /// <summary>NVIDIA Reflex through Streamline (sl.reflex.dll).</summary>
    Reflex = 8,
    Streamline = 16,
}

public sealed class DlssDll
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public Version? Version { get; set; }
    /// <summary>
    /// DLSS5 Updater added this file (its install record owns it and backed up no original), so it says nothing about
    /// what the game supports. Files it only updated stay the game's.
    /// </summary>
    public bool Installed { get; set; }

    public string Kind => Name.ToLowerInvariant() switch
    {
        "nvngx_dlss.dll" => "SR",
        "nvngx_dlssd.dll" => "RR",
        "nvngx_dlssg.dll" => "FG",
        "sl.interposer.dll" => "SL",
        _ => "?",
    };
}

public sealed class GameInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Root { get; set; } = "";
    /// <summary>Steam AppID from the game's appmanifest; null for other launchers and manual folders.</summary>
    public int? SteamAppId { get; set; }

    /// <summary>Executables, best candidate first.</summary>
    public List<string> Exes { get; set; } = [];
    public List<DlssDll> Dlss { get; set; } = [];
    /// <summary>Streamline runtime files (sl.*.dll) the game ships.</summary>
    public List<DlssDll> Streamline { get; set; } = [];
    public string? AntiCheat { get; set; }
    /// <summary>Graphics API, bitness, engine and non-NVIDIA upscaler signals from the game's files.</summary>
    public GameTech Tech { get; set; } = new();

    /// <summary>The user's hand-set detection values (mirrored from the game's settings, never saved in games.json).</summary>
    [JsonIgnore]
    public TechOverride? Manual { get; set; }

    private (GameTech Detected, TechOverride? Manual, GameTech Result)? _effective;

    /// <summary>
    /// What every consumer reads: <see cref="Tech"/> unless the user set a value. <see cref="Tech"/> itself stays what the scan found,
    /// so the game page can show "Auto (X)" and go back to it.
    /// </summary>
    [JsonIgnore]
    public GameTech Effective
    {
        get
        {
            if (_effective is { } e && ReferenceEquals(e.Detected, Tech) && ReferenceEquals(e.Manual, Manual)) return e.Result;
            var result = TechOverride.Apply(Tech, Manual);
            _effective = (Tech, Manual, result);
            return result;
        }
    }

    /// <summary>The exe treated as the game's main one: the user's pick while it is still on disk, else the best-ranked.</summary>
    [JsonIgnore]
    public string? MainExe => Manual?.MainExe is { } m && File.Exists(m) ? m : Exes.FirstOrDefault();
    /// <summary>Directories under the root holding a .dlssupdater manifest.</summary>
    public List<string> Installs { get; set; } = [];
    public DateTime Scanned { get; set; }

    public static string MakeId(string root) => Core.FileUtil.Normalize(root).ToLowerInvariant();

    [JsonIgnore]
    public GameFeatures Features => FeaturesOf(Dlss, Streamline);

    /// <summary>Newest DLSS Frame Generation dll version, if the game has one of its own.</summary>
    [JsonIgnore]
    public Version? FgVersion => Dlss.Where(d => d.Kind == "FG" && !d.Installed).Select(d => d.Version).Max();

    private static readonly (string File, GameFeatures Feature)[] FeatureFiles =
    [
        ("nvngx_dlss.dll", GameFeatures.SR), ("nvngx_dlssd.dll", GameFeatures.RR), ("nvngx_dlssg.dll", GameFeatures.FG),
        ("sl.dlss.dll", GameFeatures.SR), ("sl.dlss_d.dll", GameFeatures.RR), ("sl.dlss_g.dll", GameFeatures.FG),
        ("sl.reflex.dll", GameFeatures.Reflex), ("sl.interposer.dll", GameFeatures.Streamline),
    ];

    /// <summary>What the game's own files support; files DLSS5 Updater added don't count.</summary>
    public static GameFeatures FeaturesOf(IEnumerable<DlssDll> dlss, IEnumerable<DlssDll> streamline)
    {
        var f = GameFeatures.None;
        foreach (var d in dlss.Concat(streamline).Where(d => !d.Installed)) f |= FeatureOf(d);
        return f;
    }

    /// <summary>The feature one runtime file stands for, whoever put it there.</summary>
    public static GameFeatures FeatureOf(DlssDll d)
    {
        foreach (var (file, feature) in FeatureFiles)
            if (d.Name.Equals(file, StringComparison.OrdinalIgnoreCase)) return feature;
        return GameFeatures.None;
    }
}

/// <summary>A game folder reported by a launcher or the user, before inspection.</summary>
public sealed record GameEntry(string Name, string Root, string Source, int? SteamAppId = null);
