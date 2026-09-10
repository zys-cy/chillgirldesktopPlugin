# 成品验证 v2：不清场、不动用户任何窗口
# 检查：Z序(办公窗 > 工具箱 > 游戏) / hit-test / 原生笔记按钮开合 / 不抢焦点 / 工具箱开文件夹
import time, ctypes, subprocess, sys
from ctypes import wintypes
import embed_wallpaper as m
import numpy as np
from PIL import Image

u = m.user32
m.enable_dpi_awareness()
u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.WindowFromPoint.restype = wintypes.HWND
u.WindowFromPoint.argtypes = [wintypes.POINT]

def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value

def zindex(target):
    """目标在顶级 Z 序中的位置（0=最顶），找不到返回 None。"""
    idx = [0]; found = []
    def cb(h, _):
        if h == target: found.append(idx[0])
        idx[0] += 1
        return True
    u.EnumWindows.argtypes = [m.WNDENUMPROC, wintypes.LPARAM]
    u.EnumWindows(m.WNDENUMPROC(cb), 0)
    return found[0] if found else None

def frame(tag):
    subprocess.run([sys.executable, 'capture_game.py', f'v2_{tag}.png'], capture_output=True)
    return np.asarray(Image.open(f'v2_{tag}.png').convert('RGB')).astype(int)

def note_panel_open(a):
    # 笔记列表选中项的品红描边（开=有，关=0）
    sub = a[640:1000, 1450:2050]
    r, g, b = sub[:,:,0], sub[:,:,1], sub[:,:,2]
    mag = (r > 130) & (b > 110) & (g < 120) & ((r.astype(int) - g) > 50)
    return int(mag.sum()) > 20

def click(x, y):
    u.SetCursorPos(x, y); time.sleep(0.2)
    u.mouse_event(0x0002, 0, 0, 0, 0); time.sleep(0.07)
    u.mouse_event(0x0004, 0, 0, 0, 0); time.sleep(0.9)

NOTE = (2454, 667)
TOOL = (2322, 667)
game = u.FindWindowW('UnityWndClass', 'Chill With You')
tool = u.FindWindowW('ChillDesktopFolderWnd', None)
fg0 = u.GetForegroundWindow()
results = []

# 仅把【恰好盖住两个测试点】的窗口原样挪到屏幕右侧外，测完精确归位；不动窗口状态/内容
KEEP = {'UnityWndClass', 'ChillDesktopFolderWnd', 'Progman', 'WorkerW',
        'Shell_TrayWnd', 'Shell_SecondaryTrayWnd', 'Windows.UI.Core.CoreWindow'}
u.MoveWindow.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.BOOL]

def blocker_at(x, y):
    hit = u.WindowFromPoint(wintypes.POINT(x, y))
    return hit if cls(hit) not in KEEP else None

moved = []
for _ in range(10):  # 迭代：移走上层遮挡者后可能还有下一层
    blockers = {blocker_at(*NOTE), blocker_at(*TOOL)} - {None}
    blockers = {h for h in blockers if h not in [m[0] for m in moved]}
    if not blockers:
        break
    for h in blockers:
        r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r))
        moved.append((h, r.left, r.top, r.right - r.left, r.bottom - r.top))
        u.MoveWindow(h, 3200 + len(moved) * 40, r.top, r.right - r.left, r.bottom - r.top, True)
    time.sleep(0.4)
if moved:
    print('临时移开遮挡窗口:', [cls(h) for h, *_ in moved]); time.sleep(0.5)
import atexit
atexit.register(lambda: [u.MoveWindow(h, x, y, w, hgt, True) for h, x, y, w, hgt in moved])

# ---- 1. Z 序与 hit-test ----
zg, zt = zindex(game), zindex(tool)
print(f'1) Z序位置: 前台办公窗={cls(fg0)}(应非Unity)  工具箱={zt}  游戏={zg}（编号小=在上）')
c_note = cls(u.WindowFromPoint(wintypes.POINT(*NOTE)))
c_tool = cls(u.WindowFromPoint(wintypes.POINT(*TOOL)))
print(f'   笔记点命中={c_note}(应UnityWndClass)  工具箱点命中={c_tool}(应ChillDesktopFolderWnd)')
results.append(('工具箱在游戏之上', zt is not None and zg is not None and zt < zg))
results.append(('工具箱可点(hit-test)', c_tool == 'ChillDesktopFolderWnd'))
results.append(('原生按钮区可点(hit-test)', c_note == 'UnityWndClass'))

# ---- 2. 原生笔记按钮：开 / 关 ----
f0 = frame('0')
if note_panel_open(f0):
    print('2) 面板初始为开，先点一下收起…'); click(*NOTE)
    f0 = frame('0b')
open0 = note_panel_open(f0)
click(*NOTE)                      # 真实点击：打开
f1 = frame('1')
open1 = note_panel_open(f1)
fg_after_click = u.GetForegroundWindow()
click(*NOTE)                      # 再点：收起
f2 = frame('2')
open2 = note_panel_open(f2)
print(f'2) 面板: 初始={"开" if open0 else "关"} -> 点击后={"开" if open1 else "关"} -> 再点后={"开" if open2 else "关"}')
print(f'   点击后前台={cls(fg_after_click)}(应仍非UnityWndClass)')
zg2 = zindex(game)
print(f'   点击后游戏Z序位置={zg2}（应仍在工具箱 {zindex(tool)} 之下）')
results.append(('原生按钮点击生效(开)', open1 and not open0))
results.append(('再次点击收起(关)', not open2))
results.append(('点击游戏不抢焦点', cls(fg_after_click) != 'UnityWndClass'))
results.append(('点击后游戏不上抬', zindex(game) > zindex(tool)))

# ---- 3. 工具箱真实点击 -> chillgirl 文件夹 ----
before = set()
def collect(h, _):
    if cls(h) == 'CabinetWClass' and u.IsWindowVisible(h): before.add(h)
    return True
u.EnumWindows(m.WNDENUMPROC(collect), 0)
click(*TOOL)
time.sleep(1.2)
opened = []
def find_new(h, _):
    if cls(h) == 'CabinetWClass' and u.IsWindowVisible(h) and h not in before:
        b = ctypes.create_unicode_buffer(512); u.GetWindowTextW(h, b, 512)
        opened.append((h, b.value))
    return True
u.EnumWindows(m.WNDENUMPROC(find_new), 0)
ok_tool = any('chillgirl' in t.lower() for _, t in opened)
print(f'3) 工具箱点击后新开资源管理器: {[t for _,t in opened]}（应含 chillgirl）')
for h, _ in opened:  # 关掉测试弹出的窗口
    u.PostMessageW(h, 0x0010, 0, 0)
results.append(('工具箱打开chillgirl', ok_tool))
results.append(('全程游戏未抢焦点', cls(u.GetForegroundWindow()) != 'UnityWndClass'))

print()
ok_all = True
for name, ok in results:
    print(('  PASS ' if ok else '  FAIL ') + name)
    ok_all &= ok
print('\n结论:', '全部通过' if ok_all else '存在失败项')

for h, x, y, w, hgt in moved:  # 遮挡窗口精确归位
    u.MoveWindow(h, x, y, w, hgt, True)
