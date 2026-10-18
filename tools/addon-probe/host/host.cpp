// Fake ReShade host: logs every call an add-on makes into the ReShade add-on API.
#include <windows.h>
#include <cstdio>
#include <cstdint>
#include <cstdarg>
#include <cstring>
#include <string>
#include <vector>
#include <map>
#include <set>
#include <mutex>
#include <algorithm>

static void* g_fakeRt[8];
static void* g_fakeDev[8];
struct Regs { uint64_t gpr[4]; uint64_t xmm[4]; uint64_t stk[4]; };
extern "C" uint64_t ig_handle(unsigned idx, Regs* r);
#include "gen.inc"

asm(".intel_syntax noprefix\n"
".globl ig_common\nig_common:\n"
" push rbp\n mov rbp, rsp\n sub rsp, 0xC0\n"
" mov [rsp+0x20], rcx\n mov [rsp+0x28], rdx\n mov [rsp+0x30], r8\n mov [rsp+0x38], r9\n"
" movq [rsp+0x40], xmm0\n movq [rsp+0x48], xmm1\n movq [rsp+0x50], xmm2\n movq [rsp+0x58], xmm3\n"
" mov rax, [rbp+0x30]\n mov [rsp+0x60], rax\n mov rax, [rbp+0x38]\n mov [rsp+0x68], rax\n"
" mov rax, [rbp+0x40]\n mov [rsp+0x70], rax\n mov rax, [rbp+0x48]\n mov [rsp+0x78], rax\n"
" mov ecx, r11d\n lea rdx, [rsp+0x20]\n call ig_handle\n movq xmm0, rax\n leave\n ret\n"
".att_syntax prefix\n");

static FILE* g_log = stdout;
static std::mutex g_mu;
static DWORD g_t0;
static std::string g_dir;
static bool g_iniMode = false;
static std::string g_dumpPath;

static void L(const char* fmt, ...) {
    char buf[8192];
    va_list ap; va_start(ap, fmt);
    vsnprintf(buf, sizeof buf, fmt, ap);
    va_end(ap);
    std::lock_guard<std::mutex> lk(g_mu);
    fprintf(g_log, "[%6lu t%04lx] %s\n", GetTickCount() - g_t0, GetCurrentThreadId(), buf);
    fflush(g_log);
}

static std::string esc(const char* s, size_t n = 600) {
    std::string o;
    if (!s) return "<null>";
    for (size_t i = 0; s[i] && i < n; ++i) {
        unsigned char c = s[i];
        if (c == '\n') o += "\\n"; else if (c == '\r') o += "\\r"; else if (c == '\t') o += "\\t";
        else if (c < 32 || c > 126) { char b[8]; snprintf(b, 8, "\\x%02x", c); o += b; }
        else o += (char)c;
    }
    return o;
}

static std::string modName(void* m) {
    if (!m) return "null";
    char p[MAX_PATH] = {};
    if (!GetModuleFileNameA((HMODULE)m, p, MAX_PATH)) return "?";
    const char* b = strrchr(p, '\\');
    return b ? b + 1 : p;
}

static bool readable(const void* p, size_t n) { return p && (uintptr_t)p > 0x10000 && !IsBadReadPtr(p, n); }
static bool isText(const char* s, size_t* len) {
    if (!readable(s, 1)) return false;
    size_t i = 0;
    for (; i < 512; ++i) {
        if (IsBadReadPtr(s + i, 1)) return false;
        unsigned char c = s[i];
        if (c == 0) break;
        if (c < 9 || (c > 13 && c < 32) || c > 126) return false;
    }
    if (i == 0 || i >= 512) return false;
    *len = i; return true;
}

// ---- config model
typedef std::vector<std::pair<std::string, std::string>> Sec;
static std::map<std::string, Sec> g_cfg;
static std::map<std::string, Sec> g_written;

