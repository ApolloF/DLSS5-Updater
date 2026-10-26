namespace DLSSUpdater.Core;

/// <summary>
/// What a full uninstall puts back in the NVIDIA driver profile: the present-method values DXVK set and the frame limit the limiter
/// service wrote (including the driver cap it noted in the game's settings). Both are no-ops when nothing was recorded, so it is safe
/// to run again after a partly failed uninstall.
/// </summary>
public static class UninstallDriverRestore
{
    public static void Run(NvidiaProfile nvidia, NvidiaGame game, Action saveSettings)
    {
        if (!nvidia.Available) return;
        nvidia.RestoreLiliumPresentMethod(game);
        nvidia.RestoreFrameRateLimit(game);
        FrameLimitSettings.SetDriverCap(game.Settings, null);
        saveSettings();
    }
}
