@echo off
rem OnderCode Donanim: uygulamayi acar (bu bilgisayarin karnesi)
cd /d "%~dp0"
dotnet run --project src\Apps\Donanim\Donanim.WinForms\Donanim.WinForms.csproj -c Release
