# 诊断：点击游戏原生「笔记」按钮，输入是否到达游戏
# 1) WindowFromPoint 命中链  2) 物理点击  3) 抓帧前后对比  4) 直接 PostMessage 再对比
import time, ctypes, sys
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
m.enable_dpi_awareness()
SW_MIN, RESTORE, GA_ROOT, GA_PARENT = 6, 9, 2, 1
PW_RENDERFULLCONTENT = 2

u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.WindowFromPoint.restype = wintypes.HWND
u.WindowFromPoint.argtypes = [wintypes.POINT]
u.GetAncestor.restype = wintypes.HWND
u.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
u.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
u.ChildWindowFromPointEx.restype = wintypes.HWND
u.ChildWindowFromPointEx.argtypes = [wintypes.HWND, wintypes.POINT, wintypes.UINT]

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value
def txt(h):
    b = ctypes.create_unicode_buffer(256); u.GetWindowTextW(h, b, 256); return b.value

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
print('game style=0x%08x ex=0x%08x enabled=%d' % (
    u.GetWindowLongW(game, -16) & 0xffffffff, u.GetWindowLongW(game, -20) & 0xffffffff,
    u.IsWindowEnabled(game)))

NOTE = (2454, 667)

def grab(tag):
    import subprocess
    subprocess.run([sys.executable, 'capture_game.py', f'frame_{tag}.png'],
                   capture_output=True)

# 清场
minw = []
for _ in range(15):
    h = u.WindowFromPoint(wintypes.POINT(*NOTE)); root = u.GetAncestor(h, GA_ROOT)
    if root in (prog, game) or cls(root) in ('Progman', 'WorkerW'): break
    u.ShowWindow(root, SW_MIN); minw.append(root); time.sleep(0.4)
time.sleep(0.4)

# 命中链
h = u.WindowFromPoint(wintypes.POINT(*NOTE))
print('--- WindowFromPoint(笔记) 链 ---')
t = h
for d in range(6):
    if not t: break
    print(f'  {t} class={cls(t)!r} title={txt(t)!r}')
    t = u.GetParent(t)
cp = wintypes.POINT(*NOTE)
print('ChildWindowFromPointEx(Progman, 屏幕坐标未转)=', u.ChildWindowFromPointEx(prog, cp, 0))

try:
    grab('before')
    # 物理点击
    u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    u.SetCursorPos(*NOTE); time.sleep(0.2)
    u.mouse_event(0x0002, 0, 0, 0, 0); time.sleep(0.08); u.mouse_event(0x0004, 0, 0, 0, 0)
    print('物理点击完成'); time.sleep(1.2)
    grab('after_physical')

    # 直接给游戏窗口 PostMessage（客户区坐标 = 屏幕坐标，因为铺满原点0,0）
    lp = (NOTE[1] << 16) | (NOTE[0] & 0xffff)
    u.PostMessageW(game, 0x0201, 1, lp); time.sleep(0.06)
    u.PostMessageW(game, 0x0202, 0, lp)
    print('PostMessage 点击完成'); time.sleep(1.2)
    grab('after_post')
finally:
    for h in minw: u.ShowWindow(h, RESTORE)
    print('还原', len(minw), '窗')
