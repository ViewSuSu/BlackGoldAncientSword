@echo off
REM build_native.bat ? Compile wgc_capture.dll using C++/WinRT
REM Automatically finds Visual Studio via vswhere.
setlocal enabledelayedexpansion

:: Find Visual Studio
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo vswhere not found. Please install Visual Studio 2022+ with C++ workload.
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -property installationPath`) do set "VSPATH=%%i"
if "%VSPATH%"=="" (
    echo Visual Studio not found.
    exit /b 1
)

echo Found VS: %VSPATH%

:: Find vcvars64.bat
set "VCVARS="
for /r "%VSPATH%" %%f in (vcvars64.bat) do (
    set "VCVARS=%%f"
    goto :gotvcvars
)
:gotvcvars
if "%VCVARS%"=="" (
    echo vcvars64.bat not found in %VSPATH%
    exit /b 1
)

:: Find latest Windows SDK with C++/WinRT
set "CPPWINRT="
for /d %%d in ("%ProgramFiles(x86)%\Windows Kits\10\Include\10.*") do (
    if exist "%%d\cppwinrt\winrt\Windows.Graphics.Capture.h" (
        set "CPPWINRT=%%d\cppwinrt"
    )
)
if "%CPPWINRT%"=="" (
    echo Windows SDK with C++/WinRT not found.
    exit /b 1
)

echo SDK cppwinrt: %CPPWINRT%

:: Compile
call "%VCVARS%" >nul 2>&1
if errorlevel 1 (echo vcvars64.bat failed & exit /b 1)

cd /d "%~dp0"
cl /nologo /LD /O2 /EHsc /std:c++17 /await /I"%CPPWINRT%" wgc_capture.cpp /Fe:wgc_capture.dll /link d3d11.lib dxgi.lib ole32.lib user32.lib runtimeobject.lib WindowsApp.lib

if %errorlevel% neq 0 (echo FAILED & exit /b 1)

echo.
echo ============================================
echo   wgc_capture.dll built successfully!
echo ============================================
dir wgc_capture.dll

:: Copy to runtime distribution folder
set "RUNTIMES=%~dp0..\runtimes\win-x64\native"
if not exist "%RUNTIMES%" mkdir "%RUNTIMES%"
copy /y wgc_capture.dll "%RUNTIMES%\"
echo Copied to %RUNTIMES%
