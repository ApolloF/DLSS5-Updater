using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// An in-memory NVIDIA driver: never loads nvapi, so no test can touch a real profile. Writes stay in the session until Save;
/// a session that is disposed without Save drops them, like the real DRS.
/// </summary>
internal sealed class FakeDrs : IDrsProvider
{
    public const string Exe = @"C:\Games\Game\game.exe";
    private const int BaseHandle = 1;

    /// <summary>Ids this fake driver defines (with their default); anything else reads as unknown.</summary>
    public Dictionary<uint, uint> Defined { get; } = [];
    /// <summary>Values of the global (base) profile.</summary>
    public Dictionary<uint, uint> Global { get; } = [];
    /// <summary>exe file name -> profile.</summary>
    public Dictionary<string, FakeProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Saves { get; private set; }
    public int Opens { get; private set; }
    public Func<uint, uint, bool>? FailSet { get; set; }
    public bool Available { get; set; } = true;

    public sealed class FakeProfile(string name)
    {
        public string Name { get; } = name;
        public Dictionary<uint, uint> Values { get; set; } = [];
        public Dictionary<uint, uint> Predefined { get; } = [];
    }

    public FakeDrs Define(params uint[] ids)
    {
        foreach (var id in ids) Defined[id] = 0;
        return this;
    }

    public FakeProfile AddProfile(string exe, string name = "Game")
    {
        var p = new FakeProfile(name);
        Profiles[Path.GetFileName(exe)] = p;
        return p;
    }

    public FakeProfile? Profile(string exe = Exe) => Profiles.GetValueOrDefault(Path.GetFileName(exe));

    public uint? Value(uint id, string exe = Exe) => Profile(exe)?.Values.TryGetValue(id, out var v) == true ? v : null;

    public IDrsSession Open()
    {
        Opens++;
        return new Session(this);
    }

    private sealed class Session : IDrsSession
    {
        private readonly FakeDrs _d;
        private readonly Dictionary<string, FakeProfile> _work;
        private readonly List<FakeProfile> _handles = [];

        public Session(FakeDrs d)
        {
            _d = d;
            _work = d.Profiles.ToDictionary(p => p.Key, p => Copy(p.Value), StringComparer.OrdinalIgnoreCase);
        }

        private static FakeProfile Copy(FakeProfile p)
        {
            var c = new FakeProfile(p.Name) { Values = new Dictionary<uint, uint>(p.Values) };
            foreach (var (k, v) in p.Predefined) c.Predefined[k] = v;
            return c;
        }

        private IntPtr Handle(FakeProfile p)
        {
            var i = _handles.IndexOf(p);
            if (i < 0) { _handles.Add(p); i = _handles.Count - 1; }
            return i + 2;
        }

        private FakeProfile At(IntPtr h) => _handles[(int)h - 2];

        public IntPtr BaseProfile() => BaseHandle;

        public IntPtr? FindProfileForExe(string exePath) =>
            _work.GetValueOrDefault(Path.GetFileName(exePath)) is { } p ? Handle(p) : null;

        public IntPtr GetOrCreateProfileForExe(string exePath, string name)
        {
            var key = Path.GetFileName(exePath);
            if (!_work.TryGetValue(key, out var p)) _work[key] = p = new FakeProfile(name);
            return Handle(p);
        }

        public string ProfileName(IntPtr profile) => At(profile).Name;

        public NvDrs.Setting? Read(IntPtr profile, uint id)
        {
            if (profile != BaseHandle)
            {
                var p = At(profile);
                if (p.Values.TryGetValue(id, out var own)) return new NvDrs.Setting(own, false, 0);
                if (p.Predefined.TryGetValue(id, out var pre)) return new NvDrs.Setting(pre, true, 0);
            }
            if (_d.Global.TryGetValue(id, out var g)) return new NvDrs.Setting(g, false, profile == BaseHandle ? 0 : 1);
            return _d.Defined.TryGetValue(id, out var def) ? new NvDrs.Setting(def, true, 3) : null;
        }

        public uint? Get(IntPtr profile, uint id) => Read(profile, id) is { IsUserValue: true } s ? s.Value : null;

        public void Set(IntPtr profile, uint id, uint value)
        {
            if (_d.FailSet?.Invoke(id, value) == true) throw new NvApiException(-1, $"SetSetting 0x{id:X8}");
            At(profile).Values[id] = value;
        }

