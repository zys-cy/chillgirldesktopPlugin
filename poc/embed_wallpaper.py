# -*- coding: utf-8 -*-
"""
Chill With You —— 动态桌面 PoC（支持 Win11 24H2/25H2 新桌面架构）

原理：
  Win11 24H2 起 Progman 带 WS_EX_NOREDIRECTIONBITMAP（为 HDR 壁纸重构），
  壁纸 WorkerW 变成 Progman 的子窗口。做法（Lively Wallpaper 同款）：
    1. 给 Progman 连发两次 0x052C（lParam=1, 0），生成壁纸层
    2. 在 Progman 下找 SHELLDLL_DefView（图标层）和 WorkerW（壁纸绘制层）
    3. 游戏窗口去 WS_POPUP、加 WS_CHILD（layered 变体再加 WS_EX_LAYERED + alpha=255）
    4. SetParent 进 Progman，Z 序精确插在 DefView 之下、WorkerW 之上
  D3D11 flip-model（Unity）直接向 DWM 合成，不依赖 Progman 消失的重定向表面。

用法：
  1. 启动游戏，设置里画面模式用「无边框全屏」（窗口模式也行，会自动去标题栏）
  2. python embed_wallpaper.py                 # 等待窗口出现并嵌入（默认 layered 变体）
     python embed_wallpaper.py --variant plain # 若 layered 下游戏黑屏，换这个
     python embed_wallpaper.py --launch        # 脚本直接启动游戏
     python embed_wallpaper.py --restore       # 异常后的应急还原
  3. Ctrl+C 退出并自动还原游戏窗口
"""

import sys
import time
import ctypes
from ctypes import wintypes

# ---------- 常量 ----------
GWL_STYLE = -16
GWL_EXSTYLE = -20
WS_POPUP = 0x80000000
WS_CHILD = 0x40000000
WS_VISIBLE = 0x10000000
WS_CAPTION = 0x00C00000
WS_THICKFRAME = 0x00040000
WS_MINIMIZEBOX = 0x00020000
WS_MAXIMIZEBOX = 0x00010000
WS_EX_LAYERED = 0x00080000
WS_EX_NOREDIRECTIONBITMAP = 0x00200000
SWP_SHOWWINDOW = 0x0040
SWP_NOACTIVATE = 0x0010
SWP_NOMOVE = 0x0002
SWP_NOSIZE = 0x0001
SW_SHOW = 5
SW_RESTORE = 9
LWA_ALPHA = 0x0002
SMTO_NORMAL = 0x0000
WM_SPAWN_WORKER = 0x052C

GAME_TITLE = "Chill With You"
DEFAULT_EXE = r"C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story\Chill With You.exe"

# ---------- Win32 ----------
user32 = ctypes.windll.user32
WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

user32.FindWindowW.restype = wintypes.HWND
user32.FindWindowW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR]
user32.FindWindowExW.restype = wintypes.HWND
user32.FindWindowExW.argtypes = [wintypes.HWND, wintypes.HWND, wintypes.LPCWSTR, wintypes.LPCWSTR]
user32.EnumWindows.argtypes = [WNDENUMPROC, wintypes.LPARAM]
user32.GetWindowLongW.restype = ctypes.c_long
user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
user32.SetWindowLongW.restype = ctypes.c_long
user32.SetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_long]
user32.SetParent.restype = wintypes.HWND
user32.SetParent.argtypes = [wintypes.HWND, wintypes.HWND]
user32.GetParent.restype = wintypes.HWND
user32.GetParent.argtypes = [wintypes.HWND]
user32.MoveWindow.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_int,
                              ctypes.c_int, ctypes.c_int, wintypes.BOOL]
user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int,
                                ctypes.c_int, ctypes.c_int, wintypes.UINT]
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.IsWindow.argtypes = [wintypes.HWND]
user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
user32.SendMessageTimeoutW.restype = ctypes.c_long
user32.SendMessageTimeoutW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM,
                                       wintypes.UINT, wintypes.UINT, ctypes.POINTER(wintypes.DWORD)]
user32.SetLayeredWindowAttributes.argtypes = [wintypes.HWND, wintypes.COLORREF, wintypes.BYTE, wintypes.DWORD]


def log(msg):
    print("[ChillDesktop] " + msg, flush=True)


def enable_dpi_awareness():
    try:
        if user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4)):
            log("DPI 感知: PerMonitorV2")
            return
    except (AttributeError, OSError):
        pass
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
        log("DPI 感知: PerMonitor")
        return
    except (AttributeError, OSError):
        pass
    user32.SetProcessDPIAware()
    log("DPI 感知: System")


