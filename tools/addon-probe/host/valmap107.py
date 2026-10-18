import re, subprocess, sys, os, collections
H = os.path.dirname(os.path.abspath(__file__))
values = sys.argv[1:]
keys = []
for l in open(os.path.join(H, 'logs', 'lecram107_base.log'), encoding='utf8', errors='replace'):
    m = re.search(r'GetConfigValue .*?\[(.*?)\] (\S+)', l)
    if m and m.group(2) not in keys: keys.append(m.group(2))
for l in open(os.path.join(H, 'logs', 'lecram107_open.widgets.txt'), encoding='utf8'):
    m = re.match(r'(NR\w+)\s', l)
    if m and m.group(1) not in keys: keys.append(m.group(1))
table = collections.OrderedDict()
for v in values:
    ini = os.path.join(H, 'inis', 'vm107_%s.ini' % v)
    with open(ini, 'w') as f:
        f.write('[RenoDX.DLSS5]\n')
        for k in keys: f.write('%s=%s\n' % (k, v))
    tag = 'vm107_%s' % v
    subprocess.run([os.environ['GITBASH'], os.path.join(H, 'run.sh'), 'lecram107', 'renodx-dlss5.addon64', tag, '--wait', '300', '--overlay', '--imgui-true', 'ImGuiStorage_GetBool', '--ini', ini], capture_output=True, timeout=120)
    out = subprocess.run([sys.executable, '-I', os.path.join(H, 'widgets107.py'), os.path.join(H, 'logs', 'lecram107_%s.log' % tag)], capture_output=True, text=True).stdout
    cur = None
    for line in out.split('\n'):
        m = re.match(r'(NR\w+)\s+label=(.*)', line)
        if m: cur = m.group(1); table.setdefault(cur, collections.OrderedDict()); table[cur]['label'] = m.group(2); continue
        m = re.match(r'\s+ctl: (.*)', line)
        if m and cur:
            c = m.group(1)
            c = re.sub(r'note: .*', '', c)
            table[cur][v] = c.strip()
print('keys in ini:', len(keys))
for k, d in table.items():
    print(k, d.get('label'))
    for v in values:
        if v in d: print('    v=%-5s %s' % (v, d[v][:230]))
