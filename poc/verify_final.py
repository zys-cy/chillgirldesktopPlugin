# 启动器成品端到端验证（清场，不最小化游戏/工具箱）
import time, ctypes, subprocess, sys
from ctypes import wintypes
import embed_wallpaper as m
import numpy as np
from PIL import Image

u = m.user32
m.enable_dpi_awareness()
SW_MIN, SW_RESTORE = 6, 9
u.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.WindowFromPoint.restype = wintypes.HWND; u.WindowFromPoint.argtypes = [wintypes.POINT]
u.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
def cls(h):
    b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256); return b.value

KEEP = {'Progman','WorkerW','Shell_TrayWnd','Shell_SecondaryTrayWnd'}
game = u.FindWindowW('UnityWndClass','Chill With You')
saved=[]
def cb(h,_):
    if h==game or not u.IsWindowVisible(h): return True
    c=cls(h)
    if c in KEEP or c=='ChillDesktopFolderWnd': return True
    r=wintypes.RECT(); u.GetWindowRect(h,ctypes.byref(r))
    if r.right-r.left>40 and r.bottom-r.top>40:
        u.ShowWindow(h,SW_MIN); saved.append(h)
    return True
u.EnumWindows.argtypes=[m.WNDENUMPROC,wintypes.LPARAM]
u.EnumWindows(m.WNDENUMPROC(cb),0)
time.sleep(1.0)

def frame(tag):
    subprocess.run([sys.executable,'capture_game.py',f'vf_{tag}.png'],capture_output=True)
    return np.asarray(Image.open(f'vf_{tag}.png').convert('RGB')).astype(int)

def panel_open(a):
    # 笔记面板标题栏区域在白天房间里是深色
    return a[500:560,1520:1680].mean() < 80

def click(x,y):
    u.SetCursorPos(x,y); time.sleep(0.18)
    u.mouse_event(0x0002,0,0,0,0); time.sleep(0.08); u.mouse_event(0x0004,0,0,0,0)
    time.sleep(1.0)

NOTE=(2454,667); TOOL=(2322,667)
try:
    print('1) 笔记点 WindowFromPoint =', cls(u.WindowFromPoint(wintypes.POINT(*NOTE))),
          '(应 UnityWndClass)')
    print('   工具箱点命中 =', cls(u.WindowFromPoint(wintypes.POINT(*TOOL))),
          '(应 ChillDesktopFolderWnd)')
    f0=frame('0'); open0=panel_open(f0)
    print('   初始笔记面板:', '开' if open0 else '关', ' 前台=',cls(u.GetForegroundWindow()))
    click(*NOTE)
    f1=frame('1'); open1=panel_open(f1)
    print('2) 点击1次后面板:', '开' if open1 else '关',
          ' 前台=',cls(u.GetForegroundWindow()),'(不应为Unity)')
    changed=np.abs(f1-f0).max(2)>30
    print('   画面确实变化占比:', round(changed.mean(),4),'(应明显>0.02)')
    # 关回，使最终为关
    if panel_open(f1):
        click(*NOTE); f2=frame('2')
        print('3) 再次点击后面板:', '开' if panel_open(f2) else '关（已收起）')
    else:
        print('3) 面板本就是关，结束')
    print('结论: 原生UI可点击 =', open1 != open0, '; 游戏未抢焦点 =',
          cls(u.GetForegroundWindow())!='UnityWndClass')
finally:
    for h in saved:
        try: u.ShowWindow(h,SW_RESTORE)
        except: pass
    print('还原窗口', len(saved))
