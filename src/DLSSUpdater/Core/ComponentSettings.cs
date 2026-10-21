using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

/// <summary>The choices for one component, app-wide or for one game. Null everywhere = follow the default.</summary>
public sealed class ComponentPick
{
    /// <summary>Build (library id or release tag); null follows the default, and the app default null follows the newest build.</summary>
    public string? Build { get; set; }
    public bool? Enabled { get; set; }
    /// <summary>Option id -> value, as the component defines them.</summary>
    public Dictionary<string, string>? Options { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    [JsonIgnore] public bool IsEmpty => Build is null && Enabled is null && (Options is null || Options.Count == 0) && (Unknown is null || Unknown.Count == 0);
}

/// <summary>
/// Build picks by component id. The components that had their own setting before keep it (and settings.json stays
/// readable by 2.2.x): their id maps to that field. Every other component is kept in the <c>Components</c> map.
/// </summary>
internal static class ComponentPicks
{
    public static ComponentPick? Get(Dictionary<string, ComponentPick>? map, string id) =>
        map?.FirstOrDefault(p => p.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Sets the build of <paramref name="id"/> in <paramref name="map"/> (created when needed); an emptied entry goes.</summary>
    public static Dictionary<string, ComponentPick>? SetBuild(Dictionary<string, ComponentPick>? map, string id, string? build)
    {
        var pick = Get(map, id);
        if (pick is null)
        {
            if (build is null) return map;
            map ??= new Dictionary<string, ComponentPick>(StringComparer.OrdinalIgnoreCase);
            map[id] = pick = new ComponentPick();
        }
        pick.Build = build;
        return Tidy(map, id, pick);
    }

    /// <summary>Sets one option of <paramref name="id"/> in <paramref name="map"/> (created when needed); null removes it, and an emptied entry goes.</summary>
    public static Dictionary<string, ComponentPick>? SetOption(Dictionary<string, ComponentPick>? map, string id, string option, string? value)
    {
        var pick = Get(map, id);
        if (pick is null)
        {
            if (value is null) return map;
            map ??= new Dictionary<string, ComponentPick>(StringComparer.OrdinalIgnoreCase);
            map[id] = pick = new ComponentPick();
        }
        if (value is null) pick.Options?.Remove(option);
        else (pick.Options ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))[option] = value;
        return Tidy(map, id, pick);
    }

    public static string? GetOption(Dictionary<string, ComponentPick>? map, string id, string option) =>
        Get(map, id)?.Options is { } o && o.FirstOrDefault(p => p.Key.Equals(option, StringComparison.OrdinalIgnoreCase)) is { Key: not null } p ? p.Value : null;

    private static Dictionary<string, ComponentPick>? Tidy(Dictionary<string, ComponentPick>? map, string id, ComponentPick pick)
    {
        if (pick.IsEmpty) map!.Remove(map!.Keys.First(k => k.Equals(id, StringComparison.OrdinalIgnoreCase)));
        return map is { Count: 0 } ? null : map;
    }

    /// <summary>Replaces the option values of <paramref name="id"/> in <paramref name="map"/> (created when needed); an emptied entry goes.</summary>
    public static Dictionary<string, ComponentPick>? SetOptions(Dictionary<string, ComponentPick>? map, string id, Dictionary<string, string>? options)
    {
        var pick = Get(map, id);
        if (pick is null)
        {
            if (options is not { Count: > 0 }) return map;
            map ??= new Dictionary<string, ComponentPick>(StringComparer.OrdinalIgnoreCase);
            map[id] = pick = new ComponentPick();
        }
        pick.Options = options is { Count: > 0 } ? options : null;
        if (pick.IsEmpty) map!.Remove(map!.Keys.First(k => k.Equals(id, StringComparison.OrdinalIgnoreCase)));
        return map is { Count: 0 } ? null : map;
    }
}

public sealed partial class GameOverride
{
    /// <summary>Component id -> this game's choices, for components without their own field above.</summary>
    public Dictionary<string, ComponentPick>? Components { get; set; }

    /// <summary>This game's build of a component; null follows the default.</summary>
    public string? BuildPick(string id) => id switch
    {
        ComponentRegistry.OptiScaler => OptiTag,
        ComponentRegistry.Uplift => UpliftTag,
        ComponentRegistry.DlssNr => DlssNrId,
        ComponentRegistry.RenoDx => RenoDxId,
        ComponentRegistry.Lecram => LecramId,
        ComponentRegistry.Dfc => DfcId,
        ComponentRegistry.Dlss => DlssTag,
        ComponentRegistry.ReShade => ReShadeId,
        _ => ComponentPicks.Get(Components, id)?.Build,
    };