        public void Delete(IntPtr profile, uint id) => At(profile).Values.Remove(id);

        public void Save()
        {
            _d.Profiles.Clear();
            foreach (var (k, v) in _work) _d.Profiles[k] = v;
            _d.Saves++;
        }

        public void Dispose() { }
    }
}

public class NvidiaProfileTests
{
    private const uint Fps = NvidiaProfileCatalog.FrameRateLimit, SrPreset = NvidiaProfileCatalog.SrPreset, Vsync = NvidiaProfileCatalog.VsyncMode;

    private static (NvidiaProfile Profile, FakeDrs Drs, NvidiaGame Game) Setup(Action<FakeDrs>? configure = null)
    {
        var drs = new FakeDrs();
        drs.Define(Fps, SrPreset, NvidiaProfileCatalog.SrOverride, Vsync, NvidiaProfileCatalog.VsyncTear);
        configure?.Invoke(drs);
        return (new NvidiaProfile(drs), drs, new NvidiaGame(FakeDrs.Exe, "Game", new GameOverride()));
    }

    private static Dictionary<uint, uint?> Changes(params (uint Id, uint? Value)[] c) => c.ToDictionary(x => x.Id, x => x.Value);

    [Fact]
    public void Apply_CreatesTheProfile_WritesAndReadsBack()
    {
        var (nv, drs, game) = Setup();

        var result = nv.Apply(game, Changes((Fps, 117), (SrPreset, 0x0B)));

        Assert.True(result.Verified);
        Assert.Equal(117u, drs.Value(Fps));
        Assert.Equal(0x0Bu, drs.Value(SrPreset));
        Assert.Equal(1, drs.Saves);
        Assert.Equal("Game", drs.Profile()!.Name);
    }

    [Fact]
    public void Apply_RecordsAbsentAndPreviousValues()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 60;

        nv.Apply(game, Changes((Fps, 117), (SrPreset, 0x0B)));

