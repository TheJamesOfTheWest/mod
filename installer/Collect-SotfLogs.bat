@echo off
rem Fetches the newest log collector from GitHub and runs it; the result is sotf-logs.zip on your Desktop.
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ((New-Object Net.WebClient).DownloadString('https://raw.githubusercontent.com/TheJamesOfTheWest/mod/claude/festive-gauss-vsfids/installer/collect-logs.ps1'))"
pause
