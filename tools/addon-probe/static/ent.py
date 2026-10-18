from pel import *
import re,sys
def entries(name):
    b=B(name+'.bin')
    out=[]
    for line in open(name+'.keys.txt'):
        o,k=line.split(); o=int(o,16)
        if not k.startswith(('NR','DX11','Enable','Foreign','Studio','Hotkeys','Language','LocalTone','Skin','UICorr','LookPreset')): continue
        n=len(k)+1; pad=(n+7)//8*8
        nxt,val,z1,z2=struct.unpack_from('<4Q',b.d,o+pad)
        hf,hk=struct.unpack_from('<fi',b.d,o-8)
        nk=b.cstr(b.ptr2off(nxt)) if b.ptr2off(nxt) else None
        vo=b.ptr2off(val)
        vi=struct.unpack_from('<i',b.d,vo)[0] if vo else None
        vf=struct.unpack_from('<f',b.d,vo)[0] if vo else None
        out.append((o,k,hf,hk,hex(val),vi,vf,nk,z1,z2))
    return b,out
if __name__=='__main__':
    b,e=entries(sys.argv[1])
    for x in e: print(*x)
