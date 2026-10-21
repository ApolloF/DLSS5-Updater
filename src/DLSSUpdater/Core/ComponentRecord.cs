using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

/// <summary>
/// One component installed in a game, as the install record (<see cref="InstallManifest.Components"/>) keeps it: what it
/// placed, what it displaced, which config keys and file attributes it changed. Removing it undoes exactly that, in
/// reverse order (<see cref="Installer.RemoveComponentAsync"/>). Paths are entries as in <see cref="InstallManifest.Files"/>;
/// config files outside the game may also start with a known-folder tag (<see cref="InstallManifest.KnownFolderTags"/>).
/// </summary>
public sealed class InstalledComponent
{
    /// <summary>Stable id from <see cref="ComponentRegistry"/>.</summary>
    public string Id { get; set; } = "";
    /// <summary>Library id or release tag of the build placed; null when the component has no builds.</summary>
    public string? BuildId { get; set; }
    /// <summary>Version as shown to the user.</summary>
    public string? Version { get; set; }
    /// <summary>Files we placed, in the order we placed them, with the SHA-256 of what we put there.</summary>
    public List<PlacedFile> Files { get; set; } = [];
    /// <summary>Original entry -> backup (relative to the backup folder) of the files ours replaced. Also in <see cref="InstallManifest.Backups"/>, which restores them.</summary>
    public Dictionary<string, string> Backups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Config keys we set, in the order we set them.</summary>
    public List<ConfigEdit> ConfigEdits { get; set; } = [];
    /// <summary>Files whose attributes we changed (an Engine.ini made read-only, for example).</summary>
    public List<AttributeChange> Attributes { get; set; } = [];
    /// <summary>Whatever else the component needs to remember about this install.</summary>
    public Dictionary<string, string> Extras { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime Installed { get; set; }

    /// <summary>
    /// Built from the per-component fields of a record that stores this component the old way (see
    /// <see cref="LegacyComponents"/>); never saved, and removed by the installer's own code for it.
    /// </summary>
    [JsonIgnore] public bool Legacy { get; init; }

    /// <summary>Fields this version doesn't know (written by a newer one); kept so saving doesn't drop them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    public PlacedFile? FileAt(string entry) => Files.FirstOrDefault(f => f.Path.Equals(entry, StringComparison.OrdinalIgnoreCase));

    public ConfigEdit? EditOf(string file, string section, string key) =>
        ConfigEdits.FirstOrDefault(e => e.File.Equals(file, StringComparison.OrdinalIgnoreCase)
                                        && e.Section.Equals(section, StringComparison.OrdinalIgnoreCase)
                                        && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public AttributeChange? AttributesOf(string entry) =>
        Attributes.FirstOrDefault(a => a.Path.Equals(entry, StringComparison.OrdinalIgnoreCase));
}

public sealed class PlacedFile
{
    public string Path { get; set; } = "";
    /// <summary>SHA-256 of the file we placed; null when it isn't known (records from before it was kept).</summary>
    public string? Sha256 { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>One key we set in an ini-style config file.</summary>
public sealed class ConfigEdit
{
    public string File { get; set; } = "";
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    /// <summary>The value we wrote.</summary>
    public string Value { get; set; } = "";
    /// <summary>The value the key had before our first change; null = the key was absent.</summary>
    public string? Previous { get; set; }
    /// <summary>The file did not exist before; it goes again when no key is left in it.</summary>
    public bool FileCreated { get; set; }
    /// <summary>The previous value was never recorded (legacy records); undo can't put it back.</summary>
    [JsonIgnore] public bool PreviousUnknown { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>File attributes we changed; only <see cref="Managed"/> bits are recorded and restored.</summary>
public sealed class AttributeChange
{
    public const FileAttributes Managed = FileAttributes.ReadOnly | FileAttributes.Hidden;

    public string Path { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter<FileAttributes>))]
    public FileAttributes Before { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<FileAttributes>))]
    public FileAttributes After { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>
/// What a component wants in a game: the whole set, not a change. <see cref="Installer.InstallComponentAsync"/> brings the
/// game there from whatever the record says this component has now, and records every step before it is taken.
/// Paths are full paths in the game, the install folder or (config files only) a known folder.
/// </summary>
public sealed record ComponentPlan(string Id)
{
    public string? BuildId { get; init; }
    public string? Version { get; init; }
    /// <summary>Source file -> destination.</summary>
    public IReadOnlyList<(string Source, string Dest)> Files { get; init; } = [];
    public IReadOnlyList<(string File, string Section, string Key, string Value)> ConfigEdits { get; init; } = [];
    /// <summary>Files to lock read-only after the edits (e.g. an Engine.ini the game would otherwise rewrite).</summary>
    public IReadOnlyList<string> ReadOnly { get; init; } = [];
    public IReadOnlyDictionary<string, string> Extras { get; init; } = new Dictionary<string, string>();
}

/// <summary>What an undo left alone because it changed since we made it; the rest was undone.</summary>
public sealed record ComponentUndo(string Id, IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> ChangedKeys, IReadOnlyList<string> ChangedAttributes)
{
    public bool Clean => ChangedFiles.Count == 0 && ChangedKeys.Count == 0 && ChangedAttributes.Count == 0;
}
