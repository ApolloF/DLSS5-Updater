import pefile,struct,sys
class B:
    def __init__(s,f):
        s.d=open(f,'rb').read(); s.pe=pefile.PE(f,fast_load=True); s.ib=s.pe.OPTIONAL_HEADER.ImageBase
        s.secs=[(x.VirtualAddress,x.Misc_VirtualSize,x.PointerToRawData,x.SizeOfRawData) for x in s.pe.sections]
    def off2rva(s,o):
        for va,vs,rp,rs in s.secs:
            if rp<=o<rp+rs: return o-rp+va
    def rva2off(s,r):
        for va,vs,rp,rs in s.secs:
            if va<=r<va+max(vs,rs) and r-va<rs: return r-va+rp
    def ptr2off(s,p):
        if s.ib<=p<s.ib+0x2000000: return s.rva2off(p-s.ib)
    def cstr(s,o,n=200):
        e=s.d.find(b'\0',o,o+n); 
        if e<0: return None
        t=s.d[o:e]
        if len(t)>=1 and all(32<=c<127 or c in (10,) for c in t): return t.decode()
    def describe(s,q):
        o=s.ptr2off(q)
        if o is not None:
            c=s.cstr(o)
            if c: return 'S"%s"'%c[:90]
            return 'P%x'%o
        return None
