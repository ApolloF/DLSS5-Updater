using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

public sealed class GameOverride
{
    public string? TargetDir { get; set; }
    public string? Proxy { get; set; }
    /// <summary>DLSS release tag pinned for this game; null follows the global choice.</summary>
    public string? DlssTag { get; set; }
    public string? DlssNrId { get; set; }
    /// <summary>ShortFuse add-on build (library id) pinned for this game; null follows the global choice.</summary>
    public string? RenoDxId { get; set; }
    /// <summary>Lecram add-on build (library id) pinned for this game; null follows the global choice.</summary>
    public string? LecramId { get; set; }
    /// <summary>Deep Fried Chicken in a game without its own DLSS: also place nvngx_dlssg.dll for its DLSS-G frame generation.</summary>
    public bool? DfcFrameGen { get; set; }
    /// <summary>Config preset used for this game; null uses the current settings.</summary>
    public string? Preset { get; set; }
    /// <summary>OptiScaler or ReShade-only for this game; null follows the default.</summary>
    public InstallMode? Mode { get; set; }
    /// <summary>OptiScaler.ini changes for this game on top of its base (preset or current settings); null/empty inherits everything.</summary>
    public List<IniOverride>? Opti { get; set; }
    /// <summary>Same for ReShade.ini.</summary>
    public List<IniOverride>? ReShade { get; set; }
    /// <summary>keep | apply | fresh for this game; null follows the global choice.</summary>
    public string? IniMode { get; set; }
    /// <summary>Presets that only this game can use.</summary>
    public List<ConfigPreset>? Presets { get; set; }
    /// <summary>Fields this version doesn't know (written by a newer one); kept so saving doesn't drop them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>A named snapshot of the OptiScaler.ini and ReShade.ini settings (incl. keybinds and options).</summary>
public sealed class ConfigPreset
{
    public string Name { get; set; } = "";
    public List<IniOverride> Opti { get; set; } = [];
    public List<IniOverride> ReShade { get; set; } = [];
    /// <summary>Shipped with the app (the Recommended preset): can be loaded and assigned, not saved over or deleted.</summary>
    [JsonIgnore] public bool BuiltIn { get; init; }
}

public sealed class AppSettings
{
    public static readonly string[] ProxyNames =
        ["dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", "d3d12.dll", "wininet.dll", "winhttp.dll"];

    public List<string> ManualGames { get; set; } = [];
    public List<string> LibraryRoots { get; set; } = [];
    public List<string> HiddenGames { get; set; } = [];
    public Dictionary<string, GameOverride> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names ReShade loads under when it is the proxy itself (ReShade-only mode).</summary>
    public static readonly string[] ReShadeProxyNames =
        ["dxgi.dll", "d3d12.dll", "d3d11.dll", "d3d9.dll", "dinput8.dll", "opengl32.dll"];

    /// <summary>A proxy name DLSS Updater would ever install under (OptiScaler or ReShade-only).</summary>
    public static bool IsKnownProxy(string? name) =>
        name is not null && (ProxyNames.Contains(name, StringComparer.OrdinalIgnoreCase) || ReShadeProxyNames.Contains(name, StringComparer.OrdinalIgnoreCase));

    public string DefaultProxy { get; set; } = "dxgi.dll";
    /// <summary>What a new install puts in: OptiScaler-NR (the main purpose) or ReShade with add-ons only.</summary>
    public InstallMode DefaultMode { get; set; } = InstallMode.OptiScaler;
    public bool IncludePrereleases { get; set; } = true;
    public bool InstallReShade { get; set; } = true;
    /// <summary>Download the ReShade add-on build from reshade.me when no ReShade64.dll was imported.</summary>
    public bool AutoDownloadReShade { get; set; } = true;
    /// <summary>Check Lecram's public MediaFire channel for new v11 builds and add them to the library.</summary>
    public bool AutoDownloadLecram { get; set; } = true;
    public string LecramFolderKey { get; set; } = MediaFireClient.DefaultFolder;
    public string LecramLastResult { get; set; } = "Not checked yet.";
    public string? LecramLastVersion { get; set; }
    public string? LecramLastAssetHash { get; set; }
    public string? LecramLastBuildId { get; set; }
    public bool InstallMfgUnlock { get; set; } = true;
    public bool InstallDlssNr { get; set; } = true;
    public bool InstallRenoDx { get; set; }
    public bool InstallUplift { get; set; }
    public bool InstallLecram { get; set; }
    public string OptiSource { get; set; } = ComponentStore.OptiRepo;
    public string? PreviousDlssNrId { get; set; }
    public bool AddMissingDlss { get; set; } = true;
    /// <summary>Legacy (1.0/1.1); false maps to IniMode "fresh".</summary>
    public bool CarryOverGameIni { get; set; } = true;
    /// <summary>keep | apply | fresh — how existing game configs are treated.</summary>
    public string IniMode { get; set; } = "keep";
    /// <summary>GitHub token; settings.json holds it only encrypted for this Windows user (DPAPI).</summary>
    [JsonIgnore]
    public string? GitHubToken
    {
        get => Unprotect(GitHubTokenProtected);
        set => GitHubTokenProtected = Protect(value);
    }
    public string? GitHubTokenProtected { get; set; }

