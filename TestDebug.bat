@echo off
IF [%1] == [] GOTO Usage
cd src\Test.Server\bin\debug\net7.0
start Test.Server.exe
TIMEOUT 4 > NUL
cd ..\..\..\..\..

cd src\Test.Client\bin\debug\net7.0
FOR /L %%i IN (1,1,%1) DO (
ECHO Starting client %%i
start Test.Client.exe
TIMEOUT 2 > NUL
cd ..\..\..\..\..
@echo on
EXIT /b

:Usage
ECHO Specify the number of client nodes to start.
@echo on
EXIT /b