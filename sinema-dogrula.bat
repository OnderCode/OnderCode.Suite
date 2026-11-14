@echo off
set DOTNET_CLI_UI_LANGUAGE=en
cd /d "%~dp0"
if not exist logs mkdir logs
set L=logs\dogrula-%RANDOM%.log
dotnet build src\Apps\Sinema\Sinema.Web\Sinema.Web.csproj -c Release > "%L%.web" 2>&1
echo WEB %errorlevel% > "%L%"
findstr /c:"error " "%L%.web" >> "%L%"
dotnet test tests\Sinema.Tests\Sinema.Tests.csproj -c Release > "%L%.test" 2>&1
echo TEST %errorlevel% >> "%L%"
findstr /c:"error " /c:"Passed!" /c:"Failed!" /c:"[FAIL]" /c:"Assert" /c:"Exception" /c:"at Sinema" "%L%.test" >> "%L%"
echo BITTI >> "%L%"
