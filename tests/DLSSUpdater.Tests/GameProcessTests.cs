using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public sealed class GameProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dlssu-proc-" + Guid.NewGuid().ToString("N"));
    private readonly string _win64;
    private readonly string _exeDir;

    public GameProcessTests()
    {
        _win64 = Path.Combine(_root, @"ProjectPBZ\Binaries\Win64");
        _exeDir = Path.Combine(_root, @"platform\steam");
        Directory.CreateDirectory(_win64);
        Directory.CreateDirectory(_exeDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void ReShadeLog(string dllDir, string exeDir) => File.WriteAllText(Path.Combine(_win64, "ReShade.log"),
        $"16:45:18:517 [34656] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from '{dllDir}\\dxgi.dll' into '{exeDir}\\PBZ.exe' (0xF3D09057) ...\r\n" +
        "16:45:18:600 [34656] | INFO  | Registering hooks for 'user32.dll' ...\r\n");

    [Fact]
    public void ReShadeLog_NamesTheExeFolder()
    {
        ReShadeLog(_win64, _exeDir);
        Assert.Equal(FileUtil.Normalize(_exeDir), GameProcess.ExeDirFromLogs(_win64));
    }

    [Fact]
    public void OptiScalerLog_NamesTheExeFolder()
    {
        File.WriteAllText(Path.Combine(_win64, "OptiScaler.log"),
            "[16:18:53.177391] [W] OptiScaler v0.1.39-pre1 (956bb719) (20261121_152542) loaded\r\n" +
            $"[16:18:53.177478] [I] Setting DllPath to {_exeDir}\r\n");
        Assert.Equal(FileUtil.Normalize(_exeDir), GameProcess.ExeDirFromLogs(_win64));
    }

    [Fact]
    public void ReShadeLogFromAnotherFolder_IsNotTrusted()
    {
        // A ReShade.log written by a ReShade that lives elsewhere says nothing about the proxy in this folder.
        ReShadeLog(_exeDir, _exeDir);
        Assert.Null(GameProcess.ExeDirFromLogs(_win64));
    }

    [Fact]
    public void NoLogs_NoWarning() => Assert.Null(GameProcess.OptiScalerFolderWarning(_win64));

    [Fact]
    public void ExeInTheInstallFolder_NoWarning()
    {
        ReShadeLog(_win64, _win64);
        Assert.Null(GameProcess.OptiScalerFolderWarning(_win64));
    }

    [Fact]
    public void ExeElsewhere_WarnsAndNamesTheFolder()
    {
        ReShadeLog(_win64, _exeDir);
        var warning = GameProcess.OptiScalerFolderWarning(_win64, _root);
        Assert.NotNull(warning);
        Assert.Contains(@"platform\steam", warning);
    }

    [Fact]
    public void Conflicts_FlagOptiScalerModeOnly()
    {
        ReShadeLog(_win64, _exeDir);
        var opti = new InstallOptions { Opti = true, ReShade = true, Proxy = "dxgi.dll" };
        Assert.Contains(Installer.Conflicts(_win64, opti), c => c.Contains("OptiScaler loads ReShade"));
        Assert.DoesNotContain(Installer.Conflicts(_win64, new InstallOptions { Mode = InstallMode.ReShadeOnly, ReShade = true, Proxy = "dxgi.dll" }),
            c => c.Contains("OptiScaler loads ReShade"));
    }
}
