@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Sinema: Onda.Web + katmanlar + testler. Sonuc: logs\sinema-test-*.log
cd /d "%~dp0"
if not exist logs mkdir logs
set L=logs\sinema-test-%RANDOM%.log
dotnet test tests\Sinema.Tests\Sinema.Tests.csproj -c Release > "%L%.full" 2>&1
echo CIKIS KODU %errorlevel% > "%L%"
findstr /c:"error CS" /c:"error RZ" /c:"error NU" /c:"Passed!" /c:"Failed!" /c:"[FAIL]" /c:"Assert" /c:"Exception" "%L%.full" >> "%L%"
