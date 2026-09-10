# 受控测试：干净桌面 + 右半屏记事本，点击左半屏游戏，观察是否抬升
import time, ctypes, subprocess
from ctypes import wintypes
import embed_wallpaper as m
from PIL import ImageGrab

u = m.user32
m.enable_dpi_awareness()
WS_EX_APPWINDOW = 0x40000
WS_EX_TOOLWINDOW = 0x80
HWND_BOTTOM = 1
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
u.SetWindowPos(game, HWND_BOTTOM, 0, 0, 2560, 1600, SWP_NOACTIVATE)
time.sleep(0.5)

saved = []
def enumcb(h, _):
    if not u.IsWindowVisible(h): return True
    c = cls(h)
    if c in SKIP or c == 'UnityWndClass' or c == 'ChillDesktopFolderWnd': return True
    ex = u.GetWindowLongW(h, -20) & 0xffffffff
    r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r))
    normal = (ex & WS_EX_TOOLWINDOW) == 0 and (r.right - r.left) > 200 and (r.bottom - r.top) > 150
    if normal:
        u.ShowWindow(h, SW_MIN); saved.append(h)
    return True
u.EnumWindows.argtypes = [m.WNDENUMPROC, wintypes.LPARAM]
u.EnumWindows(m.WNDENUMPROC(enumcb), 0)
print('最小化普通窗口', len(saved))
time.sleep(0.8)
h = u.WindowFromPoint(wintypes.POINT(400, 800))
print('左半屏命中(应为游戏)=', h, cls(h))

note = None
try:
    subprocess.Popen(['notepad.exe']); time.sleep(1.5)
    note = u.FindWindowW('Notepad', None)
    u.SetWindowPos(note, 0, 1400, 200, 1100, 1100, SWP_NOACTIVATE)  # 右半屏，非topmost
    time.sleep(0.6)
    ImageGrab.grab((0,0,2560,1600)).resize((960,600)).save('raise2_before.png')
    print('记事本区域(1900,700)命中前=', cls(u.WindowFromPoint(wintypes.POINT(1900,700))))

    u.SetCursorPos(400, 800); time.sleep(0.2)
    u.mouse_event(0x0002,0,0,0,0); time.sleep(0.07); u.mouse_event(0x0004,0,0,0,0)
    time.sleep(1.0)
    fg = u.GetForegroundWindow()
    print('点击左侧游戏后 fg=', fg, cls(fg), '(game=%d)'%game)
    print('记事本区域(1900,700)命中后=', cls(u.WindowFromPoint(wintypes.POINT(1900,700))))
    ImageGrab.grab((0,0,2560,1600)).resize((960,600)).save('raise2_after.png')
    u.PostMessageW.argtypes=[wintypes.HWND,wintypes.UINT,wintypes.WPARAM,wintypes.LPARAM]
finally:
    if note: u.PostMessageW(note, 0x0010, 0, 0)
    time.sleep(0.3)
    for h in saved:
        try: u.ShowWindow(h, SW_RESTORE)
        except: pass
    print('还原', len(saved))
