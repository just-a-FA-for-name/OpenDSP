$p1='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$s=[IO.File]::ReadAllText($p1)
$s=$s.Replace('shared->version=1;','shared->version=AbiVersion;')
[IO.File]::WriteAllText($p1,$s)
$p2='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\apo.cpp'
$a=[IO.File]::ReadAllText($p2)
$a=$a.Replace('if(hr!=S_OK)return APOERR_INVALID_CONNECTION_FORMAT;','if(hr!=S_OK){trace("lock-fail-supported",hr);return APOERR_INVALID_CONNECTION_FORMAT;}')
$a=$a.Replace('if(out[0]->u32MaxFrameCount<in[0]->u32MaxFrameCount)return APOERR_INVALID_OUTPUT_MAXFRAMECOUNT;','if(out[0]->u32MaxFrameCount<in[0]->u32MaxFrameCount){trace("lock-fail-frames");return APOERR_INVALID_OUTPUT_MAXFRAMECOUNT;}')
$a=$a.Replace('catch(...){return E_OUTOFMEMORY;}','catch(...){trace("lock-fail-oom");return E_OUTOFMEMORY;}')
$a=$a.Replace('if(ni!=1||no!=1||!in||!out||!in[0]||!out[0])return APOERR_NUM_CONNECTIONS_INVALID;','if(ni!=1||no!=1||!in||!out||!in[0]||!out[0]){trace("lock-fail-conn");return APOERR_NUM_CONNECTIONS_INVALID;}')
[IO.File]::WriteAllText($p2,$a)
'patched'
