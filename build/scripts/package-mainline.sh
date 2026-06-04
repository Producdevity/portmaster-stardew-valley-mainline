#!/bin/bash

set -euo pipefail

if [ "${1:-}" != "--inside-docker" ]; then
  echo "This script is intended to run inside the project Docker image." >&2
  exit 1
fi
shift

SKIP_VALIDATION=0
VALIDATE_ONLY=0
TRACE_VALIDATION=0
SMOKE_TIMEOUT_SECONDS="${SDV_MAINLINE_SMOKE_TIMEOUT:-30}"
while [ "$#" -gt 0 ]; do
  case "$1" in
    --skip-validation)
      SKIP_VALIDATION=1
      ;;
    --validate-only)
      VALIDATE_ONLY=1
      ;;
    --trace-validation)
      TRACE_VALIDATION=1
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
  shift
done

ROOT="/workspace"
OUT="$ROOT/build/out/mainline"
ARTIFACTS="$OUT/artifacts"
NATIVE_OUT="$ARTIFACTS/native"
PATCHER_OUT="$ARTIFACTS/MainlineGameDataPatcher"
FIXTURE_MOD_OUT="$ARTIFACTS/FixtureSmapiMod"
SMAPI_BUNDLE_OUT="$ARTIFACTS/SMAPIBundle"
PACKAGE_ROOT="$OUT/package"
RELEASE_STAGE_ROOT="$OUT/release-package"
RELEASE_DIR="$OUT/release"
GAME_DIR="$PACKAGE_ROOT/stardewvalleymainline"
LOG_DIR="$OUT/logs"
CACHE_DIR="$OUT/cache"
MONOGAME_ROOT="${MONOGAME_ROOT:-/monogame}"
FRAMEWORK_PROJECT="$MONOGAME_ROOT/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj"
PATCH_PROJECT="$ROOT/build/src/StardewPatches.Mainline/StardewPatches.Mainline.csproj"
PATCHER_PROJECT="$ROOT/build/src/MainlineGameDataPatcher/MainlineGameDataPatcher.csproj"
FIXTURE_MOD_PROJECT="$ROOT/build/src/FixtureSmapiMod/FixtureSmapiMod.csproj"
DOTNET_RUNTIME_VERSION="6.0.32"
DOTNET_RUNTIME_ARCHIVE="$CACHE_DIR/dotnet-runtime-$DOTNET_RUNTIME_VERSION-linux-arm64.tar.gz"
DOTNET_RUNTIME_SRC="$OUT/runtime/dotnet"
SKIA_VERSION="2.80.3"
SKIA_PKG="$CACHE_DIR/SkiaSharp.NativeAssets.Linux.NoDependencies.$SKIA_VERSION.nupkg"
SMAPI_VERSION="4.5.2"
SMAPI_ARCHIVE="$CACHE_DIR/SMAPI-$SMAPI_VERSION-installer.zip"
SMAPI_INSTALLER_ROOT="$CACHE_DIR/smapi-installer"
SMAPI_INSTALLER_DIR="$SMAPI_INSTALLER_ROOT/SMAPI $SMAPI_VERSION installer"
SMAPI_PAYLOAD_ROOT="$CACHE_DIR/smapi-payload"
SMAPI_PAYLOAD_DIR="$SMAPI_PAYLOAD_ROOT/linux-install"
SMAPI_LICENSE_PATH="$CACHE_DIR/SMAPI-LICENSE.txt"
SMAPI_TOOLKIT_FILES=(
  SMAPI.Toolkit.dll
  SMAPI.Toolkit.xml
  SMAPI.Toolkit.CoreInterfaces.dll
  SMAPI.Toolkit.CoreInterfaces.xml
)
NATIVE_SHIMS=(
  Imm32.dll
  libImm32.dll.so
  user32.dll
  libuser32.dll.so
  SDL2.dll
  libSDL2.dll.so
  libsteam_api64.so
  libsdkencryptedappticket64.so
)

rm -rf "$ARTIFACTS" "$NATIVE_OUT"
mkdir -p "$ARTIFACTS" "$NATIVE_OUT" "$LOG_DIR" "$CACHE_DIR" "$RELEASE_DIR"

