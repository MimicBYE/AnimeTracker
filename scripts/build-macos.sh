#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
VERSION=$(tr -d '\r\n' < "$ROOT/VERSION")
STAGE="$ROOT/artifacts/macos/local-$(date +%Y%m%d-%H%M%S)"
APP="$STAGE/Anime Tracker.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$ROOT/dist/macos"
dotnet publish "$ROOT/src/AnimeTracker.Desktop/AnimeTracker.Desktop.csproj" -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o "$APP/Contents/MacOS" --configfile "$ROOT/NuGet.Config"
cp "$ROOT/src/AnimeTracker.Desktop/Assets/app.icns" "$APP/Contents/Resources/app.icns"
cp -R "$ROOT/docs/licenses" "$APP/Contents/Resources/licenses"
cp "$ROOT/docs/MACOS.md" "$ROOT/docs/THIRD-PARTY-NOTICES.md" "$ROOT/docs/ArcCatalog-SOURCES.txt" "$APP/Contents/Resources/"
cp "$ROOT/src/AnimeTracker.Desktop/Assets/Info.plist" "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/AnimeTracker"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
cp "$ROOT/docs/MACOS.md" "$ROOT/docs/THIRD-PARTY-NOTICES.md" "$ROOT/docs/ArcCatalog-SOURCES.txt" "$STAGE/"
ditto -c -k --keepParent "$APP" "$ROOT/dist/macos/AnimeTracker-$VERSION-macos-arm64-local.zip"
printf 'Built: %s\n' "$APP"
