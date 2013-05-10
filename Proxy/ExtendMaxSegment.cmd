@echo off
setlocal
set regpath=HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\services\HTTP\Parameters
reg query "%regpath%" /v "AllowRestrictedChars"
if errorlevel 1 (
   reg add %regpath% /v AllowRestrictedChars /t REG_DWORD /d 00000001
   reg add %regpath% /v UrlSegmentMaxCount /t REG_DWORD /d 00000000
   reg add %regpath% /v PercentUAllowed /t REG_DWORD /d 00000001
   reg add %regpath% /v UrlSegmentMaxLength /t REG_DWORD /d 00000000
   shutdown /r /t 0
)
