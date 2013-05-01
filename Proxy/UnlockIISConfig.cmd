%windir%\system32\inetsrv\appcmd.exe unlock config /section:system.webServer/security/access
%windir%\system32\inetsrv\appcmd.exe set config -section:applicationPools -applicationPoolDefaults.failure.rapidFailProtection:"False" /commit:apphost
%windir%\system32\inetsrv\appcmd.exe set config -section:applicationPools -applicationPoolDefaults.queueLength:"65535" /commit:apphost
if ERRORLEVEL 1 goto ERROR

goto DONE

:ERROR

echo.
echo ERROR: Failed to unlock SSL config section
echo.

exit /b 1

:DONE
