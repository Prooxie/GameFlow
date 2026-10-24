@echo off
dotnet build "%~dp0..\GameFlow.sln" --configuration Release -warnaserror %*
exit /b %errorlevel%
