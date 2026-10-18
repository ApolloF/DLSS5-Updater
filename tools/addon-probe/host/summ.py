import re,sys
log=open(sys.argv[1],encoding='utf8',errors='replace').read().split('\n')
reads=[];writes=[];seen=set();msgs=[];evs={};other=[]
for l in log:
    l=re.sub(r'^\[ *\d+ t[0-9a-f]+\] ','',l)
    m=re.match(r'GetConfigValue (?:mod=\S+ rt=\S+ )?\[(.*?)\] (\S+) \((\w+)(?: cap=(\d+))?\)? ?-> (.*)',l)
    if not m:
        m2=re.match(r'GetConfigValue (?:mod=\S+ rt=\S+ )?\[(.*?)\] (\S+) -> (.*)',l)
        if m2: reads.append((m2.group(1),m2.group(2),'?',m2.group(3)));continue
    if m:
        reads.append((m.group(1),m.group(2),m.group(4) or m.group(3),m.group(5)));continue
    m=re.match(r'SetConfigValue (?:mod=\S+ rt=\S+ )?\[(.*?)\] (\S+) = (.*)',l)
    if m: writes.append((m.group(1),m.group(2),m.group(3)));continue
    m=re.match(r'RegisterEvent (\S+) \((\d+)\) cb=(.*)',l)
    if m: evs[m.group(1)]=evs.get(m.group(1),0)+1;continue
    if l.startswith('LogMessage'): msgs.append(l)
    elif re.match(r'(RegisterOverlay|RegisterAddon|GetBasePath|GetImGui|SetConfigArray|HOST|EXCEPTION)',l): other.append(l[:300])
print('--- READS in order (%d)'%len(reads))
for i,r in enumerate(reads,1): print('%3d [%s] %s  cap=%s -> %s'%(i,*r))
print('--- WRITES in order (%d)'%len(writes))
for i,w in enumerate(writes,1): print('%3d [%s] %s = %s'%(i,*w))
print('--- EVENTS',', '.join('%s x%d'%kv for kv in sorted(evs.items())))
print('--- OTHER');print('\n'.join(other))
print('--- LOGMSGS (%d)'%len(msgs));print('\n'.join(m[:600] for m in msgs))
