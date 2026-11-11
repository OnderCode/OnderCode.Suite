@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Derler ve butun testleri calistirir. Sonuc: test.log
cd /d "%~dp0"
dotnet test OnderCode.Suite.slnx -c Release > test.log 2>&1
echo CIKIS KODU %errorlevel% >> test.log