def window_title(hwnd):
    n = user32.GetWindowTextLengthW(hwnd)
    buf = ctypes.create_unicode_buffer(n + 1)
    user32.GetWindowTextW(hwnd, buf, n + 1)
    return buf.value


def window_class(hwnd):
    buf = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, buf, 256)
    return buf.value


def find_game_window():
    # 精确标题优先
    hwnd = user32.FindWindowW(None, GAME_TITLE)
    if hwnd and window_class(hwnd) == "UnityWndClass":
        return hwnd
    # 兜底：必须是 Unity 窗口类——绝不能匹配到终端宿主（CASCADIA/ConsoleWindowClass）
    found = []

    def _cb(h, _l):
        if (user32.IsWindowVisible(h) and window_class(h) == "UnityWndClass"
                and "chill" in window_title(h).lower()):
            found.append(h)
        return True

    user32.EnumWindows(WNDENUMPROC(_cb), 0)
    return found[0] if len(found) == 1 else None


def wait_for_game_window(timeout_sec=180):
    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        hwnd = find_game_window()
        if hwnd:
            log("找到游戏窗口: HWND=%s title=%r" % (hwnd, window_title(hwnd)))
            return hwnd
        time.sleep(1)
    return None


def screen_size():
    return user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)


# ---------- 嵌入 ----------

class EmbedState:
    def __init__(self, hwnd):
        self.hwnd = hwnd
        self.old_style = None
        self.old_exstyle = None
        self.old_rect = None
        self.progman = None
        self.shell = None
        self.workerw = None


def _spawn_desktop_layers():
    """发两次 0x052C，返回 (progman, shellView, workerw, is24h2)。"""
    progman = user32.FindWindowW("Progman", "Program Manager")
    if not progman:
        raise RuntimeError("找不到 Progman")

    progman_ex = user32.GetWindowLongW(progman, GWL_EXSTYLE)
    is24h2 = bool(progman_ex & WS_EX_NOREDIRECTIONBITMAP)

    res = wintypes.DWORD()
    user32.SendMessageTimeoutW(progman, WM_SPAWN_WORKER, 0, 1, SMTO_NORMAL, 1000, ctypes.byref(res))
    user32.SendMessageTimeoutW(progman, WM_SPAWN_WORKER, 0, 0, SMTO_NORMAL, 1000, ctypes.byref(res))

    shell = user32.FindWindowExW(progman, None, "SHELLDLL_DefView", None)
    workerw = user32.FindWindowExW(progman, None, "WorkerW", None)

    # 老系统：DefView/WorkerW 是顶级窗口，枚举找兄弟 WorkerW
    if not workerw:
        classic = []

        def _cb(top, _l):
            if user32.FindWindowExW(top, None, "SHELLDLL_DefView", None):
                classic.append(user32.FindWindowExW(None, top, "WorkerW", None))
                return False
            return True

        user32.EnumWindows(WNDENUMPROC(_cb), 0)
        if classic and classic[0]:
            workerw = classic[0]
            progman = None  # 经典路径直接挂 WorkerW，不挂 Progman
    return progman, shell, workerw, is24h2


