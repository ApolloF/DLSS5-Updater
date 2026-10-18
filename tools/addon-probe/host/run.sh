#!/bin/bash
# usage: run.sh <subdir> <addon file> <tag> [extra host args...]   (cleans the run folder first, keeps host.exe + addon)
H="$(cd "$(dirname "$0")" && pwd)"
d="$1"; a="$2"; tag="$3"; shift 3
cp "$H/host.exe" "$H/$d/host.exe"
cd "$H/$d"
find . -mindepth 1 -maxdepth 1 ! -name "$a" ! -name host.exe ! -name "*.ini.seed" -exec rm -rf {} +
[ -f ReShade.ini.seed ] && cp ReShade.ini.seed ReShade.ini
timeout 90 ./host.exe --addon "$(cygpath -w "$PWD/$a")" --log "$(cygpath -w "$H/logs/${d}_${tag}.log")" --dump "$(cygpath -w "$H/logs/${d}_${tag}.writes.ini")" "$@"
echo "exit=$? ($d $tag)"
ls -la | grep -v -E "host.exe|\.addon64" | tail -n +2 > "$H/logs/${d}_${tag}.files.txt"; [ -f ReShade.ini ] && cp ReShade.ini "$H/logs/${d}_${tag}.ReShade.ini.out"
