@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Eczane cekirdek katmanlari ve testleri. Sonuc: eczane-test.log
cd /d "%~dp0"
dotnet test tests\Eczane.Tests\Eczane.Tests.csproj -c Release > eczane-test-full.log 2>&1
echo CIKIS KODU %errorlevel% > eczane-test.log
findstr /c:"error CS" /c:"Passed!" /c:"Failed!" /c:"[FAIL]" /c:"Assert" /c:"Exception" eczane-test-full.log >> eczane-test.log
