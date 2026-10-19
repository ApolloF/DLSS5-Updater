using DLSSUpdater.Addon;

namespace DLSSUpdater.Tests;

public sealed class AddonRegistrationTests
{
    [Fact]
    public void IsThisExe_MatchesSamePath()
    {
        Assert.True(AddonRegistration.IsThisExe(@"C:\Apps\DLSS5Updater.exe", @"c:\apps\dlss5updater.exe"));
    }

    [Fact]
    public void IsThisExe_MatchesOldExeNameInSameFolder()
    {
        Assert.True(AddonRegistration.IsThisExe(@"C:\Apps\DLSSUpdater.exe", @"C:\Apps\DLSS5Updater.exe"));
    }

    [Fact]
    public void IsThisExe_RejectsOtherFolderOrOtherName()
    {
        Assert.False(AddonRegistration.IsThisExe(@"D:\Other\DLSSUpdater.exe", @"C:\Apps\DLSS5Updater.exe"));
        Assert.False(AddonRegistration.IsThisExe(@"C:\Apps\Other.exe", @"C:\Apps\DLSS5Updater.exe"));
    }
}
