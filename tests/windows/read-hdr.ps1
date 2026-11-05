$path='C:\ProgramData\OpenEQ\apo\{8C283559-D27C-40D8-9F30-ACE6390D2979}.bin'
$b=[IO.File]::ReadAllBytes($path)
"magic=0x{0:X8} version={1} size={2}" -f [BitConverter]::ToUInt32($b,0),[BitConverter]::ToUInt32($b,4),[BitConverter]::ToUInt32($b,12)
