from ext import *
def run2(name,fn_rva,getters,clampI,clampF):
    b=B(name+'.bin'); F=funcs(b); starts=[s for s,e in F]
    i=bisect.bisect_right(starts,fn_rva)-1; s,e=F[i]
    md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64); md.detail=True
    o=b.rva2off(s)
    R={};X={};ST={};lastlea={}
    out=[]; stackarg=None
    M={'eax':'rax','ecx':'rcx','edx':'rdx','ebx':'rbx','esi':'rsi','edi':'rdi','r8d':'r8','r9d':'r9','r10d':'r10','r11d':'r11','r12d':'r12','r13d':'r13','r14d':'r14','r15d':'r15'}
    for ins in md.disasm(b.d[o:o+(e-s)],b.ib+s):
        m=ins.mnemonic; ops=ins.op_str
        def rip():
            mm=re.search(r'\[rip ([+-]) (0x[0-9a-f]+)\]',ops)
            if mm: return ins.address+ins.size+(1 if mm.group(1)=='+' else -1)*int(mm.group(2),16)
        if m=='mov' and re.match(r'(e..|r\d+d), (0x[0-9a-f]+|\d+)$',ops):
            a,c=ops.split(', '); R[M.get(a,a)]=int(c,0)
        elif m=='xor' and ops.split(', ')[0]==ops.split(', ')[1]:
            R[M.get(ops.split(', ')[0],ops.split(', ')[0])]=0
        elif m=='mov' and re.match(r'(e..|r\d+d), (e..|r\d+d)$',ops):
            a,c=ops.split(', ')
            if M.get(c,c) in R: R[M.get(a,a)]=R[M.get(c,c)]
            else: R.pop(M.get(a,a),None)
        elif m=='lea' and 'rip' in ops:
            a=ops.split(',')[0]; of=b.ptr2off(rip()); R[a]=('S',b.cstr(of) if of else None)
        elif m=='lea' and re.match(r'(r\w+), \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\]$',ops):
            mm=re.match(r'(r\w+), \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\]$',ops)
            R[mm.group(1)]=('P',mm.group(2),(1 if mm.group(3)=='+' else -1)*int(mm.group(4),16))
        elif m=='mov' and ops.startswith('qword ptr [rsp + 0x20], '):
            stackarg=R.get(ops.split(', ')[1])
        elif m=='mov' and re.match(r'byte ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (0x[0-9a-f]+|\d+|[a-d]l|sil|dil|r\d+b)$',ops):
            mm=re.match(r'byte ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (\S+)$',ops)
            v=mm.group(4)
            if re.match(r'(0x[0-9a-f]+|\d+)$',v): val=int(v,0)
            else:
                rr={'al':'rax','bl':'rbx','cl':'rcx','dl':'rdx','sil':'rsi','dil':'rdi'}.get(v, 'r'+v[1:-1] if v.startswith('r') else None)
                val=R.get(rr)
            ST[(mm.group(1),(1 if mm.group(2)=='+' else -1)*int(mm.group(3),16))]=('imm',val)
        elif m=='mov' and re.match(r'dword ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (0x[0-9a-f]+|\d+)$',ops):
            mm=re.match(r'dword ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (0x[0-9a-f]+|\d+)$',ops)
            ST[(mm.group(1),(1 if mm.group(2)=='+' else -1)*int(mm.group(3),16))]=('imm',int(mm.group(4),0))
        elif m=='mov' and re.match(r'dword ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (e..|r\d+d)$',ops):
            mm=re.match(r'dword ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (e..|r\d+d)$',ops)
            r=R.get(M.get(mm.group(4),mm.group(4)))
            ST[(mm.group(1),(1 if mm.group(2)=='+' else -1)*int(mm.group(3),16))]=('imm',r) if r is not None else ('?',)
        elif m=='movss' and re.match(r'dword ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (xmm\d+)$',ops):
            mm=re.match(r'dword ptr \[(rbp|rsp) ([+-]) (0x[0-9a-f]+)\], (xmm\d+)$',ops)
            ST[(mm.group(1),(1 if mm.group(2)=='+' else -1)*int(mm.group(3),16))]=('f',X.get(mm.group(4)))
        elif m=='movss' and re.match(r'xmm\d+, dword ptr \[rip',ops):
            of=b.ptr2off(rip()); X[ops.split(',')[0]]=struct.unpack_from('<f',b.d,of)[0]
        elif m in('xorps','pxor') and ops.split(', ')[0]==ops.split(', ')[1]: X[ops.split(', ')[0]]=0.0
        elif m=='movaps' and re.match(r'xmm\d+, xmm\d+$',ops):
            a,c=ops.split(', ')
            if c in X: X[a]=X[c]
            else: X.pop(a,None)
        elif m=='call':
            t=ins.operands[0]; tgt=t.imm-b.ib if t.type==capstone.x86.X86_OP_IMM else None
            if tgt in getters:
                key=R.get('r8'); dest=R.get('r9')
                init=None
                if isinstance(dest,tuple) and dest[0]=='P': init=ST.get((dest[1],dest[2]))
                out.append(('GET',getters[tgt],key[1] if isinstance(key,tuple) else key,init,dest,ins.address-b.ib))
            elif tgt==clampI:
                out.append(('CLAMPI',stackarg[1] if isinstance(stackarg,tuple) else stackarg,R.get('rdx'),R.get('r8'),R.get('r9'),ins.address-b.ib))
            elif tgt==clampF:
                out.append(('CLAMPF',stackarg[1] if isinstance(stackarg,tuple) else stackarg,X.get('xmm1'),X.get('xmm2'),X.get('xmm3'),ins.address-b.ib))
            for r in ('rcx','rdx','r8','r9','rax','r10','r11'): R.pop(r,None)
            for x in ['xmm0','xmm1','xmm2','xmm3','xmm4','xmm5']: X.pop(x,None)
    return b,out,(s,e)