static std::string lower(std::string s) { for (auto& c : s) c = (char)tolower((unsigned char)c); return s; }
static Sec* findSec(std::map<std::string, Sec>& m, const std::string& s, bool create, bool* ci = nullptr) {
    auto it = m.find(s);
    if (it != m.end()) return &it->second;
    for (auto& kv : m) if (lower(kv.first) == lower(s)) { if (ci) *ci = true; return &kv.second; }
    return create ? &m[s] : nullptr;
}
static std::string* findKey(Sec* sec, const std::string& k, bool* ci = nullptr) {
    if (!sec) return nullptr;
    for (auto& kv : *sec) if (kv.first == k) return &kv.second;
    for (auto& kv : *sec) if (lower(kv.first) == lower(k)) { if (ci) *ci = true; return &kv.second; }
    return nullptr;
}
static void setKey(std::map<std::string, Sec>& m, const std::string& s, const std::string& k, const std::string& v) {
    Sec* sec = findSec(m, s, true);
    std::string* e = findKey(sec, k);
    if (e) *e = v; else sec->push_back({k, v});
}
static void loadIni(const char* path) {
    FILE* f = fopen(path, "rb"); if (!f) { L("HOST ini not found: %s", path); return; }
    char line[8192]; std::string sec;
    while (fgets(line, sizeof line, f)) {
        std::string l = line; while (!l.empty() && (l.back() == '\n' || l.back() == '\r')) l.pop_back();
        if (l.empty() || l[0] == ';') continue;
        if (l[0] == '[') { sec = l.substr(1, l.find(']') - 1); findSec(g_cfg, sec, true); continue; }
        size_t eq = l.find('='); if (eq == std::string::npos) continue;
        setKey(g_cfg, sec, l.substr(0, eq), l.substr(eq + 1));
    }
    fclose(f);
}
static void dumpIni(FILE* f, std::map<std::string, Sec>& m) {
    for (auto& kv : m) { fprintf(f, "[%s]\n", kv.first.c_str()); for (auto& e : kv.second) fprintf(f, "%s=%s\n", e.first.c_str(), e.second.c_str()); fprintf(f, "\n"); }
}

#define EXPORT extern "C" __declspec(dllexport)

EXPORT void ReShadeLogMessage(void* module, int level, const char* message) {
    L("LogMessage mod=%s level=%d: %s", modName(module).c_str(), level, esc(message, 2000).c_str());
}
EXPORT void ReShadeGetBasePath(char* path, size_t* size) {
    size_t need = g_dir.size() + 1;
    if (!path) { if (size) *size = need; L("GetBasePath(size query) -> %zu", need); return; }
    size_t cap = size ? *size : 0;
    size_t n = std::min(need, cap);
    if (n) { memcpy(path, g_dir.c_str(), n - 1); path[n - 1] = 0; }
    if (size) *size = n;
    L("GetBasePath(cap=%zu) -> \"%s\"", cap, g_dir.c_str());
}
EXPORT bool ReShadeGetConfigValue(void* module, void* runtime, const char* section, const char* key, char* value, size_t* value_size) {
    std::string* v = nullptr; bool ci = false;
    if (g_iniMode) { std::lock_guard<std::mutex> lk(g_mu); v = findKey(findSec(g_cfg, section ? section : "", false, &ci), key ? key : "", &ci); }
    size_t cap = value_size ? *value_size : 0;
    if (!v) { L("GetConfigValue mod=%s rt=%p [%s] %s (%s cap=%zu) -> NOT FOUND", modName(module).c_str(), runtime, section ? section : "<null>", key ? key : "<null>", value ? "buf" : "sizequery", cap); return false; }
    if (!value) { if (value_size) *value_size = v->size() + 1; L("GetConfigValue [%s] %s (size query) -> found len=%zu", section, key, v->size()); return true; }
    size_t n = std::min(v->size() + 1, cap);
    if (n) { memcpy(value, v->c_str(), n - 1); value[n - 1] = 0; }
    if (value_size) *value_size = n;
    L("GetConfigValue mod=%s rt=%p [%s] %s -> \"%s\"%s", modName(module).c_str(), runtime, section, key, esc(v->c_str()).c_str(), ci ? " (case-insensitive match)" : "");
    return true;
}
EXPORT void ReShadeSetConfigValue(void* module, void* runtime, const char* section, const char* key, const char* value) {
    L("SetConfigValue mod=%s rt=%p [%s] %s = \"%s\"", modName(module).c_str(), runtime, section ? section : "<null>", key ? key : "<null>", esc(value).c_str());
    std::lock_guard<std::mutex> lk(g_mu);
    if (section && key && value) { setKey(g_written, section, key, value); if (g_iniMode) setKey(g_cfg, section, key, value); }
}
EXPORT void ReShadeSetConfigArray(void* module, void* runtime, const char* section, const char* key, const char* value, size_t value_size) {
    L("SetConfigArray mod=%s rt=%p [%s] %s = <%zu bytes> \"%s\"", modName(module).c_str(), runtime, section, key, value_size, esc(value, 300).c_str());
}
EXPORT bool ReShadeRegisterAddon(void* module, uint32_t api_version) {
    L("RegisterAddon mod=%s api_version=%u -> true", modName(module).c_str(), api_version); return true;
}
EXPORT void ReShadeUnregisterAddon(void* module) { L("UnregisterAddon mod=%s", modName(module).c_str()); }