    /// <summary>The plain-text token 1.5.1 and earlier saved: read once, then saved encrypted, never written.</summary>
    [JsonPropertyName("GitHubToken")]
    public string? LegacyGitHubToken
    {
        get => null;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            GitHubToken = value;
            _legacyTokenRead = true;
        }
    }

    private bool _legacyTokenRead;
    public List<IniOverride> IniOverrides { get; set; } = ConfigProfile.Defaults();
    public List<IniOverride> ReShadeOverrides { get; set; } = ConfigProfile.ReShadeDefaults();
    public bool InstallStreamline { get; set; }
    public List<ConfigPreset> Presets { get; set; } = [];
    /// <summary>Component -> newest release tag whose notes the user has seen (or that was current when first checked).</summary>
    public Dictionary<string, string> SeenReleases { get; set; } = [];
    /// <summary>Component -> newest release tag already announced in the log.</summary>
    public Dictionary<string, string> AnnouncedReleases { get; set; } = [];
    /// <summary>Fields this version doesn't know (written by a newer one); kept so saving doesn't drop them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>Built-in presets first, then the user's.</summary>
    [JsonIgnore]
    public IEnumerable<ConfigPreset> AllPresets => [ConfigProfile.RecommendedPreset(), .. Presets];

    /// <summary>The game's chosen preset; its own game-only presets are looked at before the global and built-in ones.</summary>
    public ConfigPreset? PresetFor(string gameId) =>
        Games.GetValueOrDefault(gameId) is { Preset: { } name } g
            ? g.Presets?.FirstOrDefault(p => p.Name == name) ?? AllPresets.FirstOrDefault(p => p.Name == name)
            : null;
    /// <summary>DLSS release tag to install; null = latest.</summary>
    public string? DlssTag { get; set; }
    public string? DlssNrId { get; set; }
    /// <summary>Newest NR library build already offered as the default (see <see cref="ComponentStore.AdoptNewerDlssNr"/>).</summary>
    public string? NewestDlssNrSeen { get; set; }
    /// <summary>Default ShortFuse add-on build (library id); games without their own choice install it.</summary>
    public string? RenoDxId { get; set; }
    /// <summary>Default Lecram add-on build (library id).</summary>
    public string? LecramId { get; set; }
    public List<AddonPairTest> AddonPairTests { get; set; } = [];

    public string? AddonId(AddonKind kind) => kind == AddonKind.RenoDx ? RenoDxId : LecramId;

    public void SetAddonId(AddonKind kind, string? id)
    {
        if (kind == AddonKind.RenoDx) RenoDxId = id;
        else LecramId = id;
    }

    public void SelectDlssNr(string id)
    {
        if (DlssNrId == id) return;
        PreviousDlssNrId = DlssNrId;
        DlssNrId = id;
    }

