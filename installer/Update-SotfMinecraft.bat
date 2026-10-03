@echo off
rem Always fetches and runs the newest updater straight from GitHub, so this file never needs replacing.
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ((New-Object Net.WebClient).DownloadString('https://raw.githubusercontent.com/TheJamesOfTheWest/mod/claude/festive-gauss-vsfids/installer/update.ps1'))"
pause
