# -*- coding: utf-8 -*-
"""探针2：挂 Progman 但不置底，看窗口是否可见、图标层级如何。18 秒自动退出。"""
import ctypes
import tkinter as tk
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
GA_ROOT = 2
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

hwnd = u.GetAncestor(wintypes.HWND(root.winfo_id()), GA_ROOT)
progman = u.FindWindowW("Progman", None)
print("hwnd", hwnd, "progman", progman, "size", (w, h))
print("prev parent", u.SetParent(hwnd, progman))
u.MoveWindow(hwnd, 0, 0, w, h, True)
# 不置底，保持最新子窗口（Z 序顶部）
root.after(18000, root.destroy)
root.mainloop()
print("done")
