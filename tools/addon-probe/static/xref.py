from pel import *
import sys,numpy as np
def xrefs(b,targets):
    # scan .text for disp32 that resolves to a target rva (rip-relative: rva_of_next_insn + disp)
    t=b.secs[0]; va,vs,rp,rs=t
    code=np.frombuffer(b.d[rp:rp+min(vs,rs)],dtype=np.uint8)
    n=len(code)-4
    d=np.lib.stride_tricks.sliding_window_view(code,4)
    # int32 little endian
    vals=(d[:,0].astype(np.int64)|(d[:,1].astype(np.int64)<<8)|(d[:,2].astype(np.int64)<<16)|(d[:,3].astype(np.int64)<<24))
    vals=np.where(vals>=2**31,vals-2**32,vals)
    tgt=np.array(sorted(targets),dtype=np.int64)
    res={}
    idx=np.arange(len(vals))
    # next insn rva between +4 and +12 past disp start
    for extra in range(4,12):
        r=va+idx+extra+vals
        m=np.isin(r,tgt)
        for i in np.nonzero(m)[0]:
            res.setdefault(int(r[i]),[]).append(va+int(i)-1)  # approximate insn pos
    return res
