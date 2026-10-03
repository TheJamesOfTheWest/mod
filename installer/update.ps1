# Downloads the newest Minecraft x Sons of the Forest build from GitHub and installs it. No git or login needed.
$ErrorActionPreference = 'Stop'
$repo = 'TheJamesOfTheWest/mod'
$branch = 'claude/festive-gauss-vsfids'
$base = Join-Path $env:LOCALAPPDATA 'SotfMinecraft'
New-Item -ItemType Directory -Force -Path $base | Out-Null
Write-Host "Checking for the newest version..." -ForegroundColor Cyan
try {
  $c = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/commits/$branch" -Headers @{ 'User-Agent' = 'sotf-updater' }
  Write-Host ("Latest change: " + ($c.commit.message -split "`n")[0]) -ForegroundColor Green
} catch { }
$zip = Join-Path $base 'latest.zip'
Invoke-WebRequest -Uri "https://codeload.github.com/$repo/zip/refs/heads/$branch" -OutFile $zip
$dir = Join-Path $base 'latest'
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $dir -Force
$root = Get-ChildItem $dir -Directory | Select-Object -First 1
# keep a desktop shortcut so updating is one double-click from now on
try {
  $bat = Join-Path $base 'Update-SotfMinecraft.bat'
  Copy-Item (Join-Path $root.FullName 'installer\Update-SotfMinecraft.bat') $bat -Force
  $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Update Minecraft x SotF.lnk'
  if (-not (Test-Path $lnk)) {
    $sh = New-Object -ComObject WScript.Shell
    $sc = $sh.CreateShortcut($lnk); $sc.TargetPath = $bat; $sc.WorkingDirectory = $base; $sc.Save()
    Write-Host "Added a desktop shortcut: 'Update Minecraft x SotF'" -ForegroundColor Green
  }
} catch { }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root.FullName 'installer\install.ps1')
