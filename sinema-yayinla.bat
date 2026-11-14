@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Kurulumsuz paket: publish\OnderCode-Sinema-v1.0.zip (Windows x64, .NET kurulu olmasi gerekmez)
cd /d "%~dp0"
taskkill /f /im OnderCodeSinema.exe >NUL 2>&1
if not exist logs mkdir logs
set L=logs\yayinla-%RANDOM%.log
set P=%~dp0publish\OnderCode-Sinema
if exist "%P%" rmdir /s /q "%P%"
rem Onda.Web yedek kutuphanesi (Onda deposu olmadan derleyebilmek icin)
dotnet build ..\Onda\src\Onda.Web\Onda.Web.csproj -c Release >> "%L%.full" 2>&1
if not exist lib\Onda mkdir lib\Onda
copy /y ..\Onda\src\Onda.Web\bin\Release\net8.0\Onda.Web.dll lib\Onda\Onda.Web.dll >> "%L%.full" 2>&1
dotnet publish src\Apps\Sinema\Sinema.Web\Sinema.Web.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o "%P%" > "%L%.full" 2>&1
echo PUBLISH %errorlevel% > "%L%"
findstr /c:"error " "%L%.full" >> "%L%"
if exist "%P%\App_Data" rmdir /s /q "%P%\App_Data"
(
echo @echo off
echo cd /d "%%~dp0"
echo echo OnderCode Sinema baslatiliyor: http://localhost:5280
echo start "" http://localhost:5280
echo "%%~dp0OnderCodeSinema.exe" --urls http://localhost:5280
) > "%P%\Baslat.bat"
(
echo OnderCode Sinema - kurulumsuz demo
echo.
echo 1. Baslat.bat dosyasini calistirin.
echo 2. Tarayicida http://localhost:5280 acilir.
echo 3. Personel ekranlari: sag ustteki "Personel girisi" - parola yoktur.
echo.
echo Ilk calistirmada ornek veri olusturulur (App_Data klasoru^).
echo Filmler, afisler ve kisiler tamamen kurgudur. Odeme alinmaz.
echo Kaynak kod: https://github.com/OnderCode/OnderCode.Suite
) > "%P%\BENIOKU.txt"
if exist publish\OnderCode-Sinema-v1.0.zip del publish\OnderCode-Sinema-v1.0.zip
powershell -NoProfile -Command "Compress-Archive -Path '%P%\*' -DestinationPath '%~dp0publish\OnderCode-Sinema-v1.0.zip'" >> "%L%" 2>&1
echo ZIP %errorlevel% >> "%L%"
for %%f in (publish\OnderCode-Sinema-v1.0.zip) do echo BOYUT %%~zf >> "%L%"
echo BITTI >> "%L%"