fetch_skia_native() {
  if [ ! -f "$SKIA_PKG" ]; then
    curl -fsSL "https://www.nuget.org/api/v2/package/SkiaSharp.NativeAssets.Linux.NoDependencies/$SKIA_VERSION" -o "$SKIA_PKG"
  fi

  rm -rf "$CACHE_DIR/skia"
  mkdir -p "$CACHE_DIR/skia"
  unzip -oq "$SKIA_PKG" "runtimes/linux-arm64/native/libSkiaSharp.so" -d "$CACHE_DIR/skia"
  cp "$CACHE_DIR/skia/runtimes/linux-arm64/native/libSkiaSharp.so" "$NATIVE_OUT/libSkiaSharp.so"
}

fetch_dotnet_runtime() {
  if [ ! -f "$DOTNET_RUNTIME_ARCHIVE" ]; then
    curl -fsSL "https://dotnetcli.azureedge.net/dotnet/Runtime/$DOTNET_RUNTIME_VERSION/dotnet-runtime-$DOTNET_RUNTIME_VERSION-linux-arm64.tar.gz" -o "$DOTNET_RUNTIME_ARCHIVE"
  fi

  rm -rf "$OUT/runtime"
  mkdir -p "$DOTNET_RUNTIME_SRC"
  tar -xzf "$DOTNET_RUNTIME_ARCHIVE" -C "$DOTNET_RUNTIME_SRC"
}

fetch_smapi_bundle() {
  if [ ! -f "$SMAPI_ARCHIVE" ]; then
    curl -fsSL "https://github.com/Pathoschild/SMAPI/releases/download/$SMAPI_VERSION/SMAPI-$SMAPI_VERSION-installer.zip" -o "$SMAPI_ARCHIVE"
  fi

  if [ ! -f "$SMAPI_LICENSE_PATH" ]; then
    curl -fsSL "https://raw.githubusercontent.com/Pathoschild/SMAPI/$SMAPI_VERSION/LICENSE.txt" -o "$SMAPI_LICENSE_PATH"
  fi

  rm -rf "$SMAPI_INSTALLER_ROOT" "$SMAPI_PAYLOAD_ROOT" "$SMAPI_BUNDLE_OUT"
  mkdir -p "$SMAPI_INSTALLER_ROOT" "$SMAPI_PAYLOAD_ROOT" "$SMAPI_BUNDLE_OUT/game-root" "$SMAPI_BUNDLE_OUT/default-mods"

  unzip -oq "$SMAPI_ARCHIVE" -d "$SMAPI_INSTALLER_ROOT"
  unzip -oq "$SMAPI_INSTALLER_DIR/internal/linux/install.dat" -d "$SMAPI_PAYLOAD_DIR"

  validate_smapi_payload

  cp -R "$SMAPI_PAYLOAD_DIR/." "$SMAPI_BUNDLE_OUT/game-root/"
  rm -rf "$SMAPI_BUNDLE_OUT/game-root/Mods"
  cp -R "$SMAPI_PAYLOAD_DIR/Mods/ConsoleCommands" "$SMAPI_BUNDLE_OUT/default-mods/ConsoleCommands"
  cp -R "$SMAPI_PAYLOAD_DIR/Mods/SaveBackup" "$SMAPI_BUNDLE_OUT/default-mods/SaveBackup"
  copy_smapi_toolkit_files "$SMAPI_BUNDLE_OUT/default-mods/ConsoleCommands"
  copy_smapi_toolkit_files "$SMAPI_BUNDLE_OUT/default-mods/SaveBackup"
  cp "$SMAPI_INSTALLER_DIR/README.txt" "$SMAPI_BUNDLE_OUT/README.SMAPI.txt"
  cp "$SMAPI_LICENSE_PATH" "$SMAPI_BUNDLE_OUT/LICENSE.SMAPI.txt"
}

copy_smapi_toolkit_files() {
  local dest_dir="$1"
  local file

  for file in "${SMAPI_TOOLKIT_FILES[@]}"; do
    cp "$SMAPI_PAYLOAD_DIR/smapi-internal/$file" "$dest_dir/$file"
  done
}

