import re
t=open('src/reshade_overlay.hpp').read()
s=t.index('struct imgui_function_table_19250'); e=t.index('};',s)
names=[]
for l in t[s:e].split('\n'):
    m=re.match(r'\t[\w:<>&\*\s]+?\(\*(\w+)\)\(',l)
    if m: names.append(m.group(1))
ev=open('src/reshade_events.hpp').read()
m=re.search(r'enum class addon_event : uint32_t\s*\{(.*?)\n\t\};',ev,re.S)
b=re.sub(r'///.*','',m.group(1))
b=re.sub(r'#.*','',b)
evn={};cur=0
for x in b.split(','):
    x=x.strip()
    if not x: continue
    if '=' in x:
        n,v=x.split('=');n=n.strip();cur=int(v.split()[0])
    else: n=x
    evn[cur]=n.split()[0];cur+=1
RT=160
o=[]
o.append('static const char* const kImNames[]={%s};'%','.join('"%s"'%n for n in names))
o.append('static const int kImCount=%d;'%len(names))
o.append('static const char* const kEvNames[102]={%s};'%','.join('"%s"'%evn.get(i,'?') for i in range(102)))
o.append('static const int kRtSlots=%d;'%RT)
for i in range(len(names)+RT):
    o.append('extern "C" void ig_stub_%d();'%i)
o.append('static void* const kImTable[]={%s};'%','.join('(void*)ig_stub_%d'%i for i in range(len(names))))
o.append('static void* const kRtVtbl[]={%s};'%','.join('(void*)ig_stub_%d'%(len(names)+i) for i in range(RT)))
BS=chr(92)
N=BS+"n"
a=['asm(".intel_syntax noprefix'+N+'"']
for i in range(len(names)+RT):
    a.append('".globl ig_stub_%d'%i+N+'ig_stub_%d: mov r11d, %d'%(i,i)+N+' jmp ig_common'+N+'"')
a.append('".att_syntax prefix'+N+'");')
o.append(chr(10).join(a))
open('gen.inc','w').write('\n'.join(o))
print(len(names),len(evn))
