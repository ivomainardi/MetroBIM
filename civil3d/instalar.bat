@echo off
rem Copia o bundle para a pasta de plugins do usuario. Reinicie o Civil 3D depois.
set DEST=%APPDATA%\Autodesk\ApplicationPlugins\MetroSP.bundle
if not exist "%~dp0MetroSP.bundle\Contents\R25\MetroSP.dll" (
  echo MetroSP.dll nao encontrada. Compile antes: dotnet build src\MetroSP.csproj -c Release
  pause
  exit /b 1
)
xcopy "%~dp0MetroSP.bundle" "%DEST%\" /E /I /Y >nul
echo Instalado em %DEST%
pause