validate_smapi_payload() {
  local required_paths=(
    "$SMAPI_PAYLOAD_DIR/StardewModdingAPI.dll"
    "$SMAPI_PAYLOAD_DIR/StardewModdingAPI.runtimeconfig.json"
    "$SMAPI_PAYLOAD_DIR/smapi-internal/0Harmony.dll"
    "$SMAPI_PAYLOAD_DIR/Mods/ConsoleCommands/manifest.json"
    "$SMAPI_PAYLOAD_DIR/Mods/SaveBackup/manifest.json"
    "$SMAPI_PAYLOAD_DIR/unix-launcher.sh"
    "$SMAPI_INSTALLER_DIR/README.txt"
    "$SMAPI_LICENSE_PATH"
  )
  local path
  for path in "${required_paths[@]}"; do
    if [ ! -e "$path" ]; then
      echo "Missing required SMAPI payload file: $path" >&2
      return 1
    fi
  done

  local console_id
  local backup_id
  console_id="$(jq -r '.UniqueId' "$SMAPI_PAYLOAD_DIR/Mods/ConsoleCommands/manifest.json")"
  backup_id="$(jq -r '.UniqueId' "$SMAPI_PAYLOAD_DIR/Mods/SaveBackup/manifest.json")"

  if [ "$console_id" != "SMAPI.ConsoleCommands" ] || [ "$backup_id" != "SMAPI.SaveBackup" ]; then
    echo "Unexpected bundled SMAPI default mod IDs: ConsoleCommands=$console_id SaveBackup=$backup_id" >&2
    return 1
  fi
}

build_framework() {
  dotnet restore "$FRAMEWORK_PROJECT"
  dotnet build "$FRAMEWORK_PROJECT" -c Release -f netstandard2.0 --no-restore

  local framework_dll
  local framework_xml
  framework_dll="$(find "$MONOGAME_ROOT/Artifacts/MonoGame.Framework/DesktopGL" -path '*netstandard2.0/MonoGame.Framework.dll' | head -n 1)"
  framework_xml="$(find "$MONOGAME_ROOT/Artifacts/MonoGame.Framework/DesktopGL" -path '*netstandard2.0/MonoGame.Framework.xml' | head -n 1)"

  cp "$framework_dll" "$ARTIFACTS/MonoGame.Framework.dll"
  if [ -n "$framework_xml" ]; then
    cp "$framework_xml" "$ARTIFACTS/MonoGame.Framework.xml"
  fi
}

validate_dynamic_audio_api() {
  local framework_dll="$ARTIFACTS/MonoGame.Framework.dll"
  local symbol
  for symbol in \
    "CueDefinition" \
    "XactSoundBankSound" \
    "OggStreamSoundEffect" \
    "GetCueDefinition" \
    "AddCue" \
    "FromStream" \
    "SetSound" \
    "OnModified"; do
    if ! grep -a -q "$symbol" "$framework_dll"; then
      echo "MonoGame.Framework.dll is missing required dynamic audio API symbol: $symbol" >&2
      return 1
    fi
  done
}

validate_monogame_compat_api() {
  local framework_dll="$ARTIFACTS/MonoGame.Framework.dll"

  if ! grep -a -q "CopyFromTexture" "$framework_dll"; then
    echo "MonoGame.Framework.dll is missing required Stardew MonoGame compatibility API symbol: CopyFromTexture" >&2
    return 1
  fi
}

build_patch_dll() {
  rm -rf "$ROOT/build/src/StardewPatches.Mainline/bin" "$ROOT/build/src/StardewPatches.Mainline/obj"
  dotnet restore "$PATCH_PROJECT"
  dotnet build "$PATCH_PROJECT" -c Release --no-restore

  local patch_dll
  local harmony_dll
  patch_dll="$(find "$ROOT/build/src/StardewPatches.Mainline/bin/Release" -path '*net6.0/StardewPatches.dll' | head -n 1)"
  harmony_dll="$(find "$ROOT/build/src/StardewPatches.Mainline/bin/Release" -path '*net6.0/0Harmony.dll' | head -n 1)"

  cp "$patch_dll" "$ARTIFACTS/StardewPatches.dll"
  if [ -n "$harmony_dll" ]; then
    cp "$harmony_dll" "$ARTIFACTS/0Harmony.dll"
  fi
}

build_game_data_patcher() {
  rm -rf "$PATCHER_OUT"
  dotnet restore "$PATCHER_PROJECT"
  dotnet publish "$PATCHER_PROJECT" -c Release --no-restore -o "$PATCHER_OUT"
}

