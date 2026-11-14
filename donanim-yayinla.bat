@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Kurulumsuz paket: publish\OnderCode-Donanim-v1.0.zip (Windows x64, .NET kurulumu gerekmez)
cd /d "%~dp0"
taskkill /f /im OnderCodeDonanim.exe >NUL 2>&1
if not exist logs mkdir logs
set L=logs\donanim-yayin-%RANDOM%.log
set P=%~dp0publish\OnderCode-Donanim
if exist "%P%" rmdir /s /q "%P%"
dotnet publish src\Apps\Donanim\Donanim.WinForms\Donanim.WinForms.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%P%" > "%L%.full" 2>&1
echo PUBLISH %errorlevel% > "%L%"
findstr /c:"error " "%L%.full" >> "%L%"
(
echo OnderCode Donanim - bilgisayarin donanim karnesi
echo.
echo OnderCodeDonanim.exe dosyasini calistirin. Kurulum gerekmez.
echo Disk sicakligi ve TPM bilgisi icin sag tik - Yonetici olarak calistir.
echo.
echo Uygulama internete baglanmaz; seri numarasi ve MAC adresi okumaz.
echo Kaynak kod: https://github.com/OnderCode/OnderCode.Suite
) > "%P%\BENIOKU.txt"
if exist publish\OnderCode-Donanim-v1.0.zip del publish\OnderCode-Donanim-v1.0.zip
powershell -NoProfile -Command "Compress-Archive -Path '%P%\*' -DestinationPath '%~dp0publish\OnderCode-Donanim-v1.0.zip'" >> "%L%" 2>&1
for %%f in (publish\OnderCode-Donanim-v1.0.zip) do echo BOYUT %%~zf >> "%L%"
if not exist "%~dp0..\site-donanim" mkdir "%~dp0..\site-donanim"
copy /y publish\OnderCode-Donanim-v1.0.zip "%~dp0..\site-donanim\" >> "%L%"
echo BITTI >> "%L%"
