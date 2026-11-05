taskkill /F /PID 40984 /T 2>&1 | Out-File 'D:\AI workspace\OpenEQ\evidence\kill.txt' -Encoding UTF8
taskkill /F /PID 54088 /T 2>&1 | Out-File 'D:\AI workspace\OpenEQ\evidence\kill.txt' -Append -Encoding UTF8
'killed' | Out-File 'D:\AI workspace\OpenEQ\evidence\kill.txt' -Append -Encoding UTF8