struct Ev { uint32_t ev; void* cb; };
struct Ov { std::string title; void (*cb)(void*); };
static std::vector<Ev> g_events;
static std::vector<Ov> g_overlays;
static const char* evName(uint32_t e) { return e < 102 ? kEvNames[e] : "?"; }

static std::string where(void* cb) {
    HMODULE m = nullptr; GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCSTR)cb, &m);
    char b[300]; snprintf(b, sizeof b, "%s+0x%llx", modName(m).c_str(), (unsigned long long)((char*)cb - (char*)m)); return b;
}
EXPORT void ReShadeRegisterEvent(uint32_t ev, void* cb) {
    L("RegisterEvent %s (%u) cb=%s", evName(ev), ev, where(cb).c_str());
    std::lock_guard<std::mutex> lk(g_mu); g_events.push_back({ev, cb});
}
EXPORT void ReShadeRegisterEventForAddon(void* module, uint32_t ev, void* cb) {
    L("RegisterEventForAddon mod=%s %s (%u) cb=%s", modName(module).c_str(), evName(ev), ev, where(cb).c_str());
    std::lock_guard<std::mutex> lk(g_mu); g_events.push_back({ev, cb});
}
EXPORT void ReShadeUnregisterEvent(uint32_t ev, void* cb) {
    L("UnregisterEvent %s (%u) cb=%p", evName(ev), ev, cb);
    std::lock_guard<std::mutex> lk(g_mu);
    g_events.erase(std::remove_if(g_events.begin(), g_events.end(), [&](const Ev& e) { return e.ev == ev && e.cb == cb; }), g_events.end());
}
EXPORT void ReShadeUnregisterEventForAddon(void* module, uint32_t ev, void* cb) { ReShadeUnregisterEvent(ev, cb); }
EXPORT void ReShadeRegisterOverlay(const char* title, void (*cb)(void*)) {
    L("RegisterOverlay title=\"%s\" cb=%s", esc(title).c_str(), where((void*)cb).c_str());
    std::lock_guard<std::mutex> lk(g_mu); g_overlays.push_back({title ? title : "", cb});
}
EXPORT void ReShadeRegisterOverlayForAddon(void* module, const char* title, void (*cb)(void*)) { ReShadeRegisterOverlay(title, cb); }
EXPORT void ReShadeUnregisterOverlay(const char* title, void (*cb)(void*)) {
    L("UnregisterOverlay title=\"%s\" cb=%p", esc(title).c_str(), (void*)cb);
    std::lock_guard<std::mutex> lk(g_mu);
    g_overlays.erase(std::remove_if(g_overlays.begin(), g_overlays.end(), [&](const Ov& o) { return o.cb == cb; }), g_overlays.end());
}
EXPORT void ReShadeUnregisterOverlayForAddon(void* module, const char* title, void (*cb)(void*)) { ReShadeUnregisterOverlay(title, cb); }
EXPORT bool ReShadeCreateEffectRuntime(int, void*, void*, void*, const char*, void** out) { L("CreateEffectRuntime -> false"); if (out) *out = nullptr; return false; }
EXPORT void ReShadeDestroyEffectRuntime(void* r) { L("DestroyEffectRuntime %p", r); }
EXPORT void ReShadeUpdateAndPresentEffectRuntime(void* r) { L("UpdateAndPresentEffectRuntime %p", r); }

