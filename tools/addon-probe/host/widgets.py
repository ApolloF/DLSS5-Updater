import re, sys, collections

def split_args(a):
    out = []; depth = 0; q = False; cur = ''
    for ch in a:
        if ch == '"': q = not q
        if not q:
            if ch == '(': depth += 1
            elif ch == ')': depth -= 1
            elif ch == ',' and depth == 0:
                out.append(cur.strip()); cur = ''; continue
        cur += ch
    if cur.strip(): out.append(cur.strip())
    return out

def unq(s):
    m = re.match(r'^"(.*)"$', s)
    return m.group(1) if m else None

def val(s):
    m = re.match(r'VAL\(u8=(\d+) i32=(-?\d+) f32=(\S+) \| \+4 i32=(-?\d+) f32=(\S+?)\)', s)
    return m.groups() if m else None

def num(s):
    m = re.match(r'^(\d+)\(0x', s)
    return int(m.group(1)) if m else None

widgets = []
cur = None
pending_label = None
for line in open(sys.argv[1], encoding='utf8', errors='replace'):
    line = re.sub(r'^\[ *\d+ t[0-9a-f]+\] ', '', line.rstrip('\n'))
    if line.startswith('HOST ==='):
        widgets.append(('MARK', line)); cur = None; continue
    m = re.match(r'\s+ImGui::(\w+)\((.*)\)$', line)
    if not m: continue
    n, a = m.groups()
    args = split_args(a)
    g = lambda i: args[i] if i < len(args) else ''
    if n == 'PushID':
        s0 = unq(g(0))
        if s0 and s0.startswith('##Key'):
            cur = dict(key=s0[5:], label=None, segs=[], kind='?', tip=[], extra=[])
            widgets.append(('W', cur)); continue
        if s0 and s0.startswith('##Reset'):
            cur = None; continue
        if s0 and s0.startswith('##') is False and cur is not None and cur['kind'] == '?':
            pass
    if cur is None:
        if n in ('TreeNodeEx', 'TreeNode', 'CollapsingHeader', 'TextWrappedV', 'TextV', 'TextDisabledV', 'TextUnformatted', 'SeparatorText', 'Begin', 'BeginTabItem'):
            widgets.append(('T', n + ' ' + (unq(g(0)) or g(0)[:60])))
        if n == 'SliderInt' and unq(g(0)) == 'Preset':
            widgets.append(('T', 'SliderInt Preset cur=%s range=%s..%s fmt=%s' % (val(g(1)) and val(g(1))[1], num(g(2)), num(g(3)), unq(g(4) if False else '') )))
        if n == 'ImDrawList_AddRectFilled':
            widgets.append(('SEGC', num(g(3))))
        if n == 'ImDrawList_AddText2':
            s6 = re.search(r's6="([^"]*)"', a)
            widgets.append(('SEGT', s6.group(1) if s6 else '?'))
        continue
    if n == 'InvisibleButton':
        cur['kind'] = 'segmented'
    elif n == 'ImDrawList_AddRectFilled':
        cur['segs'].append([None, num(g(3))])
    elif n == 'ImDrawList_AddText2':
        s6 = re.search(r's6="([^"]*)"', a)
        t = s6.group(1) if s6 else '?'
        if cur['segs'] and cur['segs'][-1][0] is None: cur['segs'][-1][0] = t
        else: cur['segs'].append([t, None])
    elif n in ('TextUnformatted',) and cur['label'] is None:
        cur['label'] = unq(g(0))
    elif n in ('SetItemTooltipV', 'SetTooltipV'):
        cur['tip'].append(unq(g(0)) or g(0))
    elif n.startswith('Slider') or n.startswith('Drag') or n.startswith('Input'):
        cur['kind'] = n; cur['label'] = unq(g(0))
        v = val(g(1))
        xm = re.search(r'xmmf=\[(.*?)\]', a)
        xs = xm.group(1).split(',') if xm else []
        if n == 'SliderInt':
            cur['extra'] = ['cur=%s' % (v[1] if v else '?'), 'min=%s' % num(g(2)), 'max=%s' % num(g(3)), 'fmt=%s' % (unq(g(4)) or unq(re.search(r's5=("[^"]*")', a).group(1)) if re.search(r's5=("[^"]*")', a) else '')]
        else:
            fm = re.search(r's5=("[^"]*")', a)
            cur['extra'] = ['cur=%s' % (v[2] if v else '?'), 'min=%s' % (xs[2] if len(xs) > 3 else '?'), 'max=%s' % (xs[3] if len(xs) > 3 else '?'), 'fmt=%s' % (fm.group(1) if fm else '?')]
    elif n in ('Checkbox', 'Combo', 'Combo2', 'RadioButton', 'ColorEdit3', 'ColorEdit4'):
        cur['kind'] = n; cur['label'] = unq(g(0)); v = val(g(1)); cur['extra'] = ['cur_u8=%s i32=%s' % (v[0], v[1]) if v else '?', a[:200]]

out = []
for kind, w in widgets:
    if kind == 'MARK':
        out.append(w); continue
    if kind == 'T':
        out.append('  [text] ' + w); continue
    if kind in ('SEGC', 'SEGT'):
        continue
    segs = w['segs']
    cols = [s[1] for s in segs if s[1] is not None]
    common = 0xff141414
    parts = []
    for t, c in segs:
        parts.append('%s%s' % (t, '*' if (c is not None and c == common) else ''))
    line = '  key=%-52s label=%-30r kind=%s' % (w['key'], w['label'], w['kind'])
    if segs: line += ' segs=[%s]' % ' | '.join(parts) + ' (* = drawn with minority fill colour, i.e. likely selected)'
    if w['extra']: line += ' ' + ' '.join(w['extra'])
    out.append(line)
    for t in w['tip']: out.append('        tooltip: ' + t)
print('\n'.join(out))
