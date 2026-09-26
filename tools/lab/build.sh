#!/usr/bin/env bash
# Builds a git ref of XG into a runnable lab directory.
#
#   tools/lab/build.sh <git ref> <output dir>
#
# Needs mono (mono-devel, mono-xbuild), gcc and NuGet access to api.nuget.org.
set -euo pipefail

REF="$1"
OUT="$(realpath -m "$2")"
LAB="$(cd "$(dirname "$0")" && pwd)"
REPO="$(git -C "$LAB" rev-parse --show-toplevel)"
WORK="$(mktemp -d)"
trap 'git -C "$REPO" worktree remove --force "$WORK/src" >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT

NUGET="${NUGET:-$HOME/.cache/xg-lab/nuget.exe}"
if [[ ! -f "$NUGET" ]]; then
	mkdir -p "$(dirname "$NUGET")"
	curl -sSfL -o "$WORK/nuget.nupkg" https://api.nuget.org/v3-flatcontainer/nuget.commandline/6.11.1/nuget.commandline.6.11.1.nupkg
	unzip -q -o "$WORK/nuget.nupkg" -d "$WORK/nuget"
	cp "$WORK/nuget/tools/NuGet.exe" "$NUGET"
fi

git -C "$REPO" worktree add -q --detach "$WORK/src" "$REF"
cd "$WORK/src"
mono "$NUGET" restore XG.sln -PackagesDirectory packages -NonInteractive >/dev/null
xbuild XG.Application/XG.Application.csproj /p:Configuration=Build.Mono >"$WORK/build.log" 2>&1 || { tail -30 "$WORK/build.log"; exit 1; }

BIN="$WORK/src/XG.Application/bin/Build.Mono"
cp packages/Nowin.0.12.1.0/lib/net45/Nowin.dll "$BIN/"

rm -rf "$OUT"
mkdir -p "$OUT"
cp -r "$BIN/." "$OUT/"

mcs "$LAB/XgLab.cs" -out:"$OUT/XgLab.exe" -nowarn:618 \
	-r:"$OUT/XG.Business.dll" -r:"$OUT/XG.Config.dll" -r:"$OUT/XG.Model.dll" -r:"$OUT/XG.Plugin.dll" \
	-r:"$OUT/XG.Plugin.Irc.dll" -r:"$OUT/XG.Plugin.Webserver.dll" -r:"$OUT/XG.Extensions.dll" \
	-r:"$OUT/log4net.dll" -r:"$OUT/Db4objects.Db4o.dll" -r:"$OUT/SharpRobin.dll" -r:System.Configuration.dll

gcc -shared -fPIC -o "$OUT/libkernel32shim.so" "$LAB/kernel32shim.c"
cat >"$OUT/Db4objects.Db4o.dll.config" <<EOF
<configuration>
  <dllmap dll="kernel32.dll" target="$OUT/libkernel32shim.so" />
</configuration>
EOF

echo "$REF $(git rev-parse --short HEAD)" >"$OUT/REVISION"
echo "built $REF ($(git rev-parse --short HEAD)) into $OUT"
