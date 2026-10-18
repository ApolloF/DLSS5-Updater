from ext2 import *
import struct as S
def f(v): return S.unpack('<f',S.pack('<I',v))[0]
def table(name,fn,G,cI,cF):
    b,out,rg=run2(name,fn,G,cI,cF)
    tab={}
    for x in out:
        if x[0]=='GET':
            k=x[2]
            if k is None: continue
            init=x[3]; kind=x[1]
            d=None
            if init and init[0]=='imm' and init[1] is not None:
                d=f(init[1]) if kind=='float' else init[1]
            if init and init[0]=='f': d=init[1]
            tab.setdefault(k,{}).update(kind=kind,init=d)
        elif x[0]=='CLAMPI':
            tab.setdefault(x[1],{}).update(cl='I',dflt=x[2],mn=x[3],mx=x[4])
        else:
            tab.setdefault(x[1],{}).update(cl='F',dflt=x[2],mn=x[3],mx=x[4])
    return tab,out
if __name__=='__main__':
    t,out=table('v107',0x196e6d,{0x881c0:'int',0x88090:'int*',0x882b0:'float',0x88340:'bool',0x2a4260:'cfgget',0x2ad7e0:'str'},0x244e90,0x245810)
    for k in sorted(t,key=str): print(k,t[k])