build_fixture_smapi_mod() {
  rm -rf "$FIXTURE_MOD_OUT"
  mkdir -p "$FIXTURE_MOD_OUT/assets"

  dotnet restore "$FIXTURE_MOD_PROJECT"
  dotnet build "$FIXTURE_MOD_PROJECT" -c Release --no-restore -p:SmapiReference="$SMAPI_BUNDLE_OUT/game-root/StardewModdingAPI.dll"

  local fixture_dll
  fixture_dll="$(find "$ROOT/build/src/FixtureSmapiMod/bin/Release" -path '*net6.0/FixtureSmapiMod.dll' | head -n 1)"

  cp "$fixture_dll" "$FIXTURE_MOD_OUT/FixtureSmapiMod.dll"
  cp "$ROOT/build/src/FixtureSmapiMod/manifest.json" "$FIXTURE_MOD_OUT/manifest.json"
  python3 - "$FIXTURE_MOD_OUT/assets/fixture.wav" <<'PY'
import math
import struct
import sys
import wave

path = sys.argv[1]
sample_rate = 22050
duration = 0.2
frequency = 660
count = int(sample_rate * duration)

with wave.open(path, "wb") as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(sample_rate)
    frames = bytearray()
    for i in range(count):
        sample = int(math.sin(2 * math.pi * frequency * i / sample_rate) * 16000)
        frames.extend(struct.pack("<h", sample))
    wav.writeframes(frames)
PY
  base64 --decode "$ROOT/build/src/FixtureSmapiMod/assets/fixture.ogg.b64" > "$FIXTURE_MOD_OUT/assets/fixture.ogg"
  cp "$FIXTURE_MOD_OUT/assets/fixture.ogg" "$FIXTURE_MOD_OUT/assets/fixture-streamed.ogg"
}

build_native_shims() {
  local shim_source="${SDV_MAINLINE_NATIVE_SOURCE:-"$OUT/native-shims"}"
  local file

  if [ ! -d "$shim_source" ]; then
    echo "Missing prebuilt native shims at $shim_source" >&2
    echo "Run ./build/scripts/build-native-shims.sh before packaging." >&2
    exit 1
  fi

  for file in "${NATIVE_SHIMS[@]}"; do
    cp "$shim_source/$file" "$NATIVE_OUT/$file"
  done
}

assert_no_windows_native_files() {
  local game_data_dir="$1"

  python3 - "$game_data_dir" <<'PY'
import sys
from pathlib import Path

game_data_dir = Path(sys.argv[1])
unexpected = []

for file_path in sorted(game_data_dir.glob("*")):
    if file_path.suffix.lower() not in {".dll", ".exe"}:
        continue

    data = file_path.read_bytes()
    if data[:2] != b"MZ":
        continue

    pe_offset = int.from_bytes(data[0x3C:0x40], "little")
    optional_magic = int.from_bytes(data[pe_offset + 24:pe_offset + 26], "little")
    data_directory_offset = pe_offset + 24 + (112 if optional_magic == 0x20B else 96)
    cli_rva = int.from_bytes(data[data_directory_offset + 14 * 8:data_directory_offset + 14 * 8 + 4], "little")

    if cli_rva == 0:
        unexpected.append(file_path.name)

if unexpected:
    print("Unexpected Windows-native PE files remain in gamedata:", file=sys.stderr)
    for name in unexpected:
        print(name, file=sys.stderr)
    raise SystemExit(1)
PY
}

