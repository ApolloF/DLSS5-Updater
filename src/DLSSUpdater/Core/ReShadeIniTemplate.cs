namespace DLSSUpdater.Core;

/// <summary>
/// The ReShade.ini every game starts from: a small file the user can edit (Settings → ReShade → Edit template) in the data
/// folder. Its keys are merged into a game's ReShade.ini when ReShade is installed or updated, through the same merge as every
/// other setting: a value the game's ini already has is kept (unless the game's "Existing configs" says apply), the keys this app
/// writes are recorded, and the file is backed up or removed on uninstall. Sections other components own (RenoDX, Lecram, Uplift,
/// Deep Fried Chicken) are ignored here.
/// </summary>
public static class ReShadeIniTemplate
{
    public const string DefaultText = """
        ; DLSS5 Updater merges these keys into the ReShade.ini of every game it installs or updates ReShade in.
        ; A key a game's ReShade.ini already has is kept (unless that game's "Existing configs" is set to apply the settings).
        ; Delete a line to stop managing that key. Sections of the add-ons (RenoDX, Lecram, Uplift, Deep Fried Chicken) are ignored.

        [ADDON]
        ; Generic Depth and Effect Runtime Sync are ReShade's own add-ons; they are off here because HDR add-ons don't need them.
        ; Remove this line to leave them on (depth-based shaders need Generic Depth).
        DisabledAddons=Generic Depth,Effect Runtime Sync

        [GENERAL]
        EffectSearchPaths=.\reshade-shaders\Shaders\**
        TextureSearchPaths=.\reshade-shaders\Textures\**

        [SCREENSHOT]
        SavePath=.\reshade-screenshots

        [INPUT]
        ; Key codes as ReShade writes them: virtual key, then Ctrl, Shift, Alt. 36 = Home, 44 = Print Screen.
        KeyOverlay=36,0,0,0
        KeyScreenshot=44,0,0,0

        [OVERLAY]
        TutorialProgress=4
        VariableListUseTabs=1
        """;

    /// <summary>What ReShade's own setup adds for a Vulkan game (depth buffer conventions of modern games).</summary>
    public static readonly IReadOnlyList<IniOverride> VulkanExtras =
    [
        new("GENERAL", "PreprocessorDefinitions",
            "RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000.0,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=0,RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0"),
    ];

    /// <summary>True for a section another component writes: not ours to seed.</summary>
    public static bool IsOwnedSection(string section)
    {
        var s = section.Trim();
        return s.StartsWith("renodx", StringComparison.OrdinalIgnoreCase)
               || s.Equals(LecramIni.Section, StringComparison.OrdinalIgnoreCase)
               || s.Equals(UpliftConfig.Section, StringComparison.OrdinalIgnoreCase)
               || s.Equals(DfcConfig.Section, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The template's keys, minus those of sections other components own. Comments and empty values are dropped.</summary>
    public static IReadOnlyList<IniOverride> Parse(string text) =>
        IniFile.Parse(text).Entries()
            .Where(e => e.Section.Length > 0 && e.Key.Length > 0 && e.Value.Length > 0 && !IsOwnedSection(e.Section))
            .Select(e => new IniOverride(e.Section, e.Key, e.Value))
            .ToList();

    /// <summary>The template file, written with the defaults the first time it is asked for.</summary>
    public static string EnsureFile()
    {
        var path = AppPaths.ReShadeTemplate;
        if (!File.Exists(path)) FileUtil.AtomicWriteText(path, DefaultText.ReplaceLineEndings("\r\n") + "\r\n");
        return path;
    }

    /// <summary>The user's template; the defaults when the file is missing or cannot be read.</summary>
    public static IReadOnlyList<IniOverride> Load()
    {
        try { return Parse(File.ReadAllText(EnsureFile())); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("ReShade.ini template unreadable, using the defaults", ex);
            return Parse(DefaultText);
        }
    }

    /// <summary>Puts the shipped defaults back.</summary>
    public static void Reset() => FileUtil.AtomicWriteText(AppPaths.ReShadeTemplate, DefaultText.ReplaceLineEndings("\r\n") + "\r\n");
}
