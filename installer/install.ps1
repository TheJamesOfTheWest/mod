# Minecraft x Sons of the Forest: one-click installer.
# Finds your game and Minecraft folders, installs BepInEx if needed, copies the ReShade add-on and effect,
# builds the plugin against your game, and copies the Minecraft mod. Safe to run again after every update.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
function Step($t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t) { Write-Host "   OK  $t" -ForegroundColor Green }
function Warn($t) { Write-Host "   !!  $t" -ForegroundColor Yellow }

Step "1/6  Finding Sons of the Forest"
$candidates = @("C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest")
try {
  $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
  $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) {
    foreach ($m in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"')) {
      $lib = $m.Matches[0].Groups[1].Value -replace '\\\\', '\'
      $candidates += (Join-Path $lib 'steamapps\common\Sons Of The Forest')
    }
  }
} catch { }
$game = $candidates | Where-Object { Test-Path (Join-Path $_ 'SonsOfTheForest.exe') } | Select-Object -First 1
if (-not $game) {
  $game = (Read-Host "Couldn't find it. Paste the Sons of the Forest folder (the one containing SonsOfTheForest.exe)").Trim('"')
  if (-not (Test-Path (Join-Path $game 'SonsOfTheForest.exe'))) { throw "SonsOfTheForest.exe is not in '$game'" }
}
Ok $game

Step "2/6  BepInEx (the mod loader)"
if (-not (Test-Path (Join-Path $game 'winhttp.dll'))) {
  Write-Host "   Downloading BepInEx 6.0.755 ..."
  $zip = Join-Path $env:TEMP 'BepInExPack_IL2CPP.zip'
  Invoke-WebRequest -Uri 'https://thunderstore.io/package/download/BepInEx/BepInExPack_IL2CPP/6.0.755/' -OutFile $zip
  $tmp = Join-Path $env:TEMP 'bepinex_unpack'
  if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
  Expand-Archive -Path $zip -DestinationPath $tmp -Force
  Copy-Item -Path (Join-Path $tmp 'BepInExPack\*') -Destination $game -Recurse -Force
  Ok "BepInEx installed"
} else { Ok "already installed" }
if (-not (Test-Path (Join-Path $game 'BepInEx\interop\UnityEngine.CoreModule.dll'))) {
  Warn "BepInEx has not generated its game files yet."
  Write-Host "   Start Sons of the Forest once, wait on the main menu for a few minutes, quit, then run this installer again."
  Read-Host "Press Enter to close"
  exit 0
}
Ok "game files found"

Step "3/6  ReShade (draws Minecraft into the game)"
if (-not (Test-Path (Join-Path $game 'ReShade.ini'))) {
  Warn "ReShade is not installed."
  Write-Host "   1) Download ReShade_Setup_6.8.0_Addon.exe from https://reshade.me  (the Addon version)"
  Write-Host "   2) Run it, click 'Select game', pick SonsOfTheForest.exe, choose Direct3D 10/11/12, skip the extras."
  Write-Host "   3) Run this installer again."
  Read-Host "Press Enter to close"
  exit 0
}
Ok "ReShade found"

Step "4/6  Add-on and effect"
Expand-Archive -Path (Join-Path $here 'sotf-addon.zip') -DestinationPath $game -Force
Ok "SotfPassthrough.addon64, MCPassthrough.fx and helper files copied"

Step "5/6  Building the plugin for your game"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Warn "The .NET SDK is missing. Trying to install it (a prompt may appear)..."
  try { winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements } catch { }
  $env:Path += ";C:\Program Files\dotnet"
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Warn "Install the .NET 8 SDK from https://dotnet.microsoft.com/en-us/download then run this again."
    Read-Host "Press Enter to close"
    exit 1
  }
}
$log = Join-Path $here 'build-log.txt'
& dotnet build (Join-Path $here 'sotf\SotfPassthrough.csproj') -c Release "-p:GameDir=$game" *> $log
if ($LASTEXITCODE -ne 0) {
  Warn "The plugin did not build. Send the file build-log.txt (in this folder) to Claude."
  Read-Host "Press Enter to close"
  exit 1
}
Ok "plugin built and copied into BepInEx\plugins"

Step "6/6  Minecraft mod"
$jar = Join-Path $here 'passthrough-0.1.0.jar'
# Your Minecraft profile: Modrinth's "SOTF Passthrough".
$mods = Join-Path $env:APPDATA 'ModrinthApp\profiles\SOTF Passthrough\mods'
if (-not (Test-Path $mods)) {
  Warn "Couldn't find $mods"
  $mods = (Read-Host "Paste the path of your profile's mods folder (the one with fabric-api inside), or press Enter to skip").Trim('"')
}
if ($mods -and (Test-Path $mods)) {
  Get-ChildItem $mods -Filter 'passthrough-*.jar' -ErrorAction SilentlyContinue | Remove-Item -Force
  Copy-Item $jar $mods -Force
  Ok "Minecraft mod copied to $mods"
  if (-not (Get-ChildItem $mods -Filter 'fabric-api*.jar' -ErrorAction SilentlyContinue)) { Warn "No fabric-api jar in that folder: add Fabric API 0.161.0+26.3" }
} else { Warn "Skipped the Minecraft mod: copy passthrough-0.1.0.jar into your profile's mods folder by hand." }

Write-Host ""
Write-Host "DONE." -ForegroundColor Green
Write-Host @"

HOW TO PLAY
  1. Start Minecraft first (Modrinth profile 'SOTF Passthrough'), leave it open behind.
  2. Start Sons of the Forest (borderless windowed) and load a world.
  3. Press Home once and tick 'MCPassthrough' in ReShade (first time only).

KEYS (in the game)
  F7   Minecraft mode on/off (mouse buttons, number keys, wheel go to Minecraft)
  F6   Steve drives: Minecraft's physics move you (turns on F7 too)
  E    Minecraft inventory (type to search, wheel to scroll, Esc to close)
  F10  test explosion      F11  panic: undo everything
"@
Read-Host "Press Enter to close"