stage_base_package_root() {
  local dest_root="$1"
  local dest_game_dir="$dest_root/stardewvalleymainline"
  local native_lib_dir="$dest_game_dir/libs.aarch64"

  rm -rf "$dest_root"
  cp -R "$ROOT/portmaster_stardewvalley_mainline" "$dest_root"

  rm -rf \
    "$dest_game_dir/dlls" \
    "$dest_game_dir/libs.aarch64" \
    "$dest_game_dir/dotnet" \
    "$dest_game_dir/overrides" \
    "$dest_game_dir/tools/MainlineGameDataPatcher" \
    "$dest_game_dir/tools/SMAPIBundle"
  mkdir -p \
    "$dest_game_dir/dlls" \
    "$native_lib_dir" \
    "$dest_game_dir/dotnet" \
    "$dest_game_dir/Mods" \
    "$dest_game_dir/overrides/gamedata" \
    "$dest_game_dir/tools/MainlineGameDataPatcher" \
    "$dest_game_dir/tools/SMAPIBundle/game-root" \
    "$dest_game_dir/tools/SMAPIBundle/default-mods"

  cp "$ARTIFACTS/StardewPatches.dll" "$dest_game_dir/dlls/StardewPatches.dll"
  if [ -f "$ARTIFACTS/0Harmony.dll" ]; then
    cp "$ARTIFACTS/0Harmony.dll" "$dest_game_dir/dlls/0Harmony.dll"
  fi

  cp "$ARTIFACTS/MonoGame.Framework.dll" "$dest_game_dir/overrides/gamedata/MonoGame.Framework.dll"
  if [ -f "$ARTIFACTS/MonoGame.Framework.xml" ]; then
    cp "$ARTIFACTS/MonoGame.Framework.xml" "$dest_game_dir/overrides/gamedata/MonoGame.Framework.xml"
  fi

  local file
  for file in "${NATIVE_SHIMS[@]}"; do
    cp "$NATIVE_OUT/$file" "$native_lib_dir/$file"
  done
  cp "$NATIVE_OUT/libSkiaSharp.so" "$native_lib_dir/libSkiaSharp.so"

  cp -R "$SMAPI_BUNDLE_OUT/game-root/." "$dest_game_dir/tools/SMAPIBundle/game-root/"
  cp -R "$SMAPI_BUNDLE_OUT/default-mods/." "$dest_game_dir/tools/SMAPIBundle/default-mods/"
  rm -f \
    "$dest_game_dir/tools/SMAPIBundle/game-root/StardewModdingAPI" \
    "$dest_game_dir/tools/SMAPIBundle/game-root/unix-launcher.sh"
  mkdir -p "$dest_game_dir/licenses"
  cp "$DOTNET_RUNTIME_SRC/LICENSE.txt" "$dest_game_dir/licenses/LICENSE.DotNet.txt"
  cp "$DOTNET_RUNTIME_SRC/ThirdPartyNotices.txt" "$dest_game_dir/licenses/THIRD-PARTY-NOTICES.DotNet.txt"
  unzip -p "$SKIA_PKG" LICENSE.txt > "$dest_game_dir/licenses/LICENSE.SkiaSharp.txt"
  unzip -p "$SKIA_PKG" THIRD-PARTY-NOTICES.txt > "$dest_game_dir/licenses/THIRD-PARTY-NOTICES.SkiaSharp.txt"
  cp "$SMAPI_BUNDLE_OUT/LICENSE.SMAPI.txt" "$dest_game_dir/licenses/LICENSE.SMAPI.txt"
  cp "$SMAPI_BUNDLE_OUT/README.SMAPI.txt" "$dest_game_dir/README.SMAPI.txt"

  cp -R "$DOTNET_RUNTIME_SRC/." "$dest_game_dir/dotnet/"
  find "$PATCHER_OUT" -maxdepth 1 -type f \( -name '*.dll' -o -name '*.json' \) -exec cp {} "$dest_game_dir/tools/MainlineGameDataPatcher/" \;

  verify_staged_package_layout "$dest_game_dir"
}

