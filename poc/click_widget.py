# 真实鼠标点击收纳夹（走系统命中测试）。用法：python click_widget.py toggle|item
import sys, ctypes
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
m.enable_dpi_awareness()

LEFTDOWN, LEFTUP = 0x0002, 0x0004

prog = u.FindWindowW('Progman', 'Program Manager')
hwnd = None
def cb(h, _):
    global hwnd
    b = ctypes.create_unicode_buffer(256); u.GetWindowTextW(h, b, 256)
    if b.value == 'ChillDesktopFolder':
        hwnd = h
    return True
u.EnumChildWindows.argtypes = [wintypes.HWND, m.WNDENUMPROC, wintypes.LPARAM]
u.EnumChildWindows(prog, m.WNDENUMPROC(cb), 0)
if not hwnd:
    print('widget not found'); sys.exit(1)

r = wintypes.RECT()
u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.GetWindowRect(hwnd, ctypes.byref(r))
print(f'widget rect=({r.left},{r.top},{r.right},{r.bottom}) {r.right-r.left}x{r.bottom-r.top}')

mode = sys.argv[1] if len(sys.argv) > 1 else 'toggle'
if mode == 'item':
    # 展开态：第一项行中心（scale=1.5: headerH=66, itemH=72）
    cx, cy = r.left + 60, r.top + 66 + 36
else:
    cx, cy = (r.left + r.right) // 2, (r.top + r.bottom) // 2
print(f'click at ({cx},{cy})')

u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
u.SetCursorPos(cx, cy)
u.mouse_event(LEFTDOWN, 0, 0, 0, 0)
u.mouse_event(LEFTUP, 0, 0, 0, 0)
print('clicked')
