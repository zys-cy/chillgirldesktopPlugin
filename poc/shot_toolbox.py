# 清场后截取「工具箱 + 笔记」区域，验证位置/大小/风格对齐，随后还原窗口
import time, ctypes, sys
from ctypes import wintypes
import embed_wallpaper as m
from PIL import ImageGrab

u = m.user32
m.enable_dpi_awareness()
SW_MINIMIZE, SW_RESTORE, GA_ROOT = 6, 9, 2
u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.WindowFromPoint.restype = wintypes.HWND
u.WindowFromPoint.argtypes = [wintypes.POINT]
u.GetAncestor.restype = wintypes.HWND
u.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value

TOOLX, TOOLY = 2322, 667
minimized = []
try:
    for _ in range(15):
        h = u.WindowFromPoint(wintypes.POINT(TOOLX, TOOLY))
        chain = set(); t = h
        for _d in range(8):
            if not t: break
            chain.add(t); t = u.GetParent(t)
        if any(cls(x) == 'ChillDesktopFolderWnd' for x in chain):
            print('圆心命中工具箱 ✓'); break
        root = u.GetAncestor(h, GA_ROOT)
        if cls(root) in ('Progman', 'WorkerW'):
            print(f'圆心落在桌面层 class={cls(h)}（工具箱未拦截？）'); break
        print(f'最小化 {root} {cls(root)}')
        u.ShowWindow(root, SW_MINIMIZE); minimized.append(root)
        time.sleep(0.45)

    time.sleep(0.4)
    # 两个圆一起：工具箱(2322) 与 笔记(2454)
    im = ImageGrab.grab(bbox=(2230, 560, 2540, 780))
    out = sys.argv[1] if len(sys.argv) > 1 else r'toolbox_live.png'
    im.save(out); print('saved', out, im.size)
finally:
    for h in minimized:
        u.ShowWindow(h, SW_RESTORE)
    print(f'还原 {len(minimized)} 窗')
