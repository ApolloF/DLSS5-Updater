from sect import *
from xref import xrefs
import re,collections
def collect(n, secrva=None):
    b,rv=find(n) if secrva is None else (B(n+'.bin'),secrva)
    r=xrefs(b,set(rv))
    md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64); md.detail=False
    res=collections.defaultdict(collections.Counter)
    for k,v in r.items():
        for a in set(v):
            o=b.rva2off(a-0x30)
            name=None;cal=None
            ins=list(md.disasm(b.d[o:o+0x90],b.ib+a-0x30))
            for i in ins:
                if i.address-b.ib<a-0x20: continue
                if i.mnemonic=='lea' and i.op_str.startswith('r8, [rip'):
                    mm=re.search(r'rip ([+-]) (0x[0-9a-f]+)',i.op_str)
                    t=i.address+i.size+(1 if mm.group(1)=='+' else -1)*int(mm.group(2),16)
                    of=b.ptr2off(t); name=b.cstr(of) if of else None
                if i.mnemonic=='call' and i.address-b.ib>a-0x4:
                    cal=i.op_str;break
            res[name][cal]+=1
    return res
if __name__=='__main__':
    res=collect(sys.argv[1])
    for k in sorted(res,key=str): print(k,dict(res[k]))
