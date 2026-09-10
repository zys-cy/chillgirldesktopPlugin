# 测试顶级沉底游戏在「办公窗口在前」时点击壁纸，是否会抬升盖住办公窗口
import time, ctypes, subprocess
from ctypes import wintypes
import embed_wallpaper as m
from PIL import ImageGrab

u = m.user32
m.enable_dpi_awareness()
HWND_BOTTOM, TOP = 1, 0
SW_SHOWNOACTIVATE, SW_RESTORE = 4, 9
SWP_NOACTIVATE = 0x10

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value
def title(h):
    b = ctypes.create_unicode_buffer(256); u.GetWindowTextW(h, b, 256); return b.value

game = u.FindWindowW('UnityWndClass', 'Chill With You')
print('game parent=', u.GetParent(game), 'iconic=', u.IsIconic(game))
# 恢复并重新铺满沉底（不激活）
u.ShowWindow(game, SW_RESTORE)
u.SetWindowPos(game, HWND_BOTTOM, 0, 0, 2560, 1600, SWP_NOACTIVATE)
time.sleep(1.0)

# 开一个记事本挡在中间
np = subprocess.Popen(['notepad.exe'])
time.sleep(1.5)
note = u.FindWindowW('Notepad', None)
if not note:
    note = u.FindWindowW(None, '无标题 - 记事本')
print('notepad=', note, 'fg before=', u.GetForegroundWindow())
u.SetWindowPos(note, -1, 500, 400, 900, 600, SWP_NOACTIVATE)  # topmost 便于观察
time.sleep(0.6)
ImageGrab.grab((0, 0, 2560, 1600)).resize((960, 600)).save('raise_before.png')

# 点击左侧游戏壁纸空白处（x=250,y=900，远离右侧UI）
u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
u.SetCursorPos(250, 900); time.sleep(0.2)
u.mouse_event(0x0002, 0, 0, 0, 0); time.sleep(0.07); u.mouse_event(0x0004, 0, 0, 0, 0)
time.sleep(1.0)
fg = u.GetForegroundWindow()
print('点击壁纸后 fg=', fg, cls(fg), title(fg))
# 查记事本是否仍可见/在前：取点 (950,700)（记事本区域）
u.WindowFromPoint.restype = wintypes.HWND; u.WindowFromPoint.argtypes = [wintypes.POINT]
h = u.WindowFromPoint(wintypes.POINT(950, 700))
print('记事本区域现在命中=', h, cls(h), title(h))
ImageGrab.grab((0, 0, 2560, 1600)).resize((960, 600)).save('raise_after.png')
# 清理：关记事本
u.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
u.PostMessageW(note, 0x0010, 0, 0)