verify_staged_package_layout() {
  local dest_game_dir="$1"
  local required_paths=(
    "$dest_game_dir/Mods/.gitkeep"
    "$dest_game_dir/libs.aarch64/libSkiaSharp.so"
    "$dest_game_dir/gl4es.aarch64/libGL.so.1"
    "$dest_game_dir/tools/smapi-common"
    "$dest_game_dir/tools/SMAPIBundle/game-root/StardewModdingAPI.dll"
    "$dest_game_dir/tools/SMAPIBundle/game-root/StardewModdingAPI.runtimeconfig.json"
    "$dest_game_dir/tools/SMAPIBundle/game-root/smapi-internal/0Harmony.dll"
    "$dest_game_dir/tools/SMAPIBundle/default-mods/ConsoleCommands/manifest.json"
    "$dest_game_dir/tools/SMAPIBundle/default-mods/SaveBackup/manifest.json"
    "$dest_game_dir/dotnet/LICENSE.txt"
    "$dest_game_dir/dotnet/ThirdPartyNotices.txt"
    "$dest_game_dir/licenses/LICENSE.DotNet.txt"
    "$dest_game_dir/licenses/THIRD-PARTY-NOTICES.DotNet.txt"
    "$dest_game_dir/licenses/LICENSE.SkiaSharp.txt"
    "$dest_game_dir/licenses/THIRD-PARTY-NOTICES.SkiaSharp.txt"
    "$dest_game_dir/licenses/LICENSE.SMAPI.txt"
  )
  local path
  for path in "${required_paths[@]}"; do
    if [ ! -e "$path" ]; then
      echo "Missing staged release path: $path" >&2
      return 1
    fi
  done

  if [ -f "$dest_game_dir/gamedata/Stardew Valley.dll" ]; then
    echo "Release template unexpectedly contains retail Stardew files." >&2
    return 1
  fi

  if [ -e "$dest_game_dir/tools/SMAPIBundle/game-root/StardewModdingAPI" ]; then
    echo "Release template unexpectedly contains SMAPI native apphost." >&2
    return 1
  fi
}

prepare_game_data() {
  local dest_game_dir="$1"
  local patcher_dll="$dest_game_dir/tools/MainlineGameDataPatcher/MainlineGameDataPatcher.dll"
  shift || true

  "$dest_game_dir/dotnet/dotnet" "$patcher_dll" \
    --game-dir "$dest_game_dir/gamedata" \
    --overlay-dir "$dest_game_dir/overrides/gamedata" \
    --mods-dir "$dest_game_dir/Mods" \
    "$@"

  assert_no_windows_native_files "$dest_game_dir/gamedata"
}

stage_validation_package() {
  stage_base_package_root "$PACKAGE_ROOT"

  find "$GAME_DIR/gamedata" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  cp -R "$ROOT/stardewvalley_steam/Stardew Valley/." "$GAME_DIR/gamedata/"

  prepare_game_data "$GAME_DIR"
}

stage_release_package() {
  stage_base_package_root "$RELEASE_STAGE_ROOT"
}

reset_validation_mods() {
  mkdir -p "$GAME_DIR/Mods"
  find "$GAME_DIR/Mods" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  touch "$GAME_DIR/Mods/.gitkeep"
}

install_fixture_mod() {
  local fixture_dir="$GAME_DIR/Mods/PortMasterFixtureMod"
  local fixture_audio_dir="$GAME_DIR/gamedata/Content/PortMasterFixtureAudio"
  mkdir -p "$fixture_dir"
  mkdir -p "$fixture_audio_dir"
  cp "$FIXTURE_MOD_OUT/FixtureSmapiMod.dll" "$fixture_dir/FixtureSmapiMod.dll"
  cp "$FIXTURE_MOD_OUT/manifest.json" "$fixture_dir/manifest.json"
  cp "$FIXTURE_MOD_OUT/assets/fixture.wav" "$fixture_audio_dir/fixture.wav"
  cp "$FIXTURE_MOD_OUT/assets/fixture.ogg" "$fixture_audio_dir/fixture.ogg"
  cp "$FIXTURE_MOD_OUT/assets/fixture-streamed.ogg" "$fixture_audio_dir/fixture-streamed.ogg"
}

run_game_process() {
  local log_file="$1"
  local trace_file="$2"
  local entry_assembly="$3"
  local patch_path="$4"
  shift 4
  local extra_env=("$@")
  local home_dir="$OUT/validation-home"
  local command=(
    env
    HOME="$home_dir"
    XDG_CONFIG_HOME="$home_dir/.config"
    XDG_DATA_HOME="$home_dir/.local/share"
    DOTNET_ROOT="$GAME_DIR/dotnet"
    MONOGAME_PATCH="$patch_path"
    LD_LIBRARY_PATH="$GAME_DIR/libs.aarch64${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    DOTNET_ReadyToRun=1
    COMPlus_ReadyToRun=1
    COMPlus_ZapDisable=0
    LIBGL_ALWAYS_SOFTWARE=1
    ALSOFT_DRIVERS=null
    "${extra_env[@]}"
    xvfb-run -a timeout "${SMOKE_TIMEOUT_SECONDS}s" "$GAME_DIR/dotnet/dotnet" "$entry_assembly"
  )

  set +e
  (
    cd "$GAME_DIR/gamedata"
    if [ "$TRACE_VALIDATION" -eq 1 ] && [ "$trace_file" != "-" ]; then
      rm -f "$trace_file"
      strace -ff -o "$trace_file" -s 256 -yy -e trace=file,process,signal \
        "${command[@]}" >"$log_file" 2>&1
    else
      "${command[@]}" >"$log_file" 2>&1
    fi
  )
  local status=$?
  set -e

  if [ "$status" -ne 0 ] && [ "$status" -ne 124 ]; then
    echo "Smoke test for $entry_assembly exited with status $status." >&2
    cat "$log_file" >&2
    return 1
  fi
}

