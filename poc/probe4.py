# -*- coding: utf-8 -*-
"""
探针4：Win11 24H2 Lively 同款 layered 算法（纯 Win32 红窗，不用 Tk）。
成功标准：桌面图标显示在红底之上，普通办公窗口在更上面。18 秒自动退出。
"""
import ctypes
from ctypes import wintypes
import embed_wallpaper as m

u = m.user32
gdi32 = ctypes.windll.gdi32

WS_POPUP = 0x80000000
WS_VISIBLE = 0x10000000
WS_CHILD = 0x40000000
WS_EX_LAYERED = 0x00080000
WS_EX_NOREDIRECTIONBITMAP = 0x00200000
SWP_SHOWWINDOW = 0x0040
SWP_NOACTIVATE = 0x0010
SWP_NOMOVE = 0x0002
SWP_NOSIZE = 0x0001
SW_SHOW = 5
WM_DESTROY = 0x0002
WM_PAINT = 0x000F
WM_ERASEBKGND = 0x0014
LWA_ALPHA = 0x0002
CS_HREDRAW = 0x0002
CS_VREDRAW = 0x0001

u.CreateWindowExW.restype = wintypes.HWND
u.CreateWindowExW.argtypes = [wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD,
                              ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                              wintypes.HWND, wintypes.HMENU, wintypes.HINSTANCE, wintypes.LPVOID]
u.DefWindowProcW.restype = ctypes.c_long
u.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
u.RegisterClassW.restype = wintypes.ATOM
u.RegisterClassW.argtypes = [ctypes.c_void_p]
u.BeginPaint.restype = wintypes.HDC
u.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u.FillRect.argtypes = [wintypes.HDC, ctypes.POINTER(wintypes.RECT), wintypes.HBRUSH]
u.EndPaint.argtypes = [wintypes.HWND, ctypes.c_void_p]
u.InvalidateRect.argtypes = [wintypes.HWND, ctypes.c_void_p, wintypes.BOOL]
u.DestroyWindow.argtypes = [wintypes.HWND]
u.SetLayeredWindowAttributes.argtypes = [wintypes.HWND, wintypes.COLORREF, wintypes.BYTE, wintypes.DWORD]
kernel32 = ctypes.windll.kernel32
kernel32.GetTickCount64.restype = ctypes.c_uint64
kernel32.GetModuleHandleW.restype = wintypes.HMODULE
kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]

WNDPROC = ctypes.WINFUNCTYPE(ctypes.c_long, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)


class WNDCLASS(ctypes.Structure):
    _fields_ = [("style", wintypes.UINT), ("lpfnWndProc", WNDPROC), ("cbClsExtra", ctypes.c_int),
                ("cbWndExtra", ctypes.c_int), ("hInstance", wintypes.HINSTANCE), ("hIcon", wintypes.HICON),
                ("hCursor", wintypes.HANDLE), ("hbrBackground", wintypes.HBRUSH),
                ("lpszMenuName", wintypes.LPCWSTR), ("lpszClassName", wintypes.LPCWSTR)]


def make_red_window(w, h):
    hinst = kernel32.GetModuleHandleW(None)

    @WNDPROC
    def wndproc(hwnd, msg, wp, lp):
        if msg == WM_ERASEBKGND:
            # 背景由 WM_PAINT 整块填红，避免闪烁
            return 1
        if msg == WM_PAINT:
            ps = ctypes.create_string_buffer(128)  # PAINTSTRUCT（x64 约 72 字节）
            hdc = u.BeginPaint(hwnd, ctypes.byref(ps))
            brush = gdi32.CreateSolidBrush(0x000000FF)  # BGR 纯红
            rect = wintypes.RECT()
            u.GetClientRect(hwnd, ctypes.byref(rect))
            u.FillRect(hdc, ctypes.byref(rect), brush)
            gdi32.DeleteObject(brush)
            u.EndPaint(hwnd, ctypes.byref(ps))
            return 0
        if msg == WM_DESTROY:
            u.PostQuitMessage(0)
            return 0
        return u.DefWindowProcW(hwnd, msg, wp, lp)

    wc = WNDCLASS()
    wc.style = CS_HREDRAW | CS_VREDRAW
    wc.lpfnWndProc = wndproc
    wc.hInstance = hinst
    wc.hbrBackground = gdi32.CreateSolidBrush(0x000000FF)
    wc.lpszClassName = "ChillProbeRed"
    atom = u.RegisterClassW(ctypes.byref(wc))
    hwnd = u.CreateWindowExW(
        0, wc.lpszClassName, "ChillProbeRed",
        WS_POPUP | WS_VISIBLE,
        0, 0, w, h, None, None, hinst, None)
    if not hwnd:
        raise ctypes.WinError()
    return hwnd, wndproc  # 保活 wndproc 回调


