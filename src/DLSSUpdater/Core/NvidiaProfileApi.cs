namespace DLSSUpdater.Core;

/// <summary>
/// The driver-settings calls the app makes, as an interface so everything above <see cref="NvDrs"/> can be tested against a fake
/// and tests never touch a real NVIDIA profile. Profiles are opaque handles of the session that returned them.
/// </summary>
public interface IDrsSession : IDisposable
{
    IntPtr BaseProfile();
    /// <summary>The profile the driver applies to this exe, or null.</summary>
    IntPtr? FindProfileForExe(string exePath);
    IntPtr GetOrCreateProfileForExe(string exePath, string name);
    string ProfileName(IntPtr profile);
    /// <summary>The setting as seen from this profile, or null when neither the profile nor the driver defines it.</summary>
    NvDrs.Setting? Read(IntPtr profile, uint id);
    /// <summary>The value this profile itself holds (not NVIDIA's shipped one), or null.</summary>
    uint? Get(IntPtr profile, uint id);
    void Set(IntPtr profile, uint id, uint value);
    /// <summary>Removes the profile's own value; deleting a value that isn't there is fine.</summary>
    void Delete(IntPtr profile, uint id);
    void Save();
}

/// <summary>Opens driver-settings sessions; <see cref="NvDrsProvider"/> is the real one.</summary>
public interface IDrsProvider
{
    bool Available { get; }
    IDrsSession Open();
}

public sealed class NvDrsProvider : IDrsProvider
{
    public bool Available => NvDrs.Available;
    public IDrsSession Open() => NvDrs.Open();
}
