# 彻底清场后测 NOACTIVATE 游戏：点击原生笔记按钮，是否响应且不抬升/不抢焦点
import time, ctypes, subprocess, sys
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
m.enable_dpi_awareness()
HWND_BOTTOM = 1
WS_EX_LAYERED, WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE = 0x80000, 0x80, 0x08000000
SW_MIN, SW_RESTORE = 6, 9
SWP_NOACTIVATE = 0x10
u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.WindowFromPoint.restype = wintypes.HWND; u.WindowFromPoint.argtypes = [wintypes.POINT]
u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value

KEEP = {'Progman','WorkerW','Shell_TrayWnd','Shell_SecondaryTrayWnd'}
game = u.FindWindowW('UnityWndClass', 'Chill With You')
if u.GetParent(game): u.SetParent(game, None)
ex = u.GetWindowLongW(game, -20) & 0xffffffff
ex = (ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & 0xffffffff
u.SetWindowLongW(game, -20, ex)
u.SetLayeredWindowAttributes(game, 0, 255, 0x2)
u.ShowWindow(game, SW_RESTORE)
u.SetWindowPos(game, HWND_BOTTOM, 0, 0, 2560, 1600, SWP_NOACTIVATE)
time.sleep(0.8)

saved = []
def enumcb(h, _):
    if h == game: return True
    if not u.IsWindowVisible(h): return True
    c = cls(h)
    if c in KEEP or c == 'ChillDesktopFolderWnd': return True
    # 跳过本进程不可见/零尺寸；其余全部最小化
    r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r))
    if r.right-r.left > 40 and r.bottom-r.top > 40:
        u.ShowWindow(h, SW_MIN); saved.append((h,c))
    return True
u.EnumWindows.argtypes = [m.WNDENUMPROC, wintypes.LPARAM]
u.EnumWindows(m.WNDENUMPROC(enumcb), 0)
print('已最小化:', [c for _,c in saved])
time.sleep(1.0)

h = u.WindowFromPoint(wintypes.POINT(2454, 667))
print('笔记点命中(应游戏)=', h, cls(h), 'fg=', cls(u.GetForegroundWindow()))
subprocess.run([sys.executable,'capture_game.py','frame_na2_before.png'],capture_output=True)

u.SetCursorPos(2454, 667); time.sleep(0.2)
u.mouse_event(0x0002,0,0,0,0); time.sleep(0.09); u.mouse_event(0x0004,0,0,0,0)
time.sleep(1.1)
print('点击后 fg=', u.GetForegroundWindow(), cls(u.GetForegroundWindow()), '(非游戏=未抢焦点)')
subprocess.run([sys.executable,'capture_game.py','frame_na2_after.png'],capture_output=True)

time.sleep(0.2)
for h,c in saved:
    try: u.ShowWindow(h, SW_RESTORE)
    except: pass
print('还原', len(saved))
