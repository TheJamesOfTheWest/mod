@echo off
rem One-click build: compiles the plugin against YOUR game's files and copies it into BepInEx\plugins.
rem Edit GAME if your Sons of the Forest is somewhere else.
set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest"
if not exist "%GAME%\BepInEx\interop\UnityEngine.CoreModule.dll" (
  echo Cannot find "%GAME%\BepInEx\interop". Edit GAME in this file, and run the game once with BepInEx installed.
  pause & exit /b 1
)
where dotnet >nul 2>nul || (echo Install the .NET 8 SDK from https://dotnet.microsoft.com/en-us/download & pause & exit /b 1)
dotnet build "%~dp0SotfPassthrough.csproj" -c Release -p:GameDir="%GAME%" > "%~dp0build-log.txt" 2>&1
type "%~dp0build-log.txt"
echo.
echo ============================================================
echo If it says "Build succeeded", the plugin is in %GAME%\BepInEx\plugins
echo If it failed, send the file build-log.txt (next to this script) to Claude.
pause
