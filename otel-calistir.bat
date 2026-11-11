@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Otel otomasyonunu derleyip acar. Ilk acilista ornek verili veritabani %LocalAppData%\OnderCode\Otel klasorunde olusur.
cd /d "%~dp0"
dotnet build src\Apps\Otel\Otel.WinForms\Otel.WinForms.csproj -c Release > otel-build.log 2>&1
if errorlevel 1 (
  echo DERLEME HATASI >> otel-build.log
  exit /b 1
)
start "" "src\Apps\Otel\Otel.WinForms\bin\Release\net10.0-windows\OnderCodeOtel.exe"
