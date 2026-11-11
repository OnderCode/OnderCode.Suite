@echo off
chcp 65001 >nul
rem Otel otomasyonunu derleyip açar. İlk açılışta örnek verili veritabanı %LocalAppData%\OnderCode\Otel klasöründe oluşur.
cd /d "%~dp0"
dotnet build src\Apps\Otel\Otel.WinForms\Otel.WinForms.csproj -c Release > otel-build.log 2>&1
if errorlevel 1 (
  echo DERLEME HATASI >> otel-build.log
  exit /b 1
)
start "" "src\Apps\Otel\Otel.WinForms\bin\Release\net10.0-windows\OtelOtomasyonu.exe"
