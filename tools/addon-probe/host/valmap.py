"""Drive the overlay with every key set to value v and tabulate which segment / slider value each widget shows.
usage: valmap.py <builddir> <addon> <logprefix-section-source-log> <mode> <values...>
"""
import re, subprocess, sys, os, collections

H = os.path.dirname(os.path.abspath(__file__))
build, addon, srclog, mode = sys.argv[1:5]
values = sys.argv[5:]
keys = []
for l in open(srclog, encoding='utf8', errors='replace'):
    m = re.search(r'GetConfigValue .*?\[(.*?)\] (\S+)', l)
    if m and (m.group(1), m.group(2)) not in keys and m.group(2) not in ('OptionsMode', 'LoadFromDllMain', 'HookStreamline'):
        keys.append((m.group(1), m.group(2)))
table = collections.OrderedDict()
labels = {}
for v in values:
    ini = os.path.join(H, 'inis', 'vm_%s_%s_%s.ini' % (build, mode, v))
    secs = collections.OrderedDict()
    secs.setdefault('RENODX-DLSS', []).append(('OptionsMode', mode))
    for s, k in keys:
        secs.setdefault(s, []).append((k, v))
    with open(ini, 'w') as f:
        for s, kv in secs.items():
            f.write('[%s]\n' % s)
            for k, x in kv: f.write('%s=%s\n' % (k, x))
    tag = 'vm_%s_%s' % (mode, v)
    subprocess.run([os.environ.get('GITBASH','bash'), os.path.join(H, 'run.sh'), build, addon, tag, '--wait', '300', '--overlay', '--ini', ini], capture_output=True, timeout=120)
    log = os.path.join(H, 'logs', '%s_%s.log' % (build, tag))
    out = subprocess.run([sys.executable, '-I', os.path.join(H, 'widgets.py'), log], capture_output=True, text=True).stdout
    out = out.split('pass 2 calling')[0]
    for line in out.split('\n'):
        m = re.match(r'\s+key=(\S+)\s+label=(.*?) kind=(\S+)(.*)', line)
        if not m: continue
        key, label, kind, rest = m.groups()
        labels[key] = (label, kind)
        sm = re.search(r'segs=\[(.*?)\] \(', rest)
        if sm:
            segs = sm.group(1).split(' | ')
            sel = [i for i, s in enumerate(segs) if s.endswith('*')]
            val = ','.join('%d:%s' % (i, segs[i].rstrip('*')) for i in sel) or 'none'
        else:
            cm = re.search(r'cur=(\S+)', rest)
            val = 'cur=' + (cm.group(1) if cm else '-')
            if kind == 'segmented' or not cm: continue
        table.setdefault(key, collections.OrderedDict())[v] = val
print('build=%s mode=%s' % (build, mode))
print('%-52s %-28s | %s' % ('key', 'label', ' | '.join('v=%s' % v for v in values)))
for key, d in table.items():
    print('%-52s %-28s | %s' % (key, labels[key][0], ' | '.join(d.get(v, '-') for v in values)))
