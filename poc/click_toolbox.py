# 物理点击工具箱圆心，验证打开 chillgirl
import time, ctypes
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
m.enable_dpi_awareness()
SW_MINIMIZE, SW_RESTORE, GA_ROOT = 6, 9, 2
u.WindowFromPoint.restype = wintypes.HWND
u.WindowFromPoint.argtypes = [wintypes.POINT]
u.GetAncestor.restype = wintypes.HWND
u.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value

X, Y = 2322, 667
minimized = []
try:
    for _ in range(15):
        h = u.WindowFromPoint(wintypes.POINT(X, Y))
        chain = set(); t = h
        for _d in range(8):
            if not t: break
            chain.add(t); t = u.GetParent(t)
        if any(cls(x) == 'ChillDesktopFolderWnd' for x in chain):
            print('命中工具箱，点击'); break
        root = u.GetAncestor(h, GA_ROOT)
        if cls(root) in ('Progman', 'WorkerW'):
            print('落在桌面层，未命中工具箱'); break
        u.ShowWindow(root, SW_MINIMIZE); minimized.append(root)
        time.sleep(0.45)

    u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    u.SetCursorPos(X, Y); time.sleep(0.2)
    u.mouse_event(0x0002, 0, 0, 0, 0); time.sleep(0.06)
    u.mouse_event(0x0004, 0, 0, 0, 0)
    print('已点击')
    time.sleep(1.6)
finally:
    for h in minimized:
        u.ShowWindow(h, SW_RESTORE)
