# 直接从游戏窗口抓帧（PrintWindow PW_RENDERFULLCONTENT），不受前台遮挡影响。
# 用法：python capture_game.py [输出路径]
import sys, ctypes
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
g = ctypes.windll.gdi32
m.enable_dpi_awareness()

PW_RENDERFULLCONTENT = 2

# 找游戏窗口（顶级或 Progman 子窗口）
game = u.FindWindowW('UnityWndClass', 'Chill With You')
if not game:
    prog = u.FindWindowW('Progman', 'Program Manager')
    found = [None]
    def cb(h, _):
        b = ctypes.create_unicode_buffer(256); u.GetClassNameW(h, b, 256)
        if b.value == 'UnityWndClass': found[0] = h
        return True
    u.EnumChildWindows.argtypes = [wintypes.HWND, m.WNDENUMPROC, wintypes.LPARAM]
    u.EnumChildWindows(prog, m.WNDENUMPROC(cb), 0)
    game = found[0]
print('game hwnd=', game)

r = wintypes.RECT()
u.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.GetClientRect(game, ctypes.byref(r))
W, H = r.right - r.left, r.bottom - r.top
print('client', W, H)

hdcScreen = u.GetDC(None)
hdcMem = g.CreateCompatibleDC(hdcScreen)

class BMI(ctypes.Structure):
    _fields_ = [('biSize', wintypes.DWORD), ('biWidth', wintypes.LONG),
                ('biHeight', wintypes.LONG), ('biPlanes', wintypes.WORD),
                ('biBitCount', wintypes.WORD), ('biCompression', wintypes.DWORD),
                ('biSizeImage', wintypes.DWORD), ('biXPelsPerMeter', wintypes.LONG),
                ('biYPelsPerMeter', wintypes.LONG), ('biClrUsed', wintypes.DWORD),
                ('biClrImportant', wintypes.DWORD)]
bmi = BMI()
bmi.biSize = ctypes.sizeof(BMI); bmi.biWidth = W; bmi.biHeight = -H
bmi.biPlanes = 1; bmi.biBitCount = 32; bmi.biCompression = 0  # BI_RGB

bits = ctypes.c_void_p()
hBmp = g.CreateDIBSection(hdcScreen, ctypes.byref(bmi), 0, ctypes.byref(bits), None, 0)
old = g.SelectObject(hdcMem, hBmp)

ok = u.PrintWindow.argtypes if hasattr(u, 'PrintWindow') else None
u.PrintWindow = ctypes.windll.user32.PrintWindow
u.PrintWindow.restype = wintypes.BOOL
u.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
res = u.PrintWindow(game, hdcMem, PW_RENDERFULLCONTENT)
print('PrintWindow result=', res)

from PIL import Image
img = Image.frombuffer('RGBA', (W, H), ctypes.string_at(bits, W*H*4), 'raw', 'BGRA', 0, 1)
out = sys.argv[1] if len(sys.argv) > 1 else r'game_frame.png'
img.convert('RGB').save(out)
print('saved', out)

g.SelectObject(hdcMem, old); g.DeleteObject(hBmp); g.DeleteDC(hdcMem); u.ReleaseDC(None, hdcScreen)