static char g_ioBuf[1 << 16];
static char g_styleBuf[1 << 14];
static char g_fontBuf[1 << 14];
static char g_dlBuf[1 << 12];
static char g_miscBuf[1 << 12];
EXPORT const void* ReShadeGetImGuiFunctionTable(uint32_t version) {
    L("GetImGuiFunctionTable version=%u -> %s (%d slots)", version, version == 19250 ? "table" : "NULL", kImCount);
    return version == 19250 ? (const void*)kImTable : nullptr;
}

// ---- ImGui stub policy
static std::set<std::string> g_true, g_false;
static std::vector<std::string> g_imNamesStr;
static std::map<std::string, uint32_t> g_floatRet;
static void initPolicy() {
    for (const char* n : {"Begin", "BeginChild", "BeginChild2", "BeginTabBar", "BeginTabItem", "BeginTable", "BeginMenu", "BeginCombo", "BeginListBox",
                          "BeginMenuBar", "BeginPopup", "BeginPopupModal", "TreeNode", "TreeNodeV", "TreeNodeV2", "TreeNodeEx", "TreeNodeExV", "TreeNodeExV2",
                          "CollapsingHeader", "CollapsingHeader2", "TableNextColumn", "TableSetColumnIndex", "TableNextRow", "BeginTooltip", "BeginItemTooltip"})
        g_true.insert(n);
    auto fb = [](float f) { uint32_t u; memcpy(&u, &f, 4); return u; };
    g_floatRet = {{"GetFontSize", fb(13)}, {"GetTextLineHeight", fb(13)}, {"GetTextLineHeightWithSpacing", fb(17)}, {"GetFrameHeight", fb(21)},
                  {"GetFrameHeightWithSpacing", fb(25)}, {"CalcItemWidth", fb(300)}, {"GetWindowDpiScale", fb(1)}, {"GetWindowWidth", fb(600)},
                  {"GetWindowHeight", fb(800)}, {"GetColumnWidth", fb(300)}};
    for (int i = 0; i < kImCount; ++i) g_imNamesStr.push_back(kImNames[i]);
}

static std::string describe(uint64_t v) {
    char b[160];
    size_t len;
    const char* p = (const char*)v;
    if (v > 0x10000 && isText(p, &len)) return "\"" + esc(p, 300) + "\"";
    if (v > 0x10000 && readable(p, 8)) {
        int32_t i32; float f32; uint8_t u8; memcpy(&i32, p, 4); memcpy(&f32, p, 4); u8 = *(const uint8_t*)p;
        int32_t i2 = 0; float f2 = 0; if (readable(p + 4, 4)) { memcpy(&i2, p + 4, 4); memcpy(&f2, p + 4, 4); }
        snprintf(b, sizeof b, "ptr(%p u8=%u i32=%d f32=%g | +4 i32=%d f32=%g)", p, u8, i32, f32, i2, f2);
        std::string s = b;
        const char* const* arr = (const char* const*)p;
        std::string items; int cnt = 0;
        for (int i = 0; i < 24 && readable(arr + i, 8); ++i) {
            size_t l; if (!isText(arr[i], &l)) break;
            items += (cnt ? " | " : "") + esc(arr[i], 120); ++cnt;
        }
        if (cnt >= 2) s += " items[" + std::to_string(cnt) + "]={" + items + "}";
        return s;
    }
    snprintf(b, sizeof b, "%llu(0x%llx)", (unsigned long long)v, (unsigned long long)v);
    return b;
}

static bool nameHas(const std::string& n, const char* s) { return n.find(s) != std::string::npos; }

