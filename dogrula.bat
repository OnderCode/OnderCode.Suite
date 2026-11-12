@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Testler + Otel, Otopark ve Eczane arayuz derlemesi + ekran goruntuleri ve ornek raporlar. Ozet: dogrula.log
cd /d "%~dp0"
echo BASLADI > dogrula.log
dotnet test OnderCode.Suite.slnx -c Release > test.log 2>&1
if errorlevel 1 (echo TEST HATASI - test.log >> dogrula.log) else (echo TESTLER GECTI >> dogrula.log)
findstr /c:"Passed!" /c:"Failed!" /c:"error CS" test.log >> dogrula.log
call "%~dp0otel-ekran-goruntuleri.bat"
findstr /c:"TAMAM" otel-build.log >nul && (echo OTEL ARAYUZ TAMAM >> dogrula.log) || (echo OTEL ARAYUZ HATASI - otel-build.log >> dogrula.log)
call "%~dp0otopark-ekran-goruntuleri.bat"
findstr /c:"TAMAM" otopark-build.log >nul && (echo OTOPARK ARAYUZ TAMAM >> dogrula.log) || (echo OTOPARK ARAYUZ HATASI - otopark-build.log >> dogrula.log)
call "%~dp0eczane-ekran-goruntuleri.bat"
findstr /c:"TAMAM" eczane-build.log >nul && (echo ECZANE ARAYUZ TAMAM >> dogrula.log) || (echo ECZANE ARAYUZ HATASI - eczane-build.log >> dogrula.log)
if exist docs\eczane\hata.txt echo ECZANE EKRAN HATASI - docs\eczane\hata.txt >> dogrula.log
if exist docs\otopark\hata.txt echo OTOPARK EKRAN HATASI - docs\otopark\hata.txt >> dogrula.log
if exist docs\otel\hata.txt echo OTEL EKRAN HATASI - docs\otel\hata.txt >> dogrula.log
echo BITTI >> dogrula.log
