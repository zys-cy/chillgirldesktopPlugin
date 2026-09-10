# -*- coding: utf-8 -*-
"""探针3：SetParent 前把 WS_POPUP 改成 WS_CHILD（文档要求），并打印状态。18 秒退出。"""
import ctypes
import tkinter as tk
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
GA_ROOT = 2
WS_POPUP = 0x80000000
WS_CHILD = 0x40000000
WS_VISIBLE = 0x10000000
HWND_BOTTOM = 1

u.GetAncestor.restype = wintypes.HWND
u.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]


def state(tag, hwnd):
    style = u.GetWindowLongW(hwnd, m.GWL_STYLE)
    r = wintypes.RECT()
    u.GetWindowRect(hwnd, ctypes.byref(r))
    print("%s style=0x%08X visible=%s rect=(%d,%d,%d,%d) parent=%s" % (
        tag, style & 0xFFFFFFFF, bool(u.IsWindowVisible(hwnd)),
        r.left, r.top, r.right - r.left, r.bottom - r.top, u.GetParent(hwnd)))


root = tk.Tk()
root.configure(bg="red")
root.overrideredirect(True)
w, h = m.screen_size()
root.geometry("%dx%d+0+0" % (w, h))
root.update()

hwnd = u.GetAncestor(wintypes.HWND(root.winfo_id()), GA_ROOT)
progman = u.FindWindowW("Progman", None)
state("before", hwnd)

style = u.GetWindowLongW(hwnd, m.GWL_STYLE)
u.SetWindowLongW(hwnd, m.GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD)
print("setparent ->", u.SetParent(hwnd, progman))
u.MoveWindow(hwnd, 0, 0, w, h, True)
u.ShowWindow(hwnd, 5)  # SW_SHOW
state("after ", hwnd)

root.after(18000, root.destroy)
root.mainloop()
print("done")