extern "C" uint64_t ig_handle(unsigned idx, Regs* r) {
    if (idx >= (unsigned)kImCount) {
        unsigned slot = idx - kImCount; void* self = (void*)r->gpr[0];
        static std::map<std::string, uint64_t> priv;
        static void* fakeDevPtr = nullptr;
        if (slot == 1 || slot == 2) {
            std::string key((const char*)r->gpr[1], 16); key += std::to_string((uintptr_t)self);
            if (slot == 2) { priv[key] = r->gpr[2]; L("  OBJ(%p)->set_private_data(guid, %p)", self, (void*)r->gpr[2]); return 0; }
            uint64_t v = priv.count(key) ? priv[key] : 0; if (r->gpr[2]) *(uint64_t*)r->gpr[2] = v;
            L("  OBJ(%p)->get_private_data(guid) -> %p", self, (void*)v); return 0;
        }
        if (slot == 3 && self == (void*)g_fakeRt) { L("  RUNTIME->get_device() -> fakeDevice"); return (uint64_t)g_fakeDev; }
        (void)fakeDevPtr;
        if (self == (void*)g_fakeDev) L("  DEVICE->vfn[%u]", slot);
        else
        L("  RUNTIME->vfn[%u](this=%p, %s, %s, %s)", idx - kImCount, (void*)r->gpr[0], describe(r->gpr[1]).c_str(), describe(r->gpr[2]).c_str(), describe(r->gpr[3]).c_str());
        return 0;
    }
    const std::string& n = g_imNamesStr[idx];
    std::string args;
    int fmtIdx = -1;
    if (n == "TextV" || n == "BulletTextV" || n == "TextDisabledV" || n == "TextWrappedV" || n == "SetTooltipV" || n == "SetItemTooltipV") fmtIdx = 0;
    else if (n == "LabelTextV" || n == "TextColoredV" || n == "TreeNodeV") fmtIdx = 1;
    if (fmtIdx >= 0 && readable((void*)r->gpr[fmtIdx], 1) && readable((void*)r->gpr[fmtIdx + 1], 8)) {
        char out[2048]; va_list ap; memcpy(&ap, &r->gpr[fmtIdx + 1], sizeof(void*));
        __mingw_vsnprintf(out, sizeof out, (const char*)r->gpr[fmtIdx], ap);
        std::string pre; for (int i = 0; i < fmtIdx; ++i) pre += describe(r->gpr[i]) + ", ";
        L("  ImGui::%s(%s\"%s\")", n.c_str(), pre.c_str(), esc(out, 1500).c_str());
    } else {
        bool widget = nameHas(n, "Slider") || nameHas(n, "Drag") || nameHas(n, "Input") || nameHas(n, "Checkbox") || nameHas(n, "Combo") || nameHas(n, "ColorEdit") || nameHas(n, "RadioButton");
        for (int i = 0; i < 4; ++i) {
            if (i == 1 && widget && readable((void*)r->gpr[1], 4)) {
                const uint8_t* q = (const uint8_t*)r->gpr[1]; int32_t i32; float f32; memcpy(&i32, q, 4); memcpy(&f32, q, 4);
                int32_t i2 = 0; float f2 = 0; if (readable(q + 4, 4)) { memcpy(&i2, q + 4, 4); memcpy(&f2, q + 4, 4); }
                char b[200]; snprintf(b, sizeof b, "VAL(u8=%u i32=%d f32=%g | +4 i32=%d f32=%g)", q[0], i32, f32, i2, f2); args += std::string(", ") + b;
            } else args += (i ? ", " : "") + describe(r->gpr[i]);
        }
        for (int i = 0; i < 2; ++i) if (r->stk[i]) args += ", s" + std::to_string(i + 5) + "=" + describe(r->stk[i]);
        if (nameHas(n, "Slider") || nameHas(n, "Drag") || nameHas(n, "Input") || nameHas(n, "Progress")) {
            char b[200]; float f[4]; for (int i = 0; i < 4; ++i) memcpy(&f[i], &r->xmm[i], 4);
            snprintf(b, sizeof b, " xmmf=[%g,%g,%g,%g]", f[0], f[1], f[2], f[3]); args += b;
        }
        L("  ImGui::%s(%s)", n.c_str(), args.c_str());
    }
    if (g_false.count(n)) return 0;
    if (g_true.count(n)) return 1;
    auto fr = g_floatRet.find(n); if (fr != g_floatRet.end()) return fr->second;
    if (n == "GetIO") return (uint64_t)g_ioBuf;
    if (n == "GetStyle") return (uint64_t)g_styleBuf;
    if (n == "GetStyleColorVec4") { static float c[4] = {0.5f, 0.5f, 0.5f, 1.0f}; return (uint64_t)c; }
    if (n == "GetVersion") return (uint64_t)"1.92.5";
    if (n == "GetFont" || n == "GetFontBaked") return (uint64_t)g_fontBuf;
    if (n == "GetWindowDrawList" || n == "GetBackgroundDrawList" || n == "GetForegroundDrawList") return (uint64_t)g_dlBuf;
    if (n == "GetColorU32") return 0xFF000000u | (uint32_t)(((r->gpr[0] & 0xFF) + 1) * 0x010101u & 0xFFFFFFu);
    if (n == "GetColorU322") return (uint32_t)r->gpr[0];
    if (n == "GetColorU323" && readable((void*)r->gpr[0], 16)) { const float* c = (const float*)r->gpr[0]; auto b = [](float f) { int v = (int)(f * 255.f + .5f); return (uint32_t)(v < 0 ? 0 : v > 255 ? 255 : v); }; return b(c[0]) | (b(c[1]) << 8) | (b(c[2]) << 16) | (b(c[3]) << 24); }
    if (n == "GetStyleColorName") return (uint64_t)"Col";
    if (n == "GetKeyName") return (uint64_t)"Key";
    if (n == "GetClipboardText") return (uint64_t)"";
    if (n == "TableGetColumnCount") return 2;
    if (n == "MemAlloc") return (uint64_t)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, r->gpr[0] ? r->gpr[0] : 1);
    if (n == "GetStateStorage" || n == "GetDrawListSharedData" || n == "TableGetSortSpecs" || n == "GetDragDropPayload" || n == "AcceptDragDropPayload" || n == "BeginMultiSelect") return (uint64_t)g_miscBuf;
    return 0;
}

