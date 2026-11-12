@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Taze ornek veriyle butun ekranlarin goruntusunu docs\otopark klasorune kaydeder.
cd /d "%~dp0"
dotnet build src\Apps\Otopark\Otopark.WinForms\Otopark.WinForms.csproj -c Release > otopark-build.log 2>&1
if errorlevel 1 (
  echo DERLEME HATASI >> otopark-build.log
  exit /b 1
)
"src\Apps\Otopark\Otopark.WinForms\bin\Release\net10.0-windows\OnderCodeOtopark.exe" --shots "%~dp0docs\otopark"
echo TAMAM >> otopark-build.log
