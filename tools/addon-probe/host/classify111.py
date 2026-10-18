import re, subprocess, sys, os, collections
H = os.path.dirname(os.path.abspath(__file__))
cand = [l.strip() for l in open(os.path.join(H, 'logs', 'cand_keys.txt')) if l.strip()]
vals = sys.argv[1:]
retired = set(); oor = collections.defaultdict(set); recog = collections.defaultdict(dict); msgs_other = set()
CH = 18
run = 0
for v in vals:
    for i in range(0, len(cand), CH):
        chunk = cand[i:i + CH]
        ini = os.path.join(H, 'inis', 'c111.ini')
        with open(ini, 'w') as f:
            f.write('[RenoDX.DLSS5]\nConfigVersion=11\n')
            for k in chunk: f.write('%s=%s\n' % (k, v))
        open(os.path.join(H, 'lecram111', 'ReShade.ini.seed'), 'w').write(open(ini).read())
        subprocess.run([os.environ['GITBASH'], os.path.join(H, 'run.sh'), 'lecram111', 'dlss5-nr.addon64', 'cls', '--wait', '250', '--ini', ini], capture_output=True, timeout=120)
        log = open(os.path.join(H, 'logs', 'lecram111_cls.log'), encoding='utf8', errors='replace').read().split('\n')
        for l in log:
            m = re.search(r'NR-SETTINGS retired settings ignored.*?: (.*)$', l)
            if m:
                items = [x.strip() for x in m.group(1).split(',')]
                for x in items:
                    x = re.sub(r' and \d+ more', '', x)
                    if re.match(r'^\w+$', x): retired.add(x)
                if 'more' in m.group(1): print('WARN truncated retired list', v, i, file=sys.stderr)
            m = re.search(r'NR-SETTINGS (saved settings out of range.*?): (.*)$', l)
            if m:
                for x in m.group(2).split(','):
                    oor[x.strip()].add(v)
            m = re.search(r'NR-SETTINGS v1 event=nondefault (?:part=\d+/\d+ )?(.*)$', l)
            if m:
                for kv in m.group(1).split():
                    if '=' in kv:
                        k, val = kv.split('=', 1); recog[k].setdefault(v, val)
            elif 'NR-SETTINGS' in l and 'retired' not in l and 'out of range' not in l and 'event=effective' not in l:
                msgs_other.add(re.sub(r'^.*level=\d: ', '', l)[:300])
import json
json.dump({'retired':sorted(retired),'oor':{k:sorted(v) for k,v in oor.items()},'recog':recog}, open(os.path.join(H,'logs','lecram111_classify.json'),'w'), indent=1)
print('RETIRED (%d):' % len(retired), ' '.join(sorted(retired)))
print('OUT_OF_RANGE (clamped at', vals, '):')
for k, s in sorted(oor.items()): print('   ', k, sorted(s))
print('RECOGNIZED nondefault (key: value-seen-per-test-value):')
for k, d in sorted(recog.items()): print('   ', k, d)
print('OTHER NR-SETTINGS MSGS:'); [print('   ', m) for m in sorted(msgs_other)]
