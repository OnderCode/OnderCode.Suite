@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem OnderCode Donanim: derleme + testler. Sonuc: logs\donanim-*.log
cd /d "%~dp0"
if not exist logs mkdir logs
taskkill /f /im OnderCodeDonanim.exe >NUL 2>&1
set L=logs\donanim-%RANDOM%.log
dotnet build src\Apps\Donanim\Donanim.WinForms\Donanim.WinForms.csproj -c Release > "%L%.build" 2>&1
echo BUILD %errorlevel% > "%L%"
findstr /c:"error " "%L%.build" >> "%L%"
dotnet test tests\Donanim.Tests\Donanim.Tests.csproj -c Release > "%L%.test" 2>&1
echo TEST %errorlevel% >> "%L%"
findstr /c:"error " /c:"Passed!" /c:"Failed!" /c:"[FAIL]" /c:"Assert" /c:"Exception" /c:"at Donanim" "%L%.test" >> "%L%"
echo BITTI >> "%L%"