def attach(st, variant):
    hwnd = st.hwnd
    rect = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    if st.old_rect is None:
        st.old_rect = (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top)
        st.old_style = user32.GetWindowLongW(hwnd, GWL_STYLE)
        st.old_exstyle = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)

    st.progman, st.shell, st.workerw, is24h2 = _spawn_desktop_layers()
    log("桌面架构: %s | Progman=%s shell=%s workerw=%s" % (
        "24H2 layered" if is24h2 else "classic", st.progman, st.shell, st.workerw))

    w, h = screen_size()

    if is24h2 and st.progman:
        # 窗口模式带标题栏时先去掉；WS_POPUP 必须清，否则 POPUP|CHILD 共存时 SetParent 无效
        style = user32.GetWindowLongW(hwnd, GWL_STYLE)
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)
        style |= WS_CHILD | WS_VISIBLE
        user32.SetWindowLongW(hwnd, GWL_STYLE, style)

        ex = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        if variant == "layered":
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED)
            user32.SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)
        else:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex & ~WS_EX_LAYERED)

        prev = user32.SetParent(hwnd, st.progman)
        if user32.GetParent(hwnd) != st.progman:
            raise RuntimeError("SetParent 失败（GetParent 校验不一致，prev=%s）" % prev)

        # 插在图标层正下方
        insert_after = st.shell if st.shell else None
        user32.SetWindowPos(hwnd, insert_after, 0, 0, w, h,
                            SWP_SHOWWINDOW | SWP_NOACTIVATE)
        # 壁纸绘制层压到我们下面
        if st.workerw:
            user32.SetWindowPos(st.workerw, hwnd, 0, 0, 0, 0,
                                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
        user32.ShowWindow(hwnd, SW_SHOW)
    else:
        # 经典路径：直接挂顶级 WorkerW
        if not st.workerw:
            raise RuntimeError("经典桌面层也没找到 WorkerW")
        style = user32.GetWindowLongW(hwnd, GWL_STYLE)
        user32.SetWindowLongW(hwnd, GWL_STYLE,
                              (style & ~(WS_CAPTION | WS_THICKFRAME)) | WS_VISIBLE)
        user32.SetParent(hwnd, st.workerw)
        user32.MoveWindow(hwnd, 0, 0, w, h, True)

    log("嵌入完成，尺寸 %dx%d，变体=%s" % (w, h, variant))


def reposition(st):
    """心跳：掉出壁纸层则重嵌，尺寸被改则拉回。不抢焦点。"""
    hwnd = st.hwnd
    if not user32.IsWindow(hwnd):
        return False
    parent = st.progman or st.workerw
    if user32.GetParent(hwnd) != parent:
        log("窗口脱离壁纸层，重新嵌入……")
        attach(st, "layered" if (user32.GetWindowLongW(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED)
               else "plain")
        return True
    r = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = screen_size()
    if (r.right - r.left, r.bottom - r.top) != (w, h):
        insert_after = st.shell if st.shell else None
        user32.SetWindowPos(hwnd, insert_after, 0, 0, w, h, SWP_NOACTIVATE)
    return True


def restore(hwnd, st=None):
    log("还原游戏窗口……")
    try:
        user32.SetParent(hwnd, None)
        if st and st.old_style is not None:
            user32.SetWindowLongW(hwnd, GWL_STYLE, st.old_style)
        else:
            style = user32.GetWindowLongW(hwnd, GWL_STYLE)
            user32.SetWindowLongW(hwnd, GWL_STYLE,
                                  (style & ~WS_CHILD) | WS_POPUP | WS_VISIBLE)
        if st and st.old_exstyle is not None:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, st.old_exstyle)
        if st and st.old_rect:
            x, y, w, h = st.old_rect
            user32.MoveWindow(hwnd, x, y, w, h, True)
        user32.ShowWindow(hwnd, SW_RESTORE)
        log("已还原")
    except OSError as e:
        log("还原出错（游戏可能已退出）: %s" % e)


def emergency_restore():
    """--restore：不知道上次状态时的尽力还原。"""
    hwnd = find_game_window()
    if not hwnd:
        log("没找到游戏窗口。")
        return
    restore(hwnd)


def main():
    enable_dpi_awareness()
    variant = "plain" if "--variant=plain" in sys.argv or "--variant plain" in sys.argv else "layered"

    if "--restore" in sys.argv:
        emergency_restore()
        return 0

    if "--launch" in sys.argv:
        import subprocess
        import os
        if not os.path.exists(DEFAULT_EXE):
            log("默认路径找不到游戏：%s" % DEFAULT_EXE)
            return 1
        log("启动游戏……")
        subprocess.Popen([DEFAULT_EXE], cwd=os.path.dirname(DEFAULT_EXE))

    hwnd = wait_for_game_window()
    if not hwnd:
        log("超时没等到游戏窗口。")
        return 1

    # 游戏启动后窗口可能重建几次（Unity 初始化），等它稳定
    log("等待游戏窗口稳定 8 秒……")
    time.sleep(8)
    hwnd = find_game_window() or hwnd

    st = EmbedState(hwnd)
    attach(st, variant)
    log("")
    log("★ 按 Win+D 应能看到她在壁纸层；办公窗口正常盖在上面。")
    log("★ 保持本窗口/脚本运行，Ctrl+C 退出自动还原。黑屏就 Ctrl+C 后换 --variant plain。")
    log("")

    try:
        while True:
            time.sleep(5)
            if not reposition(st):
                log("游戏窗口消失，退出。")
                return 0
    except KeyboardInterrupt:
        if user32.IsWindow(hwnd):
            restore(hwnd, st)
        log("再见。")
        return 0


if __name__ == "__main__":
    sys.exit(main())
