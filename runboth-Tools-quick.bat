@echo off
setlocal
start "Klovnstation Client" cmd /k "dotnet run --project Content.Client --configuration Tools --no-build %*"
start "Klovnstation Server" cmd /k "dotnet run --project Content.Server --configuration Tools --no-build %*"
exit /b 0
