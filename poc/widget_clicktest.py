# 完整点击测试：清场挡路窗口 → 命中校验 → 点折叠 → 点第一项 → 还原窗口
import sys, time, ctypes
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
m.enable_dpi_awareness()
SW_MINIMIZE, SW_RESTORE = 6, 9
LEFTDOWN, LEFTUP = 0x0002, 0x0004
GA_ROOT = 2

u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.WindowFromPoint.restype = wintypes.HWND
u.WindowFromPoint.argtypes = [wintypes.POINT]
u.GetAncestor.restype = wintypes.HWND
u.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
u.ScreenToClient.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value

def find_widget():
    prog = u.FindWindowW('Progman', 'Program Manager')
    u.FindWindowW.restype = wintypes.HWND
    u.FindWindowW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR]
    w = u.FindWindowW('ChillDesktopFolderWnd', None)
    return prog, w

def rect(h):
    r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r)); return r

def click(cx, cy):
    u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    u.SetCursorPos(cx, cy); time.sleep(0.15)
    u.mouse_event(LEFTDOWN, 0, 0, 0, 0); time.sleep(0.05)
    u.mouse_event(LEFTUP, 0, 0, 0, 0); time.sleep(0.5)

SKIP = {'Progman', 'WorkerW', 'Shell_TrayWnd', 'XamlWindow', 'Button'}
mode = sys.argv[1] if len(sys.argv) > 1 else 'full'
minimized = []
prog, w = find_widget()
if not w:
    print('widget not found'); sys.exit(1)

try:
    r = rect(w)
    cx, cy = (r.left + r.right)//2, (r.top + r.bottom)//2
    print(f'折叠态 {r.right-r.left}x{r.bottom-r.top} 中心({cx},{cy})')

    # 清场：把压在点击点上的普通顶级窗口逐一最小化
    for _ in range(12):
        h = u.WindowFromPoint(wintypes.POINT(cx, cy))
        chain = set()
        t = h
        for _d in range(8):
            if not t: break
            chain.add(t); t = u.GetParent(t)
        if w in chain:
            print('命中校验通过：WindowFromPoint = 收纳夹'); break
        root = u.GetAncestor(h, GA_ROOT)
        if root == prog or cls(root) in ('Progman', 'WorkerW'):
            print(f'命中桌面层但非收纳夹：class={cls(h)}（收纳夹未在命中链！）'); break
        if cls(root) in SKIP:
            print(f'跳过系统窗口 {cls(root)}'); break
        print(f'最小化挡路窗口 {root} class={cls(root)}')
        u.ShowWindow(root, SW_MINIMIZE); minimized.append(root)
        time.sleep(0.5)

    click(cx, cy)
    r2 = rect(w)
    print(f'点击后 {r2.right-r2.left}x{r2.bottom-r2.top}')
    if r2.right-r2.left <= 270:
        print('结果：未展开 ✗')
    elif mode == 'openonly':
        print('结果：已展开 ✓（openonly：不点击、保持桌面裸露）')
    else:
        print('结果：已展开 ✓，点击第一项……')
        ix, iy = r2.left + 80, r2.top + 66 + 36
        click(ix, iy)
        print('已点击第一项（预期启动 notepad）')
        time.sleep(1.0)
finally:
    if mode == 'openonly':
        print('MINIMIZED=' + ','.join(str(h) for h in minimized))
    else:
        for h in minimized:
            u.ShowWindow(h, SW_RESTORE)
        print(f'已还原 {len(minimized)} 个窗口')
