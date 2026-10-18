# addon-probe

Research tools for closed-source ReShade NR add-ons. Findings go to `docs/addons/`. Not part of the app build or CI.
Never add add-on binaries, logs or dumps here; work on copies in a temp folder.

## host/ — fake ReShade host

`host.exe` exports the ReShade add-on API (`ReShadeRegisterAddon`, `ReShadeGetConfigValue`, `ReShadeSetConfigValue`,
`ReShadeRegisterOverlay`, `ReShadeGetImGuiFunctionTable`, ...), loads one add-on and logs every call. Config reads
return "not found" unless `--ini` supplies a file, so a bare run shows the keys an add-on reads and the defaults it
writes. No game, device or driver profile is touched.

Build (mingw g++ x64, WinLibs):

```sh
mkdir src   # put reshade.hpp, reshade_api*.hpp, reshade_events.hpp, reshade_overlay.hpp from crosire/reshade include/ here
python gen.py            # writes gen.inc: ImGui table stubs (version 19250) and event names from the headers
g++ -O1 -std=c++20 -o host.exe host.cpp -Isrc -static -Wl,--export-all-symbols
```

Run one add-on in its own folder: `run.sh <subdir> <file.addon64> <tag> [host args]`. Host flags: `--addon`, `--log`,
`--dump` (writes the ini the add-on set), `--ini` (answer reads from a file), `--overlay` / `--events` (call the
registered overlay and event callbacks with a fake runtime), `--imgui-true <fn>` / `--imgui-false <fn>` (force an ImGui
call's return to open collapsed sections), `--passes`, `--wait`.

Parsers: `summ.py` (ordered read/write summary), `widgets.py` / `widgets107.py` (SF / Lecram v10 overlay widgets: label,
`PushID` key, options, selected segment), `valmap.py` / `valmap107.py` (seed ini values and watch the selection move),
`oracle.py` (Lecram v10 "could not be used as written" clamp messages), `classify111.py` (Lecram v11 retired /
recognised keys from its `NR-SETTINGS` log lines), `igview.py` (raw ImGui call view).

Limits: Lecram v11 reads and writes `ReShade.ini` from disk next to the exe and draws its own UI (d2d1/dwrite), so its
labels can't be captured; seed `ReShade.ini` next to `host.exe` and read its `NR-SETTINGS event=effective` log line.

## static/ — binary analysis (Python, `pip install pefile capstone numpy`)

`pel.py` (PE loader), `xref.py` (code references to string RVAs), `disa.py` (disassembly around an RVA), `ext.py` /
`ext2.py` (Lecram v10 config getter and clamp calls → key, default, min, max), `tab.py` (table output), `sect.py` /
`keys.py` / `ent.py` (sections, key lists, entries). They expect `strings -t x` dumps and a `<name>.bin` copy in the
working folder; read the script before running it on a new build.
