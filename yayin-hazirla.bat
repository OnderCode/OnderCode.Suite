@echo off
chcp 65001 >nul
rem Onda kutuphanesini lib\Onda'ya kopyalar, DLL ile derlemeyi dogrular, Otel'i tek dosya olarak paketler. Sonuc: yayin.log
cd /d "%~dp0"
echo BASLADI > yayin.log
dotnet build ..\Onda\src\Onda.WinForms\Onda.WinForms.csproj -c Release >> yayin.log 2>&1 || (echo ONDA DERLEME HATASI >> yayin.log & exit /b 1)
if not exist lib\Onda mkdir lib\Onda
copy /y ..\Onda\src\Onda.WinForms\bin\Release\net8.0-windows\Onda.WinForms.dll lib\Onda\ >> yayin.log
copy /y ..\Onda\LICENSE lib\Onda\LICENSE.txt >> yayin.log
dotnet build src\Apps\Otel\Otel.WinForms\Otel.WinForms.csproj -c Release -p:UseOndaDll=true >> yayin.log 2>&1 || (echo DLL ILE DERLEME HATASI >> yayin.log & exit /b 1)
echo DLL ILE DERLEME TAMAM >> yayin.log
if exist publish rmdir /s /q publish
dotnet publish src\Apps\Otel\Otel.WinForms\Otel.WinForms.csproj -c Release -r win-x64 --self-contained true -p:UseOndaDll=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish\otel >> yayin.log 2>&1 || (echo YAYIN HATASI >> yayin.log & exit /b 1)
copy /y docs\BENIOKU-otel.txt publish\otel\BENIOKU.txt >nul
copy /y LICENSE publish\otel\LICENSE.txt >nul
powershell -NoProfile -Command "Compress-Archive -Path 'publish\otel\*' -DestinationPath 'publish\Otel-Otomasyonu-v0.1.zip' -Force" >> yayin.log 2>&1
dir publish\otel >> yayin.log
dir publish\*.zip >> yayin.log
call :findgit
if defined GIT "%GIT%" log -3 --format="%%h %%an <%%ae>" >> yayin.log
echo TAMAM >> yayin.log
exit /b 0

:findgit
set "GIT="
for /f "delims=" %%g in ('where git 2^>nul') do if not defined GIT set "GIT=%%g"
if not defined GIT for /d %%a in ("%LocalAppData%\GitHubDesktop\app-*") do if exist "%%a\resources\app\git\cmd\git.exe" set "GIT=%%a\resources\app\git\cmd\git.exe"
exit /b 0
