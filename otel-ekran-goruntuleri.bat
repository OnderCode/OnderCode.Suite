@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Taze ornek veriyle butun ekranlarin goruntusunu docs\otel klasorune kaydeder.
cd /d "%~dp0"
dotnet build src\Apps\Otel\Otel.WinForms\Otel.WinForms.csproj -c Release > otel-build.log 2>&1
if errorlevel 1 (
  echo DERLEME HATASI >> otel-build.log
  exit /b 1
)
"src\Apps\Otel\Otel.WinForms\bin\Release\net10.0-windows\OnderCodeOtel.exe" --shots "%~dp0docs\otel"
echo TAMAM >> otel-build.log
