from pel import *
import capstone,re,sys,bisect
def funcs(b):
    pd=[s for s in b.secs if True]
    sec=[x for x in b.pe.sections if x.Name.startswith(b'.pdata')][0]
    d=sec.get_data()
    f=[]
    for i in range(0,len(d)-11,12):
        s,e,u=struct.unpack_from('<3I',d,i)
        if s: f.append((s,e))
    return sorted(f)
def run(name, key_rva_hint=None, clampI=None, clampF=None, ldI=None, ldF=None, fn_rva=None, verbose=False):
    b=B(name+'.bin'); F=funcs(b)
    starts=[s for s,e in F]
    i=bisect.bisect_right(starts,fn_rva)-1
    s,e=F[i]
    md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64); md.detail=True
    o=b.rva2off(s)
    R={}; X={}
    def rg(n): return R.get(n)
    name64={'rcx':'ecx','rdx':'edx','r8':'r8d','r9':'r9d'}
    results=[]
    strs={}
    stackarg=None
    for ins in md.disasm(b.d[o:o+(e-s)],b.ib+s):
        m=ins.mnemonic; ops=ins.op_str
        def reg32(x):
            x=x.strip()
            M={'eax':'rax','ecx':'rcx','edx':'rdx','ebx':'rbx','esi':'rsi','edi':'rdi','r8d':'r8','r9d':'r9','r10d':'r10','r11d':'r11','r12d':'r12','r13d':'r13','r14d':'r14','r15d':'r15','ebp':'rbp','esp':'rsp'}
            return M.get(x, x)
        def ripaddr():
            mm=re.search(r'\[rip ([+-]) (0x[0-9a-f]+)\]',ops)
            if mm: return ins.address+ins.size+(1 if mm.group(1)=='+' else -1)*int(mm.group(2),16)
        if m=='mov' and re.match(r'(e..|r\d+d), (0x[0-9a-f]+|\d+)$',ops):
            a,c=ops.split(', '); R[reg32(a)]=int(c,0)
        elif m=='xor':
            a,c=ops.split(', ')
            if a==c: R[reg32(a)]=0; 
        elif m=='mov' and re.match(r'(e..|r\d+d), (e..|r\d+d)$',ops):
            a,c=ops.split(', '); 
            if reg32(c) in R: R[reg32(a)]=R[reg32(c)]
            else: R.pop(reg32(a),None)
        elif m=='lea' and ops.endswith(']') and 'rip' in ops:
            a=ops.split(',')[0]; t=ripaddr(); of=b.ptr2off(t); R[a]=('S',b.cstr(of) if of else None)
        elif m=='mov' and ops.startswith('qword ptr [rsp + 0x20], '):
            stackarg=R.get(ops.split(', ')[1])
        elif m in('movss',) and re.match(r'xmm\d+, dword ptr \[rip',ops):
            t=ripaddr(); of=b.ptr2off(t); X[ops.split(',')[0]]=struct.unpack_from('<f',b.d,of)[0]
        elif m=='xorps' or m=='pxor':
            a,c=ops.split(', ')
            if a==c: X[a]=0.0
        elif m=='movaps' and re.match(r'xmm\d+, xmm\d+$',ops):
            a,c=ops.split(', ')
            if c in X: X[a]=X[c]
            else: X.pop(a,None)
        elif m=='movss' and re.match(r'xmm\d+, xmm\d+$',ops):
            a,c=ops.split(', ')
            if c in X: X[a]=X[c]
        elif m=='mov' and re.match(r'(e..|r\d+d), ',ops):
            R.pop(reg32(ops.split(',')[0]),None)
        elif m=='call':
            t=ins.operands[0]
            tgt=t.imm-b.ib if t.type==capstone.x86.X86_OP_IMM else None
            if tgt==clampI:
                results.append(('I',stackarg,'val?',R.get('rdx'),R.get('r8'),R.get('r9'),ins.address-b.ib))
            elif tgt==clampF:
                results.append(('F',stackarg,X.get('xmm1'),X.get('xmm2'),X.get('xmm3'),ins.address-b.ib))
            elif tgt in(ldI,ldF):
                results.append(('LI' if tgt==ldI else 'LF', R.get('r8'), ins.address-b.ib))
            for r in ('rcx','rdx','r8','r9','rax','r10','r11'): R.pop(r,None)
            if tgt!=clampF and tgt not in (clampI,): pass
            for x in ['xmm0','xmm1','xmm2','xmm3','xmm4','xmm5']: X.pop(x,None)
    return b,results,(s,e)
if __name__=='__main__':
    n=sys.argv[1]; args=[int(x,16) for x in sys.argv[2:]]
    b,res,rg=run(n,clampI=args[0],clampF=args[1],ldI=args[2],ldF=args[3],fn_rva=args[4])
    print(hex(rg[0]),hex(rg[1]),len(res))
    for r in res: print(r)
