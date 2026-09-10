# -*- coding: utf-8 -*-
"""无害探针：红底 Tk 窗口挂到 Progman，验证 Win11 壁纸层回退方案。20 秒后自动退出。"""
import ctypes
import time
import tkinter as tk
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
GA_ROOT = 2
HWND_BOTTOM = 1
SWP_NOACTIVATE = 0x0010
SWP_SHOWWINDOW = 0x0040
WS_EX_LAYERED = 0x00080000
WS_EX_TRANSPARENT = 0x00000020

u.GetAncestor.restype = wintypes.HWND
u.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]

root = tk.Tk()
root.configure(bg="red")
root.overrideredirect(True)
w, h = m.screen_size()
root.geometry("%dx%d+0+0" % (w, h))
root.update()

child = wintypes.HWND(root.winfo_id())
hwnd = u.GetAncestor(child, GA_ROOT)
print("test hwnd =", hwnd, "size =", (w, h))

progman = u.FindWindowW("Progman", None)
print("progman =", progman)
prev = u.SetParent(hwnd, progman)
print("setparent prev =", prev)
# 放到 Progman 子窗口 Z 序底部 → SHELLDLL_DefView（图标）画在我们上面
u.SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW)
# 点击穿透：鼠标消息落到下层的 DefView，桌面右键/框选不受影响
ex = u.GetWindowLongW(hwnd, m.GWL_EXSTYLE)
u.SetWindowLongW(hwnd, m.GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT)
print("embedded, waiting 20s...")
root.after(20000, root.destroy)
root.mainloop()
print("done")