assert_runtime_log_healthy() {
  local log_file="$1"

  if ! grep -q "StardewPatches initialized" "$log_file"; then
    echo "Smoke test did not execute StardewPatches." >&2
    cat "$log_file" >&2
    return 1
  fi

  if grep -Eq "DllNotFoundException|EntryPointNotFoundException|MissingMethodException|TypeLoadException|FileNotFoundException|Unhandled exception|Segmentation fault|Could not load file or assembly|BadImageFormatException" "$log_file"; then
    echo "Smoke test reported a hard runtime failure." >&2
    cat "$log_file" >&2
    return 1
  fi
}

run_validation() {
  local vanilla_log="$LOG_DIR/smoke-test.log"
  local vanilla_trace="$LOG_DIR/smoke-test.strace"
  local smapi_log="$LOG_DIR/smapi-smoke.log"
  local fixture_log="$LOG_DIR/smapi-fixture-smoke.log"
  local home_dir="$OUT/validation-home"
  local mode

  mkdir -p "$home_dir/.config" "$home_dir/.local/share"
  # shellcheck source=/dev/null
  source "$GAME_DIR/tools/smapi-common"

  reset_validation_mods
  mode="$(sdv_smapi_determine_mode "$GAME_DIR/Mods")"
  if [ "$mode" != "vanilla" ]; then
    echo "Expected empty Mods directory to keep vanilla mode, got $mode." >&2
    return 1
  fi

  run_game_process "$vanilla_log" "$vanilla_trace" "Stardew Valley.dll" "$GAME_DIR/dlls/StardewPatches.dll"
  assert_runtime_log_healthy "$vanilla_log"

  sdv_smapi_sync_default_mods "$GAME_DIR/tools/SMAPIBundle/default-mods" "$GAME_DIR/Mods"
  mode="$(sdv_smapi_determine_mode "$GAME_DIR/Mods")"
  if [ "$mode" != "vanilla" ]; then
    echo "Bundled default SMAPI mods should not trigger SMAPI mode, got $mode." >&2
    return 1
  fi

  prepare_game_data "$GAME_DIR" \
    --smapi-bundle-dir "$GAME_DIR/tools/SMAPIBundle/game-root" \
    --smapi-patch-assembly "$GAME_DIR/dlls/StardewPatches.dll"
  if [ ! -f "$GAME_DIR/gamedata/StardewModdingAPI.deps.json" ]; then
    echo "SMAPI preparation did not synthesize StardewModdingAPI.deps.json." >&2
    return 1
  fi

  export SDV_FORCE_SMAPI=1
  mode="$(sdv_smapi_determine_mode "$GAME_DIR/Mods")"
  unset SDV_FORCE_SMAPI
  if [ "$mode" != "smapi" ]; then
    echo "Forced SMAPI validation mode did not select SMAPI." >&2
    return 1
  fi

  run_game_process "$smapi_log" "-" "StardewModdingAPI.dll" "$GAME_DIR/gamedata/smapi-internal/StardewPatches.dll" \
    "SMAPI_MODS_PATH=$GAME_DIR/Mods" \
    "SMAPI_USE_CURRENT_SHELL=true"
  assert_runtime_log_healthy "$smapi_log"
  if ! grep -Eq "SMAPI [0-9]+\.[0-9]+\.[0-9]+" "$smapi_log"; then
    echo "Forced SMAPI smoke test did not emit a SMAPI startup banner." >&2
    cat "$smapi_log" >&2
    return 1
  fi

  reset_validation_mods
  install_fixture_mod
  sdv_smapi_sync_default_mods "$GAME_DIR/tools/SMAPIBundle/default-mods" "$GAME_DIR/Mods"
  mode="$(sdv_smapi_determine_mode "$GAME_DIR/Mods")"
  if [ "$mode" != "smapi" ]; then
    echo "Fixture mod did not trigger SMAPI mode, got $mode." >&2
    return 1
  fi

  prepare_game_data "$GAME_DIR" \
    --smapi-bundle-dir "$GAME_DIR/tools/SMAPIBundle/game-root" \
    --smapi-patch-assembly "$GAME_DIR/dlls/StardewPatches.dll"
  run_game_process "$fixture_log" "-" "StardewModdingAPI.dll" "$GAME_DIR/gamedata/smapi-internal/StardewPatches.dll" \
    "SMAPI_MODS_PATH=$GAME_DIR/Mods" \
    "SMAPI_USE_CURRENT_SHELL=true"
  assert_runtime_log_healthy "$fixture_log"
  if ! grep -q "Fixture mod loaded" "$fixture_log"; then
    echo "SMAPI fixture smoke test did not load the validation fixture mod." >&2
    cat "$fixture_log" >&2
    return 1
  fi
  if ! grep -q "Fixture injected Data/AudioChanges test entries" "$fixture_log"; then
    echo "SMAPI fixture smoke test did not inject dynamic audio cue entries." >&2
    cat "$fixture_log" >&2
    return 1
  fi
}

