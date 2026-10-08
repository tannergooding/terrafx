@ECHO OFF
pwsh.exe -NoLogo -NoProfile -File "%~dp0build.ps1" -ci %*
EXIT /B %ERRORLEVEL%
