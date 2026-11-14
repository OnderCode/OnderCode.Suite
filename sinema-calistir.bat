@echo off
rem OnderCode Sinema: http://localhost:5280
cd /d "%~dp0src\Apps\Sinema\Sinema.Web"
dotnet run -c Release --urls http://localhost:5280
