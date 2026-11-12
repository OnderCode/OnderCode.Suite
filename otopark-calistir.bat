@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Otopark otomasyonunu derleyip acar. Ilk acilista ornek verili veritabani %LocalAppData%\OnderCode\Otopark klasorunde olusur.
cd /d "%~dp0"
dotnet build src\Apps\Otopark\Otopark.WinForms\Otopark.WinForms.csproj -c Release > otopark-build.log 2>&1
if errorlevel 1 (
  echo DERLEME HATASI >> otopark-build.log
  exit /b 1
)
start "" "src\Apps\Otopark\Otopark.WinForms\bin\Release\net10.0-windows\OnderCodeOtopark.exe"
