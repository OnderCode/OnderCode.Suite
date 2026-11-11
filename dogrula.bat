@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Testler + Otel arayuzu derlemesi + ekran goruntuleri ve ornek raporlar. Ozet: dogrula.log
cd /d "%~dp0"
echo BASLADI > dogrula.log
dotnet test OnderCode.Suite.slnx -c Release > test.log 2>&1
if errorlevel 1 (echo TEST HATASI - test.log >> dogrula.log) else (echo TESTLER GECTI >> dogrula.log)
call "%~dp0otel-ekran-goruntuleri.bat"
findstr /c:"TAMAM" otel-build.log >nul && (echo ARAYUZ TAMAM >> dogrula.log) || (echo ARAYUZ HATASI - otel-build.log >> dogrula.log)
echo BITTI >> dogrula.log