        var record = NvidiaRecord.Load(game.Settings);
        Assert.Equal(60u, record.Previous[Fps]);
        Assert.Null(record.Previous[SrPreset]);
        Assert.True(record.Previous.ContainsKey(SrPreset));
    }

    [Fact]
    public void Apply_KeepsTheFirstPreviousValue_WhenWrittenAgain()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 60;

        nv.Apply(game, Changes((Fps, 117)));
        nv.Apply(game, Changes((Fps, 144)));

        Assert.Equal(144u, drs.Value(Fps));
        Assert.Equal(60u, NvidiaRecord.Load(game.Settings).Previous[Fps]);
    }

    [Fact]
    public void Apply_AValueThatIsNvidiasShippedOne_IsRecordedAsAbsent()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Predefined[Fps] = 90;

        nv.Apply(game, Changes((Fps, 117)));

        Assert.True(NvidiaRecord.Load(game.Settings).Previous.ContainsKey(Fps));
        Assert.Null(NvidiaRecord.Load(game.Settings).Previous[Fps]);
    }

    [Fact]
    public void Apply_ARemoval_DeletesTheOwnValue_AndIsRecorded()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 60;

        var result = nv.Apply(game, Changes((Fps, null)));

        Assert.True(result.Verified);
        Assert.Null(drs.Value(Fps));
        Assert.Equal(60u, NvidiaRecord.Load(game.Settings).Previous[Fps]);
    }

    [Fact]
    public void Apply_ReportsWhatTheDriverDidNotKeep()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe);
        drs.FailSet = null;
        // A driver that silently clamps the value.
        var clamping = new ClampingDrs(drs, Fps, 100);
        var result = new NvidiaProfile(clamping).Apply(game, Changes((Fps, 117)));

        Assert.False(result.Verified);
        Assert.Equal([(Fps, (uint?)117, (uint?)100)], result.Mismatches);
    }

    private sealed class ClampingDrs(FakeDrs inner, uint id, uint max) : IDrsProvider
    {
        public bool Available => true;
        public IDrsSession Open() => new Clamp(inner.Open(), id, max);

        private sealed class Clamp(IDrsSession s, uint id, uint max) : IDrsSession
        {
            public IntPtr BaseProfile() => s.BaseProfile();
            public IntPtr? FindProfileForExe(string e) => s.FindProfileForExe(e);
            public IntPtr GetOrCreateProfileForExe(string e, string n) => s.GetOrCreateProfileForExe(e, n);
            public string ProfileName(IntPtr p) => s.ProfileName(p);
            public NvDrs.Setting? Read(IntPtr p, uint i) => s.Read(p, i);
            public uint? Get(IntPtr p, uint i) => s.Get(p, i);
            public void Set(IntPtr p, uint i, uint v) => s.Set(p, i, i == id ? Math.Min(v, max) : v);
            public void Delete(IntPtr p, uint i) => s.Delete(p, i);
            public void Save() => s.Save();
            public void Dispose() => s.Dispose();
        }
    }

    [Fact]
    public void Apply_WhenTheDriverRefuses_LeavesTheDriverAndTheRecordUntouched()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 60;
        drs.FailSet = (id, _) => id == SrPreset;

        Assert.Throws<NvApiException>(() => nv.Apply(game, Changes((Fps, 117), (SrPreset, 0x0B))));

        Assert.Equal(60u, drs.Value(Fps));
        Assert.Equal(0, drs.Saves);
        Assert.Null(game.Settings.Components);
    }

    [Fact]
    public void Reset_RestoresAbsentAndPreviousValues_AndForgetsTheRecord()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 60;
        nv.Apply(game, Changes((Fps, 117), (SrPreset, 0x0B)));

        var result = nv.Reset(game);

        Assert.True(result.Verified);
        Assert.Equal(60u, drs.Value(Fps));
        Assert.Null(drs.Value(SrPreset));
        Assert.False(drs.Profile()!.Values.ContainsKey(SrPreset));
        Assert.True(NvidiaRecord.Load(game.Settings).IsEmpty);
        Assert.Null(game.Settings.Components);
    }

    [Fact]
    public void Reset_AfterARemoval_PutsTheRemovedValueBack()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Vsync] = 0x47814940;
        nv.Apply(game, Changes((Vsync, null)));
        Assert.Null(drs.Value(Vsync));

        nv.Reset(game);

        Assert.Equal(0x47814940u, drs.Value(Vsync));
    }

    [Fact]
    public void Reset_WithNothingRecorded_DoesNotOpenTheDriver()
    {
        var (nv, drs, game) = Setup();

        var result = nv.Reset(game);

        Assert.True(result.Verified);
        Assert.Equal(0, drs.Opens);
    }

    [Fact]
    public void Reset_WhenTheProfileIsGone_DoesNotCreateOne_AndClearsTheRecord()
    {
        var (nv, drs, game) = Setup();
        var record = new NvidiaRecord();
        record.Previous[Fps] = null;
        record.Save(game.Settings);

        nv.Reset(game);

        Assert.Empty(drs.Profiles);
        Assert.Equal(0, drs.Saves);
        Assert.True(NvidiaRecord.Load(game.Settings).IsEmpty);
    }

    [Fact]
    public void Record_RoundTripsThroughSettingsJson()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 60;
        nv.Apply(game, Changes((Fps, 117), (SrPreset, 0x0B)));
        var settings = new AppSettings();
        settings.Games["game-1"] = game.Settings;

        var json = JsonSerializer.Serialize(settings, JsonCtx.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, JsonCtx.Default.AppSettings)!;

        var record = NvidiaRecord.Load(back.Games["game-1"]);
        Assert.Equal(60u, record.Previous[Fps]);
        Assert.Null(record.Previous[SrPreset]);
        Assert.Contains("prev:10835002", json);
        Assert.Contains("absent", json);
    }

    [Fact]
    public void Record_KeepsOtherOptionsOfThePick()
    {
        var game = new GameOverride();
        game.SetPickOptions(ComponentRegistry.NvidiaDriver, new Dictionary<string, string> { ["future"] = "x" });
        var record = new NvidiaRecord();
        record.Previous[Fps] = 30;
        record.Save(game);

        Assert.Equal("x", game.PickOf(ComponentRegistry.NvidiaDriver)!.Options!["future"]);
        record.Previous.Clear();
        record.Save(game);
        Assert.Equal(["future"], game.PickOf(ComponentRegistry.NvidiaDriver)!.Options!.Keys.ToArray());
    }

    [Fact]
    public void Read_ReportsUnknownSettings_AndShippedGameValues()
    {
        var (nv, drs, _) = Setup();
        drs.Global[Vsync] = 0x47814940;
        drs.AddProfile(FakeDrs.Exe, "Some Game").Predefined[Fps] = 90;
        drs.Profile()!.Values[SrPreset] = 0x0B;
        const uint unknown = NvidiaProfileCatalog.SmoothMotion;

        var snap = nv.Read(FakeDrs.Exe, [Fps, SrPreset, Vsync, unknown]);

        Assert.DoesNotContain(unknown, snap.Known);
        Assert.Contains(Fps, snap.Known);
        Assert.Equal(0x0Bu, snap.User[SrPreset]);
        Assert.False(snap.User.ContainsKey(Fps));
        Assert.Contains(Fps, snap.GameDefaults);
        Assert.Equal(90u, snap.Fallback[Fps]);
        Assert.Equal(0x47814940u, snap.Fallback[Vsync]);
        Assert.Equal("Driver profile: Some Game", snap.Status);
    }

    [Fact]
    public void Read_WithoutAProfile_SaysApplyCreatesOne_AndWritesNothing()
    {
        var (nv, drs, _) = Setup();

        var snap = nv.Read(FakeDrs.Exe, [Fps]);

        Assert.False(snap.HasProfile);
        Assert.Contains("Apply creates one", snap.Status);
        Assert.Equal(0, drs.Saves);
        Assert.Empty(drs.Profiles);
    }

    [Fact]
    public void SetFrameRateLimit_WritesTheLimiter_AndRestoreBringsItBack()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 60;
        drs.Profile()!.Values[SrPreset] = 0x0B;

        Assert.True(nv.SetFrameRateLimit(game, 237).Verified);
        Assert.Equal(237u, drs.Value(Fps));
        Assert.True(nv.SetFrameRateLimit(game, 0).Verified);
        Assert.Equal(0u, drs.Value(Fps));

        nv.RestoreFrameRateLimit(game);
        Assert.Equal(60u, drs.Value(Fps));
        Assert.Equal(0x0Bu, drs.Value(SrPreset));
        Assert.True(NvidiaRecord.Load(game.Settings).IsEmpty);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(19)]
    [InlineData(1001)]
    public void SetFrameRateLimit_RejectsValuesTheDriverDoesNotTake(int fps)
    {
        var (nv, drs, game) = Setup();

        Assert.Throws<ArgumentOutOfRangeException>(() => nv.SetFrameRateLimit(game, fps));
        Assert.Equal(0, drs.Opens);
    }

    [Fact]
    public async Task ViewModel_HidesOptionsTheDriverDoesNotKnow_AndShowsTheLoadedValues()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe).Values[Fps] = 117;
        var vm = new DriverProfileViewModel(FakeDrs.Exe, "Game", settings: game.Settings, profile: nv);

        await vm.Load();

        Assert.True(vm.Loaded);
        Assert.All(vm.Relevant.GroupBy(o => o.Group), g => Assert.True(g.First().ShowGroupHeader && g.Skip(1).All(o => !o.ShowGroupHeader)));
        Assert.Contains(vm.Relevant, o => o.Topic == "nv-fps");
        Assert.Contains(vm.Relevant, o => o.Topic == "nv-sr-preset");
        // The fake driver defines only a few ids: everything else is hidden.
        Assert.DoesNotContain(vm.Relevant.Concat(vm.Other), o => o.Topic is "nv-smooth-motion" or "nv-rtx-hdr" or "nv-mfg" or "nv-low-latency");
        Assert.Contains("not shown", vm.HiddenNote);
        Assert.Equal("117 FPS", vm.Relevant.Single(o => o.Topic == "nv-fps").Selected!.Label);
    }

    [Fact]
    public async Task ViewModel_ShowsEverythingWhenTheDriverDefinesAllOfIt()
    {
        var all = NvidiaProfileCatalog.Create();
        var (nv, _, game) = Setup(d => d.Define(all.SelectMany(o => o.Ids).ToArray()));
        var vm = new DriverProfileViewModel(FakeDrs.Exe, "Game", settings: game.Settings, profile: nv);

        await vm.Load();

        Assert.Null(vm.HiddenNote);
        Assert.Equal(all.Count, vm.Relevant.Count + vm.Other.Count);
    }

    [Fact]
    public async Task ViewModel_ResetIsOfferedOnlyWhenTheAppWroteSomething()
    {
        var (nv, drs, game) = Setup();
        drs.AddProfile(FakeDrs.Exe);
        var before = new DriverProfileViewModel(FakeDrs.Exe, "Game", settings: game.Settings, profile: nv);
        await before.Load();
        Assert.False(before.CanReset);

        nv.Apply(game, Changes((Fps, 117)));

        var after = new DriverProfileViewModel(FakeDrs.Exe, "Game", settings: game.Settings, profile: nv);
        Assert.True(after.CanReset);
    }

    private static readonly uint[] DlssIds = [0x10E41DF3, 0x10E41E01, 0x10E41DF7, 0x10E41E02, 0x10E41DF1, 0x10E41E03];

    /// <summary>Profiles as 2.2.x left them: preset and override switch written as one block.</summary>
    [Theory]
    [InlineData(0x10E41DF3u, 0x10E41E01u, 0x0Bu, 1u, "sr-preset", "K")]
    [InlineData(0x10E41DF3u, 0x10E41E01u, 0x00FFFFFFu, 1u, "sr-preset", "Recommended")]
    [InlineData(0x10E41DF3u, 0x10E41E01u, 0x0Du, 1u, "sr-preset", "M")]
    [InlineData(0x10E41DF3u, 0x10E41E01u, 0u, 0u, "sr-preset", "Off")]
    [InlineData(0x10E41DF7u, 0x10E41E02u, 0x06u, 1u, "rr-preset", "F")]
    [InlineData(0x10E41DF7u, 0x10E41E02u, 0u, 0u, "rr-preset", "Off")]
    [InlineData(0x10E41DF1u, 0x10E41E03u, 0x00FFFFFEu, 1u, "fg-preset", "NVIDIA default")]
    [InlineData(0x10E41DF1u, 0x10E41E03u, 0x02u, 1u, "fg-preset", "B")]
    [InlineData(0x10E41DF1u, 0x10E41E03u, 0u, 0u, "fg-preset", "Off")]
    public async Task ViewModel_ShowsTheChoiceAProfileFromTheOldVersionHolds(uint presetId, uint switchId, uint preset, uint switchValue, string key, string label)
    {
        var (nv, drs, game) = Setup(d => d.Define(DlssIds));
        var profile = drs.AddProfile(FakeDrs.Exe);
        profile.Values[presetId] = preset;
        profile.Values[switchId] = switchValue;
        var vm = new DriverProfileViewModel(FakeDrs.Exe, "Game", settings: game.Settings, profile: nv);

        await vm.Load();

        var option = vm.Relevant.Concat(vm.Other).Single(o => o.Topic == $"nv-{key}");
        Assert.Equal(label, option.Selected!.Label);
        Assert.False(option.IsDirty);
    }

    [Fact]
    public void Apply_OfAPresetChoice_WritesPresetAndSwitch_AndResetRestoresBoth()
    {
        var (nv, drs, game) = Setup(d => d.Define(DlssIds));
        var profile = drs.AddProfile(FakeDrs.Exe);
        profile.Values[0x10E41DF3] = 0x0A;
        profile.Values[0x10E41E01] = 0;

        var changes = NvidiaProfileCatalog.Create().Single(o => o.Key == "sr-preset").Choices.Single(c => c.Label == "K").Writes
            .ToDictionary(w => w.Id, w => (uint?)w.Value);
        Assert.True(nv.Apply(game, changes).Verified);
        Assert.Equal(0x0Bu, drs.Value(0x10E41DF3));
        Assert.Equal(1u, drs.Value(0x10E41E01));

        Assert.True(nv.Reset(game).Verified);
        Assert.Equal(0x0Au, drs.Value(0x10E41DF3));
        Assert.Equal(0u, drs.Value(0x10E41E01));
        Assert.True(NvidiaRecord.Load(game.Settings).IsEmpty);
    }

    [Fact]
    public void UseGlobal_RemovesPresetAndSwitch()
    {
        var (nv, drs, game) = Setup(d => d.Define(DlssIds));
        var profile = drs.AddProfile(FakeDrs.Exe);
        profile.Values[0x10E41DF7] = 0x06;
        profile.Values[0x10E41E02] = 1;
        var option = new NvOptionViewModel(NvidiaProfileCatalog.Create().Single(o => o.Key == "rr-preset"));

        Assert.Equal([0x10E41DF7u, 0x10E41E02u], option.Ids);
        Assert.Null(option.Choices[0].Value);
        var result = nv.Apply(game, option.Ids.ToDictionary(id => id, _ => (uint?)null));

        Assert.True(result.Verified);
        Assert.Null(drs.Value(0x10E41DF7));
        Assert.Null(drs.Value(0x10E41E02));
    }

    [Fact]
    public void NoNvidiaDriver_IsNotAvailable()
    {
        var (nv, drs, _) = Setup();
        drs.Available = false;

        Assert.False(nv.Available);
    }
}

