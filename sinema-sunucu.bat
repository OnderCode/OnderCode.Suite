@echo off
taskkill /f /im OnderCodeSinema.exe >NUL 2>&1
cd /d "%~dp0src\Apps\Sinema\Sinema.Web"
if not exist "%~dp0logs" mkdir "%~dp0logs"
set ASPNETCORE_ENVIRONMENT=Development
start "OnderCode Sinema" /min cmd /c "dotnet run -c Release --no-launch-profile --urls http://localhost:5280 > "%~dp0logs\sunucu-%RANDOM%.log" 2>&1"
