@echo off
set DOTNET_CLI_UI_LANGUAGE=en
rem Sinema web uygulamasini derler ve yayin klasorune cikarir. Sonuc: logs\sinema-build-*.log
cd /d "%~dp0"
if not exist logs mkdir logs
set L=logs\sinema-build-%RANDOM%.log
dotnet build src\Apps\Sinema\Sinema.Web\Sinema.Web.csproj -c Release > "%L%.full" 2>&1
echo BUILD %errorlevel% > "%L%"
findstr /c:"error " /c:"warning " /c:"Build succeeded" "%L%.full" >> "%L%"
