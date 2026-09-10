# 关键实验：游戏顶级 + WS_EX_NOACTIVATE + 沉底，能否既响应原生按钮，又不抢焦点/不抬升
import time, ctypes, subprocess, sys
from ctypes import wintypes
import embed_wallpaper as m
from PIL import ImageGrab

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

SKIP = {'Progman','WorkerW','Shell_TrayWnd','Shell_SecondaryTrayWnd','XamlWindow',
        'ApplicationManager_DesktopShellWindow','Windows.UI.Core.CoreWindow'}
game = u.FindWindowW('UnityWndClass', 'Chill With You')

# 确保顶级
if u.GetParent(game): u.SetParent(game, None)
u.ShowWindow(game, SW_RESTORE)
ex = u.GetWindowLongW(game, -20) & 0xffffffff
ex = (ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & 0xffffffff
u.SetWindowLongW(game, -20, ex)
u.SetLayeredWindowAttributes(game, 0, 255, 0x2)
u.SetWindowPos(game, HWND_BOTTOM, 0, 0, 2560, 1600, SWP_NOACTIVATE)
print('exstyle=0x%08x 含NOACTIVATE=%d' % (ex, bool(ex & WS_EX_NOACTIVATE)))
time.sleep(1.0)

saved = []
def enumcb(h, _):
    if not u.IsWindowVisible(h): return True
    c = cls(h)
    if c in SKIP or c == 'UnityWndClass' or c == 'ChillDesktopFolderWnd': return True
    exx = u.GetWindowLongW(h, -20) & 0xffffffff
    r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r))
    if not (exx & WS_EX_TOOLWINDOW) and r.right-r.left > 200 and r.bottom-r.top > 150:
        u.ShowWindow(h, SW_MIN); saved.append(h)
    return True
u.EnumWindows.argtypes = [m.WNDENUMPROC, wintypes.LPARAM]
u.EnumWindows(m.WNDENUMPROC(enumcb), 0)
time.sleep(0.8)

note = None
try:
    subprocess.Popen(['notepad.exe']); time.sleep(1.5)
    note = u.FindWindowW('Notepad', None)
    u.SetWindowPos(note, 0, 1400, 200, 1100, 1100, SWP_NOACTIVATE)
    time.sleep(0.6)
    print('点击前 fg=', u.GetForegroundWindow(), cls(u.GetForegroundWindow()))
    subprocess.run([sys.executable,'capture_game.py','frame_na_before.png'],capture_output=True)

    # 点笔记按钮（右半屏记事本不覆盖 2454,667？记事本 x1400..2500 覆盖2454！改放左下）
    # 记事本移到左中，让出右侧UI
    u.SetWindowPos(note, 0, 60, 300, 900, 900, SWP_NOACTIVATE)
    time.sleep(0.4)
    h = u.WindowFromPoint(wintypes.POINT(2454, 667))
    print('笔记点命中(应游戏)=', h, cls(h))
    u.SetCursorPos(2454, 667); time.sleep(0.2)
    u.mouse_event(0x0002,0,0,0,0); time.sleep(0.08); u.mouse_event(0x0004,0,0,0,0)
    time.sleep(1.1)
    fg = u.GetForegroundWindow()
    print('点击后 fg=', fg, cls(fg), '记事本仍在前=', fg==note)
    print('记事本区域(450,700)命中=', cls(u.WindowFromPoint(wintypes.POINT(450,700))))
    subprocess.run([sys.executable,'capture_game.py','frame_na_after.png'],capture_output=True)
finally:
    if note:
        u.PostMessageW.argtypes=[wintypes.HWND,wintypes.UINT,wintypes.WPARAM,wintypes.LPARAM]
        u.PostMessageW(note,0x0010,0,0)
    time.sleep(0.3)
    for h in saved:
        try: u.ShowWindow(h, SW_RESTORE)
        except: pass
    print('还原', len(saved))
