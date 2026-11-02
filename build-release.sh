#!/usr/bin/env bash
# Builds ready-to-run copies of Timetable Checker for Windows and Mac.
# Needs the .NET 8 SDK (https://dotnet.microsoft.com/download). Output goes in ./publish
set -euo pipefail
cd "$(dirname "$0")"
OUT=publish
rm -rf "$OUT"
mkdir -p "$OUT"

publish() { dotnet publish src/TimetableChecker.csproj -c Release -r "$1" -o "$OUT/$1" -p:DebugType=none; }

# ---- Windows: TimetableChecker.exe with the timetables folder beside it ----
publish win-x64
(cd "$OUT/win-x64" && zip -qr "../TimetableChecker-Windows.zip" TimetableChecker.exe timetables)

# ---- Mac: a TimetableChecker.app for Apple Silicon (arm64) and Intel (x64) ----
for RID in osx-arm64 osx-x64; do
  publish "$RID"
  APP="$OUT/$RID/Timetable Checker.app"
  mkdir -p "$APP/Contents/MacOS"
  cp packaging/Info.plist "$APP/Contents/"
  mv "$OUT/$RID/TimetableChecker" "$APP/Contents/MacOS/"
  chmod +x "$APP/Contents/MacOS/TimetableChecker"
  # The example timetables sit next to the app (apps can't safely edit files inside themselves)
  mv "$OUT/$RID/timetables" "$OUT/$RID/Example timetables"
  # Macs only run apps that are signed; an "ad-hoc" signature is enough for personal use
  if command -v codesign >/dev/null; then codesign --force --deep --sign - "$APP"; fi
  NAME=$([ "$RID" = osx-arm64 ] && echo "Mac-AppleSilicon" || echo "Mac-Intel")
  (cd "$OUT/$RID" && zip -qry "../TimetableChecker-$NAME.zip" "Timetable Checker.app" "Example timetables")
done

echo "Done:"; ls -lh "$OUT"/*.zip
