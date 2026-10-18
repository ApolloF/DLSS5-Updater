import re, sys, collections
# usage: oracle.py section log1 log2 ...  -> prints key -> message suffix per log
sec = sys.argv[1]
logs = sys.argv[2:]
tab = collections.OrderedDict()
for lg in logs:
    for l in open(lg, encoding='utf8', errors='replace'):
        m = re.search(r'ReShade\.ini \[%s\] (\S+?)=(\S+) \((.*?)\)(?:: (.*))?$' % re.escape(sec), l)
        if m:
            tab.setdefault(m.group(1), {})[lg] = '%s -> %s%s' % (m.group(2), m.group(3), (' / ' + m.group(4)) if m.group(4) and 'could not be used' not in m.group(4) else '')
for k, d in tab.items():
    print('%-34s %s' % (k, ' || '.join(d.get(lg, '-') for lg in logs)))