    public GameOverride For(string gameId)
    {
        if (!Games.TryGetValue(gameId, out var o)) Games[gameId] = o = new GameOverride();
        return o;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.SettingsFile), JsonCtx.Default.AppSettings);
                if (s is not null)
                {
                    s.Repair();
                    if (!s.CarryOverGameIni && s.IniMode == "keep") s.IniMode = "fresh";
                    s.CarryOverGameIni = true;
                    var sourceMigrated = s.OptiSource == ComponentStore.LegacyOptiRepo;
                    if (sourceMigrated) s.OptiSource = ComponentStore.OptiRepo;
                    s.Presets.RemoveAll(p => p.Name.Equals(ConfigProfile.RecommendedName, StringComparison.OrdinalIgnoreCase));
                    var renoDxMigrated = RenoDxIni.Migrate(s);
                    if (s._legacyTokenRead || sourceMigrated || renoDxMigrated) s.Save();
                    return s;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("settings.json unreadable, using defaults", ex);
            KeepUnreadable();
        }
        return new AppSettings();
    }

    internal static string BadSettingsFile => AppPaths.SettingsFile + ".bad";

    /// <summary>Fields older versions of this app wrote and that nothing reads anymore; dropped instead of kept as unknown.</summary>
    private static readonly string[] RetiredKeys = ["AddonCombos", "TestedComboSeeded"];

    /// <summary>The next save overwrites settings.json with defaults; one copy of the unreadable file survives for repair.</summary>
    private static void KeepUnreadable()
    {
        try
        {
            File.Copy(AppPaths.SettingsFile, BadSettingsFile, true);
            Log.Info($"Kept a copy of the unreadable settings as {BadSettingsFile}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not keep a copy of the unreadable settings.json", ex);
        }
    }

    /// <summary>
    /// A hand-edited or damaged settings.json may hold null for any collection or text; the app expects them set.
    /// Nulls become the defaults, null items are dropped, and game ids match case-insensitively.
    /// </summary>
    private void Repair()
    {
        var defaults = new AppSettings();
        if (Unknown is not null)
        {
            foreach (var key in Unknown.Keys.Where(k => RetiredKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) Unknown.Remove(key);
            if (Unknown.Count == 0) Unknown = null;
        }
        ManualGames = Strings(ManualGames);
        LibraryRoots = Strings(LibraryRoots);
        HiddenGames = Strings(HiddenGames);
        Games = new Dictionary<string, GameOverride>((Games ?? []).Where(g => g.Value is not null), StringComparer.OrdinalIgnoreCase);
        foreach (var g in Games.Values)
        {
            if (g.Opti is not null) g.Opti = Overrides(g.Opti);
            if (g.ReShade is not null) g.ReShade = Overrides(g.ReShade);
            if (g.Presets is not null) g.Presets = PresetList(g.Presets);
        }
        IniOverrides = IniOverrides is null ? defaults.IniOverrides : Overrides(IniOverrides);
        ReShadeOverrides = ReShadeOverrides is null ? defaults.ReShadeOverrides : Overrides(ReShadeOverrides);
        Presets = PresetList(Presets);
        SeenReleases ??= [];
        AnnouncedReleases ??= [];
        AddonPairTests = (AddonPairTests ?? []).Where(t => t is not null).ToList();
        DefaultProxy ??= defaults.DefaultProxy;
        IniMode ??= defaults.IniMode;
        OptiSource ??= defaults.OptiSource;
        LecramFolderKey ??= defaults.LecramFolderKey;
        LecramLastResult ??= defaults.LecramLastResult;

        static List<string> Strings(List<string>? list) => (list ?? []).Where(s => s is not null).ToList();

        static List<IniOverride> Overrides(List<IniOverride> list) =>
            list.Where(o => o is not null).Select(o =>
            {
                o.Section ??= "";
                o.Key ??= "";
                o.Value ??= "";
                return o;
            }).ToList();

        static List<ConfigPreset> PresetList(List<ConfigPreset>? list) =>
            (list ?? []).Where(p => p is not null).Select(p =>
            {
                p.Name ??= "";
                p.Opti = Overrides(p.Opti ?? []);
                p.ReShade = Overrides(p.ReShade ?? []);
                return p;
            }).ToList();
    }

    public void Save()
    {
        try { FileUtil.AtomicWriteText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonCtx.Default.AppSettings)); }
        catch (IOException ex) { Log.Error("Could not save settings", ex); }
    }

    private static string? Protect(string? secret) =>
        string.IsNullOrEmpty(secret)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Encrypted by another Windows user or PC: the token has to be entered again.
            Log.Error("Saved GitHub token can't be decrypted on this account", ex);
            return null;
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(InstallManifest))]
[JsonSerializable(typeof(List<GameInfo>))]
[JsonSerializable(typeof(ApiCacheEntry))]
[JsonSerializable(typeof(MediaFireCacheEntry))]
[JsonSerializable(typeof(List<GhRelease>))]
[JsonSerializable(typeof(GhRelease))]
[JsonSerializable(typeof(CachedComponent))]
[JsonSerializable(typeof(NrVersion))]
[JsonSerializable(typeof(AddonBuild))]
[JsonSerializable(typeof(AddonPairTest))]
internal partial class JsonCtx : JsonSerializerContext;
