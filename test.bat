@echo off
chcp 65001 >nul
rem Derler ve bütün testleri çalıştırır. Sonuç: test.log
cd /d "%~dp0"
dotnet test OnderCode.Suite.slnx -c Release > test.log 2>&1
echo CIKIS KODU %errorlevel% >> test.log
