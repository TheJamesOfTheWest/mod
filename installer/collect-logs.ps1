# Gathers every log that matters into ONE zip on your Desktop: sotf-logs.zip
$ErrorActionPreference = 'Continue'
$out = Join-Path $env:TEMP 'sotf-logs'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$candidates = @("C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest")
try {
  $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
  $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) { foreach ($m in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"')) { $candidates += (Join-Path ($m.Matches[0].Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Sons Of The Forest') } }
} catch { }
$game = $candidates | Where-Object { Test-Path (Join-Path $_ 'SonsOfTheForest.exe') } | Select-Object -First 1
function Grab($src, $name) { if ($src -and (Test-Path $src)) { Copy-Item $src (Join-Path $out $name) -Force -ErrorAction SilentlyContinue } }

if ($game) {
  Grab "$game\BepInEx\LogOutput.log" 'bepinex-LogOutput.log'
  Grab "$game\BepInEx\ErrorLog.log" 'bepinex-ErrorLog.log'
  Grab "$game\BepInEx\sotf-debug.log" 'sotf-debug.log'
  Grab "$game\BepInEx\sotf-debug.log.old" 'sotf-debug.log.old'
  Grab "$game\ReShade.log" 'reshade.log'
  Grab "$game\ReShade.ini" 'ReShade.ini'
  Grab "$game\ReShadePreset.ini" 'ReShadePreset.ini'
}
Grab "$env:USERPROFILE\AppData\LocalLow\Endnight\SonsOfTheForest\Player.log" 'unity-Player.log'
Grab "$env:USERPROFILE\AppData\LocalLow\Endnight\SonsOfTheForest\Player-prev.log" 'unity-Player-prev.log'
$mc = Join-Path $env:APPDATA 'ModrinthApp\profiles\SOTF Passthrough'
Grab "$mc\logs\latest.log" 'minecraft-latest.log'
if (Test-Path "$mc\crash-reports") { Get-ChildItem "$mc\crash-reports" | Sort-Object LastWriteTime -Descending | Select-Object -First 2 | ForEach-Object { Grab $_.FullName ('minecraft-crash-' + $_.Name) } }

$info = @()
$info += "Collected: $(Get-Date)"
$info += "Game folder: $game"
try { $info += "OS: " + (Get-CimInstance Win32_OperatingSystem).Caption } catch { }
try { Get-CimInstance Win32_VideoController | ForEach-Object { $info += "GPU: $($_.Name) driver $($_.DriverVersion)" } } catch { }
try { $info += "dotnet: " + (& dotnet --version) } catch { }
if ($game) {
  foreach ($f in @('SotfPassthrough.addon64', 'MCPassthrough.fx', 'BepInEx\plugins\SotfPassthrough.dll')) {
    $p = Join-Path $game $f
    if (Test-Path $p) { $i = Get-Item $p; $info += "$f  $($i.Length) bytes  modified $($i.LastWriteTime)" } else { $info += "$f  MISSING" }
  }
}
$jar = Get-ChildItem "$mc\mods" -Filter 'passthrough-*.jar' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($jar) { $info += "Minecraft mod: $($jar.Name)  $($jar.Length) bytes  modified $($jar.LastWriteTime)" } else { $info += "Minecraft mod: MISSING in $mc\mods" }
$info | Out-File (Join-Path $out 'info.txt') -Encoding utf8

$zip = Join-Path ([Environment]::GetFolderPath('Desktop')) 'sotf-logs.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force
Write-Host ""
Write-Host "Done: $zip" -ForegroundColor Green
Write-Host "Attach that file in the chat with Claude."
Start-Process explorer.exe "/select,`"$zip`""
