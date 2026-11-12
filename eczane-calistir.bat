@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Eczane otomasyonunu derleyip acar. Ilk acilista ornek verili veritabani %LocalAppData%\OnderCode\Eczane klasorunde olusur.
cd /d "%~dp0"
dotnet build src\Apps\Eczane\Eczane.WinForms\Eczane.WinForms.csproj -c Release > eczane-build.log 2>&1
if errorlevel 1 (
  echo DERLEME HATASI >> eczane-build.log
  exit /b 1
)
start "" "src\Apps\Eczane\Eczane.WinForms\bin\Release\net10.0-windows\OnderCodeEczane.exe"
