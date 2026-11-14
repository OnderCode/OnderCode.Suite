@echo off
rem Bu bilgisayarin gercek taramasi (denetim icin, yayinlanmaz): logs\tara-*
cd /d "%~dp0"
if not exist logs mkdir logs
taskkill /f /im OnderCodeDonanim.exe >NUL 2>&1
set DOTNET_CLI_UI_LANGUAGE=en
dotnet build src\Apps\Donanim\Donanim.WinForms\Donanim.WinForms.csproj -c Release > logs\tara-build.log 2>&1
set R=%RANDOM%
src\Apps\Donanim\Donanim.WinForms\bin\Release\net10.0-windows\OnderCodeDonanim.exe --tara logs\tara-%R%
echo logs\tara-%R% > logs\tara-son.txt
