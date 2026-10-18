from pel import *
import capstone,sys
def dis(b,rva,before=0x40,after=0x80):
    md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64); md.detail=False
    start=rva-before
    o=b.rva2off(start)
    for i in md.disasm(b.d[o:o+before+after],b.ib+start):
        s=''
        yield i
if __name__=='__main__':
    b=B(sys.argv[1]); rva=int(sys.argv[2],16)
    for i in dis(b,rva,int(sys.argv[3],16),int(sys.argv[4],16)):
        ann=''
        if 'rip' in i.op_str:
            import re
            m=re.search(r'rip ([+-]) (0x[0-9a-f]+)',i.op_str)
            t=i.address+i.size+(1 if m.group(1)=='+' else -1)*int(m.group(2),16)
            o=b.ptr2off(t)
            if o: 
                c=b.cstr(o); ann=' ; '+(repr(c[:70]) if c else 'f=%s i=%d'%(struct.unpack_from('<f',b.d,o)[0],struct.unpack_from('<i',b.d,o)[0]))
        print('%x: %s %s%s'%(i.address-b.ib,i.mnemonic,i.op_str,ann))