// ---- crash reporting
static LONG WINAPI veh(EXCEPTION_POINTERS* ep) {
    DWORD code = ep->ExceptionRecord->ExceptionCode;
    if ((code & 0xF0000000) != 0xC0000000) return EXCEPTION_CONTINUE_SEARCH;
    void* a = ep->ExceptionRecord->ExceptionAddress;
    HMODULE km = nullptr; GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCSTR)a, &km);
    if (km == GetModuleHandleA("KERNEL32.DLL") || km == GetModuleHandleA("kernelbase.dll")) return EXCEPTION_CONTINUE_SEARCH;
    L("EXCEPTION code=0x%08lx at %p (%s) info0=%llu info1=0x%llx", code, a, where(a).c_str(),
      ep->ExceptionRecord->NumberParameters > 0 ? (unsigned long long)ep->ExceptionRecord->ExceptionInformation[0] : 0ull,
      ep->ExceptionRecord->NumberParameters > 1 ? (unsigned long long)ep->ExceptionRecord->ExceptionInformation[1] : 0ull);
    return EXCEPTION_CONTINUE_SEARCH;
}

static void finish(const char* why) {
    L("HOST finish: %s", why);
    std::lock_guard<std::mutex> lk(g_mu);
    if (!g_dumpPath.empty()) {
        FILE* f = fopen(g_dumpPath.c_str(), "wb");
        if (f) { fprintf(f, "; keys written by add-on (final value per section/key)\n"); dumpIni(f, g_written); fclose(f); }
    }
}

static DWORD WINAPI watchdog(void* p) {
    Sleep((DWORD)(uintptr_t)p);
    L("HOST watchdog timeout, terminating");
    finish("watchdog");
    TerminateProcess(GetCurrentProcess(), 99);
    return 0;
}

static void splitInto(const std::string& s, std::set<std::string>& out) {
    size_t p = 0;
    while (p <= s.size()) { size_t q = s.find(',', p); if (q == std::string::npos) q = s.size(); out.insert(s.substr(p, q - p)); p = q + 1; }
}