def attach_24h2(hwnd):
    progman = u.FindWindowW("Progman", "Program Manager")
    ex = u.GetWindowLongW(progman, m.GWL_EXSTYLE)
    is24h2 = bool(ex & WS_EX_NOREDIRECTIONBITMAP)
    print("progman=%s exstyle=0x%08X 24H2_layered=%s" % (progman, ex & 0xFFFFFFFF, is24h2))

    res = wintypes.DWORD()
    u.SendMessageTimeoutW(progman, 0x052C, 0, 1, 0, 1000, ctypes.byref(res))
    u.SendMessageTimeoutW(progman, 0x052C, 0, 0, 0, 1000, ctypes.byref(res))

    shell = u.FindWindowExW(progman, None, "SHELLDLL_DefView", None)
    workerw = u.FindWindowExW(progman, None, "WorkerW", None)
    print("shellView=%s childWorkerW=%s" % (shell, workerw))

    style = u.GetWindowLongW(hwnd, m.GWL_STYLE)
    u.SetWindowLongW(hwnd, m.GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD)
    exs = u.GetWindowLongW(hwnd, m.GWL_EXSTYLE)
    u.SetWindowLongW(hwnd, m.GWL_EXSTYLE, exs | WS_EX_LAYERED)
    u.SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)
    print("setparent ->", u.SetParent(hwnd, progman))

    w, h = m.screen_size()
    # insertAfter = shell：恰好排在图标层正下方
    ok = u.SetWindowPos(hwnd, shell, 0, 0, w, h, SWP_SHOWWINDOW | SWP_NOACTIVATE)
    print("SetWindowPos(below shell)=%s size=%dx%d" % (ok, w, h))
    if workerw:
        u.SetWindowPos(workerw, hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
    u.ShowWindow(hwnd, SW_SHOW)
    u.InvalidateRect(hwnd, None, True)
    u.UpdateWindow(hwnd)

    print("after: visible=%s parent=%s style=0x%X ex=0x%X" % (
        bool(u.IsWindowVisible(hwnd)), u.GetParent(hwnd),
        u.GetWindowLongW(hwnd, m.GWL_STYLE) & 0xFFFFFFFF,
        u.GetWindowLongW(hwnd, m.GWL_EXSTYLE) & 0xFFFFFFFF))

    def _child(c, _):
        r = wintypes.RECT()
        u.GetWindowRect(c, ctypes.byref(r))
        print("  progman-child %s class=%-20s vis=%d rect=(%d,%d %dx%d)" % (
            c, m.window_class(c), u.IsWindowVisible(c),
            r.left, r.top, r.right - r.left, r.bottom - r.top))
        return True
    u.EnumChildWindows.argtypes = [wintypes.HWND, m.WNDENUMPROC, wintypes.LPARAM]
    u.EnumChildWindows(progman, m.WNDENUMPROC(_child), 0)


m.enable_dpi_awareness()
w, h = m.screen_size()
hwnd, keep_cb = make_red_window(w, h)
print("red hwnd =", hwnd)
attach_24h2(hwnd)
print("embedded, message loop 18s")

# 消息循环 18 秒（保证 WM_PAINT 被处理）
end = kernel32.GetTickCount64() + 45000
msg = wintypes.MSG()
while kernel32.GetTickCount64() < end:
    if u.PeekMessageW(ctypes.byref(msg), None, 0, 0, 1):  # PM_REMOVE
        u.TranslateMessage(ctypes.byref(msg))
        u.DispatchMessageW(ctypes.byref(msg))
    else:
        ctypes.windll.kernel32.Sleep(20)
u.DestroyWindow(hwnd)
print("done")
