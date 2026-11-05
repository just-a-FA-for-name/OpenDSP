import re
p1='D:/AI workspace/OpenEQ/windows/OpenEQ.Apo/checks.cpp'
s=open(p1,encoding='utf-8').read()
s=s.replace('shared->version=1;','shared->version=AbiVersion;')
open(p1,'w',encoding='utf-8').write(s)
p2='D:/AI workspace/OpenEQ/windows/OpenEQ.Apo/apo.cpp'
a=open(p2,encoding='utf-8').read()
a=a.replace('if(hr!=S_OK)return APOERR_INVALID_CONNECTION_FORMAT;','if(hr!=S_OK){trace("lock-fail-supported",hr);return APOERR_INVALID_CONNECTION_FORMAT;}')
a=a.replace('if(out[0]->u32MaxFrameCount<in[0]->u32MaxFrameCount)return APOERR_INVALID_OUTPUT_MAXFRAMECOUNT;','if(out[0]->u32MaxFrameCount<in[0]->u32MaxFrameCount){trace("lock-fail-frames");return APOERR_INVALID_OUTPUT_MAXFRAMECOUNT;}')
a=a.replace('catch(...){return E_OUTOFMEMORY;}','catch(...){trace("lock-fail-oom");return E_OUTOFMEMORY;}')
a=a.replace('if(ni!=1||no!=1||!in||!out||!in[0]||!out[0])return APOERR_NUM_CONNECTIONS_INVALID;','if(ni!=1||no!=1||!in||!out||!in[0]||!out[0]){trace("lock-fail-conn");return APOERR_NUM_CONNECTIONS_INVALID;}')
open(p2,'w',encoding='utf-8').write(a)
print('patched both')
