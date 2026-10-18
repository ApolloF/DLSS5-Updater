"""Parse an overlay-render log of the Lecram 10.7 add-on into per-setting records.
usage: widgets107.py <log>   (uses pass 1 only)
"""
import re, sys

lines = []
on = False
for raw in open(sys.argv[1], encoding='utf8', errors='replace'):
    raw = re.sub(r'^\[ *\d+ t[0-9a-f]+\] ', '', raw.rstrip('\n'))
    if raw.startswith('HOST === calling overlay') and 'pass 1' in raw: on = True; continue
    if raw.startswith('HOST === overlay') and 'pass 1' in raw: break
    if not on: continue
    m = re.match(r'\s+ImGui::(\w+)\((.*)\)$', raw)
    if not m: continue
    n, a = m.groups()
    a = re.sub(r'ptr\(([0-9a-f]+) u8=(\d+) i32=(-?\d+) f32=(\S+) \| \+4 i32=(-?\d+) f32=([^ )]+)([^)]*)\)(?: items\[(\d+)\]=\{(.*?)\})?', lambda mm: 'P[%s,%s]' % (mm.group(4), mm.group(6)) + ('{%s}' % mm.group(9) if mm.group(9) else ''), a)
    lines.append((n, a, raw))

def split_args(a):
    out = []; depth = 0; q = False; cur = ''
    for ch in a:
        if ch == '"': q = not q
        if not q:
            if ch in '([{': depth += 1
            elif ch in ')]}': depth -= 1
            elif ch == ',' and depth == 0:
                out.append(cur.strip()); cur = ''; continue
        cur += ch
    if cur.strip(): out.append(cur.strip())
    return out

def unq(s):
    m = re.match(r'^"(.*)"$', s or '')
    return m.group(1) if m else None

def valf(s):
    m = re.match(r'VAL\(u8=(\d+) i32=(-?\d+) f32=(\S+) \| \+4 i32=(-?\d+) f32=(\S+?)\)', s or '')
    return m.groups() if m else None

recs = []
cur = None
sel_pending = False
last_rect_w = None
section = None
i = 0
for n, a, raw in lines:
    args = split_args(a)
    g = lambda k: args[k] if k < len(args) else ''
    if n == 'PushID':
        s = unq(g(0))
        if s and re.match(r'^NR\w+$', s):
            cur = dict(key=s, label=None, tips=[], ctl=[], section=section)
            recs.append(cur); continue
        if s and not s.startswith('##') and not re.match(r'^NR', s):
            section = s
            cur = dict(key='[section] ' + s, label=None, tips=[], ctl=[], section=s); recs.append(cur); continue
    if cur is None: continue
    if n == 'TextUnformatted' and cur['label'] is None and unq(g(0)):
        cur['label'] = unq(g(0))
    elif n == 'TextV' and unq(g(0)):
        t = unq(g(0))
        if t not in cur['tips']: cur['tips'].append(t)
    elif n == 'TextWrappedV' and unq(g(0)):
        cur['ctl'].append('note: ' + unq(g(0)))
    elif n == 'PushStyleColor2' and g(0).startswith('21('):
        sel_pending = True
    elif n == 'Button':
        cur['ctl'].append(('btn', unq(g(0)), sel_pending)); sel_pending = False
    elif n == 'SliderFloat' or n == 'SliderInt' or n == 'DragFloat' or n == 'DragInt' or n.startswith('Input'):
        v = valf(g(1)); xm = re.search(r'xmmf=\[(.*?)\]', a); xs = xm.group(1).split(',') if xm else []
        fm = re.search(r's5="([^"]*)"', a)
        if n.endswith('Int'):
            cur['ctl'].append((n, 'cur=%s' % (v[1] if v else '?'), 'min=%s max=%s' % (re.sub(r'\(.*', '', g(2)), re.sub(r'\(.*', '', g(3))), fm.group(1) if fm else ''))
        else:
            cur['ctl'].append((n, 'cur=%s' % (v[2] if v else '?'), 'min=%s max=%s' % (xs[2] if len(xs) > 3 else '?', xs[3] if len(xs) > 3 else '?'), fm.group(1) if fm else ''))
    elif n == 'Combo':
        v = valf(g(1)); it = re.search(r'\{(.*?)\}', a)
        cur['ctl'].append(('Combo', 'cur_index=%s' % (v[1] if v else '?'), it.group(1) if it else '?'))
    elif n == 'Checkbox':
        v = valf(g(1)); cur['ctl'].append(('Checkbox', unq(g(0)), 'cur_u8=%s' % (v[0] if v else '?')))
    elif n == 'ImDrawList_AddRectFilled':
        m = re.match(r'P\[([-\d.e+]+),', g(2)); last_rect_w = float(m.group(1)) if m else None
    elif n == 'ImDrawList_AddCircleFilled':
        m = re.match(r'P\[([-\d.e+]+),', g(1))
        if m and last_rect_w:
            x = float(m.group(1)); cur['ctl'].append(('toggle', 'knob_x=%.1f/width=%.1f' % (x, last_rect_w), 'ON' if x > last_rect_w / 2 else 'OFF'))
    elif n == 'Selectable':
        cur['ctl'].append(('Selectable', unq(g(0)), a[:100]))

for r in recs:
    ctl = []
    btn = [c for c in r['ctl'] if isinstance(c, tuple) and c[0] == 'btn']
    if btn: ctl.append('buttons=[%s]' % ' | '.join('%s%s' % (c[1], '*' if c[2] else '') for c in btn))
    for c in r['ctl']:
        if isinstance(c, tuple) and c[0] != 'btn': ctl.append(' '.join(str(x) for x in c))
        elif isinstance(c, str): ctl.append(c)
    print('%-34s label=%r' % (r['key'], r['label']))
    if ctl: print('      ctl: ' + ' ;; '.join(ctl))
    for t in r['tips']: print('      tip: ' + t)
