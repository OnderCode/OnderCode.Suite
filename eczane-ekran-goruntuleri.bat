@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Taze ornek veriyle butun ekranlarin goruntusunu docs\eczane klasorune kaydeder.
cd /d "%~dp0"
dotnet build src\Apps\Eczane\Eczane.WinForms\Eczane.WinForms.csproj -c Release > eczane-build.log 2>&1
if errorlevel 1 (
  echo DERLEME HATASI >> eczane-build.log
  exit /b 1
)
"src\Apps\Eczane\Eczane.WinForms\bin\Release\net10.0-windows\OnderCodeEczane.exe" --shots "%~dp0docs\eczane"
echo TAMAM >> eczane-build.log
