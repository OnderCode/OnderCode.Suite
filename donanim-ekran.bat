@echo off
rem Ekran goruntuleri (docs\donanim) ve tanitim videosu kareleri (logs\video-*). Ornek bilgisayarlar kullanilir.
cd /d "%~dp0"
if not exist logs mkdir logs
taskkill /f /im OnderCodeDonanim.exe >NUL 2>&1
set EXE=src\Apps\Donanim\Donanim.WinForms\bin\Release\net10.0-windows\OnderCodeDonanim.exe
set R=%RANDOM%
if exist docs\donanim\raw rmdir /s /q docs\donanim\raw
"%EXE%" --shots docs\donanim\raw
"%EXE%" --video logs\video-%R%
echo logs\video-%R% > logs\video-son.txt
powershell -NoProfile -Command "Compress-Archive -Force -Path 'logs\video-%R%\*' -DestinationPath 'logs\video-%R%.zip'" > logs\video-zip.log 2>&1
echo BITTI >> logs\video-son.txt