/// <summary>The list of offered settings: every id and value is one the research and Profile Inspector name.</summary>
public class NvidiaCatalogTests
{
    private static readonly IReadOnlyList<NvOptionDef> Options = NvidiaProfileCatalog.Create();

    [Fact]
    public void EveryChoiceWritesOnlyIdsOfItsOption_AndEveryOptionHasASourceAndHelp()
    {
        foreach (var o in Options)
        {
            Assert.NotEmpty(o.Source);
            Assert.NotNull(HelpTopics.Get(o.Topic));
            Assert.NotEmpty(o.Choices);
            foreach (var c in o.Choices)
            {
                Assert.All(c.Writes, w => Assert.Contains(w.Id, o.Ids));
                Assert.Equal(c.Writes.Count, c.Writes.Select(w => w.Id).Distinct().Count());
            }
        }
    }

    [Fact]
    public void OptionKeys_AndOwnedIds_AreUnique()
    {
        Assert.Equal(Options.Count, Options.Select(o => o.Key).Distinct().Count());
        var ids = Options.SelectMany(o => o.Ids).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ChoiceLabels_AreUniquePerOption()
    {
        foreach (var o in Options) Assert.Equal(o.Choices.Count, o.Choices.Select(c => c.Label).Distinct().Count());
    }

    private static (uint Id, uint Value)[] Writes(string key, string label) =>
        Options.Single(o => o.Key == key).Choices.Single(c => c.Label == label).Writes.ToArray();

    [Fact]
    public void Values_MatchProfileInspector()
    {
        Assert.Equal([(0x10AFB768u, 6u), (0x10E41DF5u, 77u)], Writes("sr-mode", "Custom 77%"));
        Assert.Equal([(0x10308298u, 2u)], Writes("mfg-mode", "Fixed"));
        Assert.Equal([(0x10308298u, 4u)], Writes("mfg-mode", "Dynamic"));
        Assert.Equal([(0x104D6667u, 5u)], Writes("mfg-factor", "6x"));
        Assert.Equal([(0x10562D0Fu, 3u)], Writes("mfg-dynamic-max", "Up to 4x"));
        Assert.Equal([(0x10CF4125u, 0x01000000u)], Writes("mfg-dynamic-fps", "Max refresh rate"));
        Assert.Equal([(0xB0D384C0u, 1u)], Writes("smooth-motion", "On"));
        Assert.Equal([(0xB0CC0875u, 3u)], Writes("smooth-motion-apis", "DX12 + DX11"));
        Assert.Equal([(0x1077A11Au, 1u)], Writes("rtx-hdr-allow", "Allow"));
        Assert.Equal([(0x00DD48FBu, 1u)], Writes("rtx-hdr", "On"));
        Assert.Equal([(0x00432F84u, 0x0Au)], Writes("rtx-hdr-debanding", "Medium debanding"));
        Assert.Equal([(0x00A879CFu, 0x71271021u), (0x005A375Cu, 0x96861077u)], Writes("vsync", "1/3 refresh rate"));
        Assert.Equal([(0x0005F543u, 2u), (0x10835000u, 1u)], Writes("low-latency", "Ultra"));
        Assert.Equal([(0x10835002u, 237u)], Writes("frame-limit", "237 FPS"));
    }

    [Theory]
    [InlineData("sr-preset", "Off", 0x10E41DF3u, 0u, 0x10E41E01u, 0u)]
    [InlineData("sr-preset", "Recommended", 0x10E41DF3u, 0x00FFFFFFu, 0x10E41E01u, 1u)]
    [InlineData("sr-preset", "K", 0x10E41DF3u, 0x0Bu, 0x10E41E01u, 1u)]
    [InlineData("sr-preset", "J", 0x10E41DF3u, 0x0Au, 0x10E41E01u, 1u)]
    [InlineData("sr-preset", "M", 0x10E41DF3u, 0x0Du, 0x10E41E01u, 1u)]
    [InlineData("sr-preset", "L", 0x10E41DF3u, 0x0Cu, 0x10E41E01u, 1u)]
    [InlineData("rr-preset", "Off", 0x10E41DF7u, 0u, 0x10E41E02u, 0u)]
    [InlineData("rr-preset", "Recommended", 0x10E41DF7u, 0x00FFFFFFu, 0x10E41E02u, 1u)]
    [InlineData("rr-preset", "D", 0x10E41DF7u, 0x04u, 0x10E41E02u, 1u)]
    [InlineData("rr-preset", "F", 0x10E41DF7u, 0x06u, 0x10E41E02u, 1u)]
    [InlineData("fg-preset", "Off", 0x10E41DF1u, 0u, 0x10E41E03u, 0u)]
    [InlineData("fg-preset", "NVIDIA default", 0x10E41DF1u, 0x00FFFFFEu, 0x10E41E03u, 1u)]
    [InlineData("fg-preset", "A", 0x10E41DF1u, 0x01u, 0x10E41E03u, 1u)]
    [InlineData("fg-preset", "B", 0x10E41DF1u, 0x02u, 0x10E41E03u, 1u)]
    public void DlssPresets_AreWrittenTogetherWithTheirOverrideSwitch(string key, string label, uint presetId, uint preset, uint switchId, uint switchValue)
    {
        Assert.Equal([(presetId, preset), (switchId, switchValue)], Writes(key, label));
    }

    [Fact]
    public void EveryDlssPresetChoice_IsCovered_AndThereAreNoSeparateLatestDllOptions()
    {
        Assert.Equal([0x10E41DF3u, 0x10E41E01u], Options.Single(o => o.Key == "sr-preset").Ids);
        Assert.Equal([0x10E41DF7u, 0x10E41E02u], Options.Single(o => o.Key == "rr-preset").Ids);
        Assert.Equal([0x10E41DF1u, 0x10E41E03u], Options.Single(o => o.Key == "fg-preset").Ids);
        Assert.Equal(6, Options.Single(o => o.Key == "sr-preset").Choices.Count);
        Assert.Equal(4, Options.Single(o => o.Key == "rr-preset").Choices.Count);
        Assert.Equal(4, Options.Single(o => o.Key == "fg-preset").Choices.Count);
        Assert.DoesNotContain(Options, o => o.Key.EndsWith("-latest"));
        Assert.All(Options.Where(o => o.Key.EndsWith("-preset")), o => Assert.All(o.Choices, c => Assert.Equal(2, c.Writes.Count)));
    }

    [Fact]
    public void SmoothMotion_HasAVulkanFreeChoice()
    {
        var apis = Options.Single(o => o.Key == "smooth-motion-apis");

        Assert.All(apis.Choices.Where(c => c.Label == "DX12 + DX11"), c => Assert.Equal(0u, c.Writes.Single().Value & 4));
    }

    [Fact]
    public void RtxHdrSliders_AreNotOffered_BecauseTheirValuesAreUndocumented()
    {
        var ids = Options.SelectMany(o => o.Ids).ToHashSet();

        Assert.DoesNotContain(0x00DD48FCu, ids);
        Assert.DoesNotContain(0x00DD48FDu, ids);
        Assert.DoesNotContain(0x00DD48FEu, ids);
        Assert.DoesNotContain(0x00DD48FFu, ids);
        Assert.Contains(NvidiaProfileCatalog.NotOffered, n => n.Contains("0x00DD48FC"));
    }

    [Fact]
    public void FrameRateLimit_ChoicesStayInsideTheDriverRange()
    {
        var limit = Options.Single(o => o.Key == "frame-limit");

        foreach (var w in limit.Choices.SelectMany(c => c.Writes))
            Assert.True(w.Value == 0 || w.Value is >= NvidiaProfileCatalog.MinFrameRate and <= NvidiaProfileCatalog.MaxFrameRate);
    }

    [Fact]
    public void HardwareHints_ArePartOfTheHelpTexts()
    {
        Assert.Contains("RTX 40/50", HelpTopics.Get("nv-smooth-motion")!.Body);
        Assert.Contains("571.86", HelpTopics.Get("nv-smooth-motion")!.Body);
        Assert.Contains("595.97", HelpTopics.Get("nv-mfg-dynamic")!.Body);
        Assert.Contains("RTX 50", HelpTopics.Get("nv-mfg")!.Body);
        Assert.Contains("441.87", HelpTopics.Get("nv-fps")!.Body);
        Assert.Contains("572.83", HelpTopics.Get("nv-sr-mode")!.Body);
        Assert.Contains("Vulkan", HelpTopics.Get("nv-smooth-motion")!.Body);
    }
}
