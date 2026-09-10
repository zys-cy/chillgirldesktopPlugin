# 实验：游戏改为顶级 WS_POPUP 铺满 + HWND_BOTTOM（Rainmeter 式），验证显示与真实点击
import time, ctypes, subprocess, sys
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
m.enable_dpi_awareness()
WS_POPUP, WS_VISIBLE, WS_CLIPSIBLINGS = 0x80000000, 0x10000000, 0x04000000
WS_CHILD = 0x40000000
WS_EX_LAYERED, WS_EX_TOOLWINDOW = 0x80000, 0x80
HWND_BOTTOM = 1
SW_MIN, RESTORE, GA_ROOT = 6, 9, 2
SWP_SHOWWINDOW, SWP_NOACTIVATE = 0x40, 0x10

u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.WindowFromPoint.restype = wintypes.HWND
u.WindowFromPoint.argtypes = [wintypes.POINT]
u.GetAncestor.restype = wintypes.HWND
u.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value

prog = u.FindWindowW('Progman', 'Program Manager')
game = None
def cb(h, _):
    global game
    if cls(h) == 'UnityWndClass': game = h
    return True
u.EnumChildWindows.argtypes = [wintypes.HWND, m.WNDENUMPROC, wintypes.LPARAM]
u.EnumChildWindows(prog, m.WNDENUMPROC(cb), 0)
if not game: game = u.FindWindowW('UnityWndClass', 'Chill With You')
print('game=', game, 'parent=', u.GetParent(game))

# 1) 脱离 Progman，改顶级 popup 工具窗
u.SetParent(game, None)
style = u.GetWindowLongW(game, -16)
style = (style & ~WS_CHILD) | WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS
u.SetWindowLongW(game, -16, style)
ex = u.GetWindowLongW(game, -20)
u.SetWindowLongW(game, -20, (ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW) & 0xffffffff)
u.SetLayeredWindowAttributes(game, 0, 255, 0x2)
# 铺满主屏、沉底、不激活
u.SetWindowPos(game, HWND_BOTTOM, 0, 0, 2560, 1600, SWP_SHOWWINDOW | SWP_NOACTIVATE)
print('已改顶级沉底，parent=', u.GetParent(game))
time.sleep(1.5)

NOTE = (2454, 667)
minw = []
try:
    for _ in range(15):
        h = u.WindowFromPoint(wintypes.POINT(*NOTE)); root = u.GetAncestor(h, GA_ROOT)
        if root == game: print('WindowFromPoint 命中游戏 ✓'); break
        if cls(root) in ('Progman', 'WorkerW'):
            print('命中桌面层而非游戏 ✗ class=', cls(h)); break
        u.ShowWindow(root, SW_MIN); minw.append(root); time.sleep(0.4)

    subprocess.run([sys.executable, 'capture_game.py', 'frame_tl_before.png'], capture_output=True)
    u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    u.SetCursorPos(*NOTE); time.sleep(0.2)
    u.mouse_event(0x0002, 0, 0, 0, 0); time.sleep(0.08); u.mouse_event(0x0004, 0, 0, 0, 0)
    print('物理点击笔记'); time.sleep(1.3)
    subprocess.run([sys.executable, 'capture_game.py', 'frame_tl_after.png'], capture_output=True)
finally:
    for h in minw: u.ShowWindow(h, RESTORE)
print('完成')