    public void SetBuildPick(string id, string? build)
    {
        switch (id)
        {
            case ComponentRegistry.OptiScaler: OptiTag = build; break;
            case ComponentRegistry.Uplift: UpliftTag = build; break;
            case ComponentRegistry.DlssNr: DlssNrId = build; break;
            case ComponentRegistry.RenoDx: RenoDxId = build; break;
            case ComponentRegistry.Lecram: LecramId = build; break;
            case ComponentRegistry.Dfc: DfcId = build; break;
            case ComponentRegistry.Dlss: DlssTag = build; break;
            case ComponentRegistry.ReShade: ReShadeId = build; break;
            default: Components = ComponentPicks.SetBuild(Components, id, build); break;
        }
    }

    /// <summary>This game's choices for a component in the map (on/off, options); null when it has none.</summary>
    public ComponentPick? PickOf(string id) => ComponentPicks.Get(Components, id);

    /// <summary>The game's Unreal Extended switch for the RenoDX HDR component; null = the default (on unless the catalog opts the game out).</summary>
    public bool? HdrUeExtended => ComponentPicks.GetOption(Components, ComponentRegistry.RenoDxHdr, "ue-extended") switch { "on" => true, "off" => false, _ => null };

    public void SetHdrUeExtended(bool? on) =>
        Components = ComponentPicks.SetOption(Components, ComponentRegistry.RenoDxHdr, "ue-extended", on is null ? null : on.Value ? "on" : "off");

    /// <summary>The RenoDX HDR mod the user picked by hand for this game: a catalog mod id, a generic id, or <see cref="HdrManual.CustomMod"/> (an imported add-on, whose library id is the build pick); null = automatic.</summary>
    public string? HdrMod => ComponentPicks.GetOption(Components, ComponentRegistry.RenoDxHdr, "mod");

    public void SetHdrMod(string? mod) => Components = ComponentPicks.SetOption(Components, ComponentRegistry.RenoDxHdr, "mod", mod);

    /// <summary>Engine.ini location typed by the user for this game's HDR mod and Unreal frame cap; null = the catalog's hint, else the search.</summary>
    public string? EngineIniPath => ComponentPicks.GetOption(Components, ComponentRegistry.RenoDxHdr, "engine-ini");

    public void SetEngineIniPath(string? path) =>
        Components = ComponentPicks.SetOption(Components, ComponentRegistry.RenoDxHdr, "engine-ini", string.IsNullOrWhiteSpace(path) ? null : path.Trim());

    /// <summary>Replaces this game's option values for a component in the map; null or empty removes them.</summary>
    public void SetPickOptions(string id, Dictionary<string, string>? options) => Components = ComponentPicks.SetOptions(Components, id, options);
}

public sealed partial class AppSettings
{
    /// <summary>Component id -> the app-wide choices, for components without their own default field.</summary>
    public Dictionary<string, ComponentPick>? Components { get; set; }

    /// <summary>The default build of a component; null follows the newest build.</summary>
    public string? DefaultBuild(string id) => id switch
    {
        ComponentRegistry.OptiScaler => OptiTag,
        ComponentRegistry.Uplift => UpliftTag,
        ComponentRegistry.DlssNr => DlssNrId,
        ComponentRegistry.RenoDx => RenoDxId,
        ComponentRegistry.Lecram => LecramId,
        ComponentRegistry.Dfc => DfcId,
        ComponentRegistry.Dlss => DlssTag,
        ComponentRegistry.ReShade => ReShadeId,
        _ => ComponentPicks.Get(Components, id)?.Build,
    };

    /// <summary>Pins the default build of a map component (null = Newest). Components with their own field keep their own setters.</summary>
    public void SetDefaultBuild(string id, string? build)
    {
        if (ComponentRegistry.Find(id)?.Legacy == true)
            throw new InvalidOperationException($"{id} keeps its default in its own setting.");
        Components = ComponentPicks.SetBuild(Components, id, build);
    }

    /// <summary>The app-wide choices for a component in the map; null when there are none.</summary>
    public ComponentPick? PickOf(string id) => ComponentPicks.Get(Components, id);

    /// <summary>Peak brightness in nits written as ToneMapPeakNits into the RenoDX presets; null = leave the mods' own value.</summary>
    public int? HdrPeakNits { get; set; }

    /// <summary>Download ShortFuse's renodx-dlss add-on from the catalog (hash-pinned) next to importing it by hand.</summary>
    public bool AutoDownloadRenoDx { get; set; } = true;
}