create_release_archive() {
  local release_game_dir="$RELEASE_STAGE_ROOT/stardewvalleymainline"
  local archive_name
  local archive_path
  local checksum_path
  local listing_path

  archive_name="$(jq -r '.name' "$release_game_dir/port.json")"
  if [ -z "$archive_name" ] || [ "$archive_name" = "null" ]; then
    echo "Unable to determine release archive name from port.json." >&2
    return 1
  fi

  archive_path="$RELEASE_DIR/$archive_name"
  checksum_path="$RELEASE_DIR/$archive_name.sha256"
  listing_path="$RELEASE_DIR/${archive_name%.zip}.contents.txt"

  rm -f "$archive_path" "$checksum_path" "$listing_path"
  (
    cd "$RELEASE_STAGE_ROOT"
    zip -qr "$archive_path" .
  )

  (
    cd "$RELEASE_DIR"
    sha256sum "$archive_name" > "$(basename "$checksum_path")"
    unzip -l "$archive_name" > "$(basename "$listing_path")"
  )

  if ! grep -q 'stardewvalleymainline/tools/SMAPIBundle/game-root/StardewModdingAPI.dll' "$listing_path"; then
    echo "Release archive is missing the staged SMAPI runtime bundle." >&2
    return 1
  fi

  if ! grep -q 'stardewvalleymainline/tools/SMAPIBundle/default-mods/ConsoleCommands/manifest.json' "$listing_path"; then
    echo "Release archive is missing the staged bundled SMAPI default mods." >&2
    return 1
  fi

  if ! grep -q 'stardewvalleymainline/Mods/.gitkeep' "$listing_path"; then
    echo "Release archive is missing the external Mods folder placeholder." >&2
    return 1
  fi

  if grep -Eq 'stardewvalleymainline/Mods/PortMasterFixtureMod|FixtureSmapiMod' "$listing_path"; then
    echo "Release archive unexpectedly includes the SMAPI validation fixture mod." >&2
    return 1
  fi

  if grep -q 'stardewvalleymainline/gamedata/Stardew Valley.dll' "$listing_path"; then
    echo "Release archive unexpectedly includes retail Stardew game files." >&2
    return 1
  fi

}

if [ "$VALIDATE_ONLY" -eq 0 ]; then
  fetch_dotnet_runtime
  fetch_skia_native
  fetch_smapi_bundle
  build_framework
  validate_dynamic_audio_api
  validate_monogame_compat_api
  build_patch_dll
  build_game_data_patcher
  build_fixture_smapi_mod
  build_native_shims
  stage_validation_package
fi

if [ "$SKIP_VALIDATION" -eq 0 ] || [ "$VALIDATE_ONLY" -eq 1 ]; then
  run_validation
fi

if [ "$VALIDATE_ONLY" -eq 0 ]; then
  stage_release_package
  create_release_archive
fi
