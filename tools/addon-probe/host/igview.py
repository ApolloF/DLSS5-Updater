import re,sys
skip=set('''SameLine NewLine Spacing Dummy Indent Unindent PushStyleColor PushStyleColor2 PopStyleColor PushStyleVar PushStyleVar2 PopStyleVar PushStyleVarX PushStyleVarY PushItemWidth PopItemWidth SetNextItemWidth CalcItemWidth GetFontSize GetTextLineHeight GetFont GetFontBaked GetFrameHeight GetStyle GetIO GetColorU32 GetColorU322 GetColorU323 GetStyleColorVec4 ColorConvertRGBtoHSV ColorConvertHSVtoRGB ColorConvertFloat4ToU32 ColorConvertU32ToFloat4 PushID PushID2 PushID3 PushID4 PopID GetID GetID2 GetID3 GetID4 AlignTextToFramePadding GetCursorPos GetCursorPosX GetCursorPosY SetCursorPos SetCursorPosX SetCursorPosY GetCursorScreenPos SetCursorScreenPos GetContentRegionAvail CalcTextSize GetWindowDrawList GetWindowWidth GetWindowHeight GetWindowPos GetWindowSize IsItemHovered IsItemActive IsItemClicked IsItemFocused IsItemEdited IsItemActivated IsItemDeactivated IsItemDeactivatedAfterEdit GetItemRectMin GetItemRectMax GetItemRectSize GetTextLineHeightWithSpacing GetFrameHeightWithSpacing SetNextWindowPos SetNextWindowSize SetNextWindowSizeConstraints PushFont PopFont GetTreeNodeToLabelSpacing GetColumnWidth GetScrollY GetScrollX PushClipRect PopClipRect SetNextItemOpen TreePop TreePush EndGroup BeginGroup GetWindowDpiScale Separator PushTextWrapPos PopTextWrapPos GetKeyName IsKeyDown IsKeyPressed IsMouseClicked IsMouseDown PushItemFlag PopItemFlag SetItemDefaultFocus SetNextItemAllowOverlap SetKeyboardFocusHere GetMousePos IsMouseHoveringRect SetNextWindowFocus SetNextWindowBgAlpha GetItemID GetTime GetFrameCount'''.split())
ign=re.compile(r'^ImGui::(\w+)\((.*)\)$')
out=[]
for l in open(sys.argv[1],encoding='utf8',errors='replace'):
    l=re.sub(r'^\[ *\d+ t[0-9a-f]+\] ','',l.rstrip('\n'))
    if l.startswith('HOST ==='): out.append(l);continue
    if l.startswith('  RUNTIME') or l.startswith('  OBJ') or l.startswith('  DEVICE'): out.append('    '+l.strip()[:160]);continue
    if l.startswith('GetConfigValue') or l.startswith('SetConfigValue') or l.startswith('LogMessage'):out.append(l[:400]);continue
    m=re.match(r'\s+ImGui::(\w+)\((.*)\)$',l)
    if not m: continue
    n,a=m.groups()
    if n in skip: continue
    strs=re.findall(r'"([^"]*)"',a)
    # value hints: ptr(... i32=X f32=Y | +4 ...) first occurrence at arg index>=1
    ptrs=re.findall(r'ptr\([0-9a-fx]+ u8=(\d+) i32=(-?\d+) f32=(\S+) \| \+4 i32=(-?\d+) f32=(\S+?)\)(?: items\[(\d+)\]=\{(.*?)\})?',a)
    nums=re.findall(r'(?:, |^)(\d+)\(0x',a)
    xm=re.search(r'xmmf=\[(.*?)\]',a)
    s=n
    if n=='ImDrawList_AddText2':
        t=re.search(r's6="([^"]*)"',a)
        out.append('  seg-text: '+(t.group(1) if t else '?'));continue
    if strs: s+=' '+' | '.join('"%s"'%x for x in strs)
    if n.startswith(('Slider','Drag','Input','Checkbox','Combo','Radio','ColorEdit','Selectable','ListBox','CheckboxFlags','ProgressBar')) :
        if ptrs: p=ptrs[0]; s+='  [cur u8=%s i32=%s f32=%s]'%(p[0],p[1],p[2])
        if len(ptrs)>0 and ptrs[0][5]: s+=' items(%s)={%s}'%(ptrs[0][5],ptrs[0][6])
        for p in ptrs[1:]:
            if p[5]: s+=' items(%s)={%s}'%(p[5],p[6])
        if xm: s+=' xmm=['+xm.group(1)+']'
        if nums: s+=' ints='+','.join(nums[:4])
    out.append(s)
print('\n'.join(out))