int main(int argc, char** argv) {
    g_t0 = GetTickCount();
    std::string addon, logp, iniPath; int waitMs = 3000; bool overlay = false; int passes = 1; std::vector<std::string> evList;
    std::set<std::string> userTrue;
    for (int i = 1; i < argc; ++i) {
        std::string a = argv[i];
        auto nx = [&]() { return i + 1 < argc ? std::string(argv[++i]) : std::string(); };
        if (a == "--addon") addon = nx();
        else if (a == "--log") logp = nx();
        else if (a == "--ini") { iniPath = nx(); g_iniMode = true; }
        else if (a == "--wait") waitMs = atoi(nx().c_str());
        else if (a == "--overlay") overlay = true;
        else if (a == "--passes") passes = atoi(nx().c_str());
        else if (a == "--events") { std::set<std::string> t; std::string v = nx(); size_t p = 0; while (p <= v.size()) { size_t q = v.find(',', p); if (q == std::string::npos) q = v.size(); evList.push_back(v.substr(p, q - p)); p = q + 1; } }
        else if (a == "--dump") g_dumpPath = nx();
        else if (a == "--imgui-true") splitInto(nx(), userTrue);
        else if (a == "--imgui-false") splitInto(nx(), g_false);
    }
    char exe[MAX_PATH]; GetModuleFileNameA(nullptr, exe, MAX_PATH);
    g_dir = exe; g_dir = g_dir.substr(0, g_dir.rfind('\\') + 1);
    if (!logp.empty()) { g_log = fopen(logp.c_str(), "wb"); if (!g_log) return 2; }
    initPolicy();
    for (auto& s : userTrue) g_true.insert(s);
    AddVectoredExceptionHandler(1, veh);
    CreateThread(nullptr, 0, watchdog, (void*)(uintptr_t)(waitMs + 20000), 0, nullptr);
    if (!iniPath.empty()) loadIni(iniPath.c_str());
    L("HOST start addon=%s iniMode=%d dir=%s", addon.c_str(), (int)g_iniMode, g_dir.c_str());
    HMODULE m = LoadLibraryA(addon.c_str());
    if (!m) { L("HOST LoadLibrary FAILED err=%lu", GetLastError()); finish("loadfail"); return 3; }
    L("HOST loaded %s at %p", addon.c_str(), (void*)m);
    typedef bool (*InitFn)(HMODULE, HMODULE);
    typedef void (*UninitFn)(HMODULE, HMODULE);
    if (auto init = (InitFn)GetProcAddress(m, "AddonInit")) {
        L("HOST calling AddonInit(addon, host)");
        bool ok = init(m, GetModuleHandleA(nullptr));
        L("HOST AddonInit -> %d", (int)ok);
    }
    for (const char* ex : {"NAME", "DESCRIPTION"}) {
        const char** pp = (const char**)GetProcAddress(m, ex); const char* s = (pp && readable(pp, 8)) ? *pp : nullptr;
        if (s && readable(s, 1)) L("HOST export %s = \"%s\"", ex, esc(s, 400).c_str());
    }
    Sleep(waitMs);
    L("HOST registered: %zu events, %zu overlays", g_events.size(), g_overlays.size());
    if (overlay || !evList.empty()) {
        void** fakeRt = g_fakeRt;
        fakeRt[0] = (void*)kRtVtbl; g_fakeDev[0] = (void*)kRtVtbl;
        std::vector<Ev> evs; { std::lock_guard<std::mutex> lk(g_mu); evs = g_events; }
        for (int pass = 1; pass <= passes; ++pass)
            for (auto& name : evList)
                for (auto& e : evs) {
                    if (name != evName(e.ev)) continue;
                    L("HOST === pass %d calling event %s cb=%s", pass, name.c_str(), where(e.cb).c_str());
                    uint64_t r = ((uint64_t (*)(void*, uint64_t, uint64_t))e.cb)(fakeRt, 1, 0);
                    L("HOST === returned %llu", (unsigned long long)(r & 0xff));
                }
        for (auto& o : g_overlays) {
            for (int pass = 1; pass <= 2; ++pass) {
                L("HOST === calling overlay \"%s\" pass %d", o.title.c_str(), pass);
                o.cb(fakeRt);
                L("HOST === overlay \"%s\" pass %d returned", o.title.c_str(), pass);
            }
        }
        Sleep(500);
    }
    if (auto un = (UninitFn)GetProcAddress(m, "AddonUninit")) { L("HOST calling AddonUninit"); un(m, GetModuleHandleA(nullptr)); }
    L("HOST FreeLibrary");
    FreeLibrary(m);
    L("HOST FreeLibrary done");
    finish("normal");
    return 0;
}
