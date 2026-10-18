# Add-on research notes

What we learned about third-party NR add-ons by reading their binaries. None of them publish source or config docs,
so these notes are the reference when a new build arrives. Update the file for an add-on whenever a new build is
checked, and keep the "Checked builds" table current.

| Add-on | Author | File(s) | Notes |
|---|---|---|---|
| RenoDX DLSS | ShortFuse (clshortfuse) | `renodx-dlss.addon64` | [renodx-dlss-sf.md](renodx-dlss-sf.md) |
| DLSS5 Neural Rendering | Lecram | `renodx-dlss5.addon64` (≤ v10.x), `dlss5-nr.addon64` (v11+) | [lecram-dlss5.md](lecram-dlss5.md) |

Deep Fried Chicken's licence forbids reverse engineering, so none of the methods below apply to it.
[deep-fried-chicken.md](deep-fried-chicken.md) records only what its author ships (INSTALL.txt, cfg keys and
comments, file names, version resources) and what the app installs.

## Rules

- Check the licence first (bundled LICENSE / THIRD-PARTY-NOTICES, strings like "reverse engineer"). Stop if it forbids it.
- Never commit the binaries (`.gitignore` blocks `*.addon64`, `*.dll`). Work on copies in a temp folder.
- Mark every fact: **C** = a string or source states it literally, **D** = read from disassembly (clamp/constructor
  immediates), **I** = inference. Options in the app need C or D; I-facts go into help text as "undocumented".

## How to analyse a new build

1. Version and identity: `(Get-Item x.addon64).VersionInfo` (OriginalFilename, FileVersion). Lecram v11 has no
   version resource; identify it by its strings instead.
2. Strings with offsets: `strings -t x -n 4 x.addon64 > a.txt` and `strings -t x -e l x.addon64 > w.txt` (UTF-16).
   Grep for the ini section, `Key=` in help/log text, tooltips and label lists.
3. RenoDX-framework add-ons (SF): each `Setting` is a constructor in a static initializer. Key, label, label list,
   tooltip, default (float immediate), min/max and the `is_global` flag are all in that constructor. Globals go to
   `[<NAME>]`, everything else to `[<NAME>-preset1]` (renodx `utils/settings.hpp`: preset slot 1 is always loaded at
   start, the overlay's slot choice is not saved).
4. Lecram v10: settings are read through clamp helpers, so default/min/max are immediates next to the key string xref.
   v11 is Rust: the `nr-settings` catalog carries a doc string per key (range, default, retired/dev-only).
5. Diff against the previous build's table, then follow AGENTS.md: update options, help, advice and tests before
   raising the tested version.

Tooling: `tools/addon-probe/` (fake ReShade host that logs config reads/writes, plus the xref/clamp scripts).
