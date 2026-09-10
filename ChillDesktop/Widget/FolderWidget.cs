using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ChillDesktop.Desktop;
using ChillDesktop.Native;

namespace ChillDesktop
{
    /// <summary>
    /// 游戏右侧 UI 列「笔记」旁边的伪装按钮「工具箱」：与游戏内图标等大的灰色半透明圆圈。
    /// 点击打开桌面上真实的 chillgirl 文件夹（用户自己往里面放快捷方式/应用）。
    ///
    /// 实现要点（均已实机验证）：
    /// - 顶级 WS_POPUP + WS_EX_LAYERED，用 HWND_BOTTOM 沉到普通窗口堆底：在 Progman 内的
    ///   游戏壁纸之上、办公窗口之下；
    /// - UpdateLayeredWindow 逐像素 alpha（圆形/标签之外透明）；坐标用主屏物理像素，
    ///   直接取 GetSystemMetrics——不能跨进程 GetClientRect(游戏窗口)，会被 DPI 虚拟化；
    /// - 自有窗口类（默认 HTCLIENT），WM_NCHITTEST 让圆与标签之外返回 HTTRANSPARENT，
    ///   实现透明区点击穿透、按钮区可点（Progman 子窗口收不到真实鼠标，故必须顶级）；
    /// - 悬停增亮并在圆左侧浮现「工具箱」小标签。
    /// 位置按参考分辨率 2560×1600 比例换算，跟随主屏尺寸。
    /// </summary>
    internal sealed class FolderWidget : NativeWindow, IDisposable
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_VISIBLE = 0x10000000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;

        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_MOUSEMOVE = User32.WM_MOUSEMOVE;
        private const int WM_MOUSELEAVE = User32.WM_MOUSELEAVE;
        private const int WM_NCHITTEST = User32.WM_NCHITTEST;

        private const string WindowClass = "ChillDesktopFolderWnd";
        private const string FolderName = "chillgirl";
        private const string Label = "工具箱";

        private const float RefW = 2560f, RefH = 1600f;
        private const float NoteCx = 2454f, NoteCy = 667f, NoteR = 51f;
        private const float ToolDx = -132f; // 工具箱圆心相对笔记圆心（左边）

        private static readonly Color RingColor = Color.FromArgb(235, 153, 153, 190);
        private static readonly Color GlyphColor = Color.FromArgb(240, 168, 168, 205);
        private static readonly Color DiscColor = Color.FromArgb(150, 24, 26, 48);
        private static readonly Color RingColorHot = Color.FromArgb(255, 200, 200, 230);
        private static readonly Color GlyphColorHot = Color.FromArgb(255, 214, 214, 242);
        private static readonly Color DiscColorHot = Color.FromArgb(195, 30, 32, 60);
        private static readonly Color ChipColor = Color.FromArgb(215, 22, 24, 44);
        private static readonly Color ChipTextColor = Color.FromArgb(238, 205, 205, 230);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static readonly WndProcDelegate _defProc =
            (h, msg, w, l) => User32.DefWindowProc(h, msg, w, l);

        private readonly DesktopLayer _layer;
        private bool _created;
        private bool _hover;
        private bool _tracking;

        // 物理像素几何
        private int _cx, _cy, _r;            // 圆心（屏幕）、半径
        private int _winX, _winY, _winW, _winH;
        private int _ccx, _ccy;              // 圆心相对窗口
        private Rectangle _chip;             // 标签矩形（相对窗口）

        static FolderWidget()
        {
            var wc = new User32.WNDCLASS
            {
                style = 0x0003, // CS_HREDRAW | CS_VREDRAW
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_defProc),
                hInstance = User32.GetModuleHandleW(null),
                hCursor = User32.LoadCursorW(IntPtr.Zero, (IntPtr)User32.IDC_ARROW),
                hbrBackground = IntPtr.Zero,
                lpszClassName = WindowClass,
            };
            ushort atom = User32.RegisterClassW(ref wc);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
                Log.Write("RegisterClass 失败 err=" + Marshal.GetLastWin32Error());
        }

        private static string ChillGirlPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FolderName);

        public FolderWidget(DesktopLayer layer)
        {
            _layer = layer;
        }

        public void ShowWidget()
        {
            EnsureCreated();
            SendToBottom();
            Render();
        }

        private void EnsureCreated()
        {
            if (_created && User32.IsWindow(Handle))
                return;

            _created = true;
            IntPtr hwnd = User32.CreateWindowEx(
                WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                WindowClass, "ChillDesktopToolbox",
                WS_POPUP | WS_VISIBLE,
                0, 0, 16, 16,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                Log.Write("工具箱 CreateWindowEx 失败 err=" + Marshal.GetLastWin32Error());
                return;
            }
            AssignHandle(hwnd);
            Log.Write("工具箱窗口已创建 hwnd=" + hwnd);
        }

        /// <summary>
        /// 维持 Z 序（顶→底）：办公窗口 … 工具箱 游戏 Progman。
        /// 两次沉底即可：工具箱先沉底，再把游戏沉底——后沉的在更底，
        /// 于是游戏垫底、工具箱正好贴在游戏正上方（二者都在所有办公窗口之下）。
        /// 注意 SetWindowPos(A, B) 的语义是把 A 放到 B 的【下方】，不能直接传游戏句柄。
        /// </summary>
        private void SendToBottom()
        {
            if (Handle == IntPtr.Zero) return;
            User32.SetWindowPos(Handle, User32.HWND_BOTTOM, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);

            var gh = _layer?.GameHwnd ?? IntPtr.Zero;
            if (gh != IntPtr.Zero && User32.IsWindow(gh) && User32.GetParent(gh) == IntPtr.Zero)
                User32.SetWindowPos(gh, User32.HWND_BOTTOM, 0, 0, 0, 0,
                    User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
        }

        // ---------- 几何 ----------

        private bool ComputeGeometry()
        {
            int gw = DesktopLayer.ScreenW();
            int gh = DesktopLayer.ScreenH();
            if (gw <= 0 || gh <= 0) return false;
            float sx = gw / RefW, sy = gh / RefH;
            float s = (sx + sy) / 2f;

            _cx = (int)Math.Round((NoteCx + ToolDx) * sx);
            _cy = (int)Math.Round(NoteCy * sy);
            _r = (int)Math.Round(NoteR * s);

            int pad = Math.Max(3, (int)Math.Round(3 * s));
            int gap = (int)Math.Round(10 * s);
            int chipW = (int)Math.Round(96 * s);
            int chipH = (int)Math.Round(34 * s);

            _winW = pad + _r * 2 + gap + chipW + pad; // 圆在右、标签在左
            _winH = pad * 2 + _r * 2;
            _winX = _cx - (_winW - pad - _r);
            _winY = _cy - _r - pad;
            _ccx = _winW - pad - _r;
            _ccy = _r + pad;
            _chip = new Rectangle(pad, _ccy - chipH / 2, chipW, chipH);
            return true;
        }

        // ---------- 输入 ----------

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCHITTEST:
                {
                    Point p = ScreenPoint(m.LParam);
                    m.Result = HitTest(p) ? (IntPtr)User32.HTCLIENT : (IntPtr)User32.HTTRANSPARENT;
                    return;
                }
                case WM_MOUSEMOVE:
                    if (!_hover)
                    {
                        _hover = true;
                        TrackLeave();
                        Render();
                    }
                    m.Result = IntPtr.Zero;
                    return;
                case WM_MOUSELEAVE:
                    _hover = false; _tracking = false;
                    Render();
                    m.Result = IntPtr.Zero;
                    return;
                case WM_LBUTTONDOWN:
                    OpenChillGirl();
                    m.Result = IntPtr.Zero;
                    return;
            }
            base.WndProc(ref m);
        }

        private bool HitTest(Point screen)
        {
            int dx = screen.X - _cx, dy = screen.Y - _cy;
            if (dx * dx + dy * dy <= (_r - 1) * (_r - 1))
                return true;
            if (_hover)
            {
                var local = new Point(screen.X - _winX, screen.Y - _winY);
                if (_chip.Contains(local)) return true;
            }
            return false;
        }

        private static Point ScreenPoint(IntPtr lParam)
        {
            int x = unchecked((short)((long)lParam & 0xffff));
            int y = unchecked((short)(((long)lParam >> 16) & 0xffff));
            return new Point(x, y); // WM_NCHITTEST 的 lParam 即屏幕物理坐标
        }

        private void TrackLeave()
        {
            if (_tracking) return;
            var t = new User32.TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<User32.TRACKMOUSEEVENT>(),
                dwFlags = User32.TME_LEAVE,
                hwndTrack = Handle,
            };
            User32.TrackMouseEvent(ref t);
            _tracking = true;
        }

        private void OpenChillGirl()
        {
            try
            {
                string dir = ChillGirlPath;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"")
                {
                    UseShellExecute = true,
                });
                Log.Write("打开 chillgirl 文件夹");
            }
            catch (Exception ex) { Log.Error("OpenChillGirl", ex); }
        }

        // ---------- 自愈 ----------

        public void Heartbeat()
        {
            if (!_created) return;
            if (!User32.IsWindow(Handle))
            {
                ReleaseHandle();
                _created = false;
                ShowWidget();
                return;
            }
            if (!User32.IsWindowVisible(Handle))
                User32.ShowWindow(Handle, User32.SW_SHOW);
            SendToBottom();
            if (ComputeGeometry())
            {
                User32.GetWindowRect(Handle, out var rc);
                if (rc.Left != _winX || rc.Top != _winY || rc.Width != _winW || rc.Height != _winH)
                    Render();
            }
        }

        // ---------- 绘制 ----------

        private void Render()
        {
            if (Handle == IntPtr.Zero || !ComputeGeometry()) return;
            bool hover = _hover;
            int ccx = _ccx, ccy = _ccy, r = _r, winW = _winW, winH = _winH;
            Rectangle chip = _chip;
            LayeredRenderer.Present(Handle, _winX, _winY, winW, winH, g =>
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var disc = hover ? DiscColorHot : DiscColor;
                var ring = hover ? RingColorHot : RingColor;
                var glyph = hover ? GlyphColorHot : GlyphColor;

                using (var b = new SolidBrush(disc))
                    g.FillEllipse(b, ccx - r, ccy - r, r * 2, r * 2);

                float penW = Math.Max(2.4f, r * 0.105f);
                using (var p = new Pen(ring, penW))
                {
                    float d = r - penW / 2f;
                    g.DrawEllipse(p, ccx - d, ccy - d, d * 2, d * 2);
                }

                DrawToolboxGlyph(g, ccx, ccy, r, glyph, penW);

                if (hover) DrawChip(g, chip, r);
            });
        }

        private void DrawChip(Graphics g, Rectangle chip, int r)
        {
            float rad = chip.Height * 0.32f;
            using var path = new GraphicsPath();
            AddRoundedRect(path, new RectangleF(chip.X, chip.Y, chip.Width, chip.Height), rad);
            using (var b = new SolidBrush(ChipColor))
                g.FillPath(b, path);

            float s = r / NoteR;
            using var f = new Font("Microsoft YaHei UI", 14f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            using var tb = new SolidBrush(ChipTextColor);
            g.DrawString(Label, f, tb, new RectangleF(chip.X, chip.Y - 1, chip.Width, chip.Height), sf);
        }

        /// <summary>工具箱线稿：提手 + 圆角箱体 + 中缝，与游戏图标同款描边语言。</summary>
        private static void DrawToolboxGlyph(Graphics g, float c, float cy, int r, Color col, float ringW)
        {
            float w = r * 1.02f;
            float left = c - w / 2f, right = c + w / 2f;
            float top = cy - w * 0.20f, bottom = cy + w * 0.40f;
            float penW = Math.Max(2.6f, ringW * 1.05f);

            using var p = new Pen(col, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            float hw = w * 0.30f, hh = w * 0.24f;
            float hx0 = c - hw / 2f, hx1 = c + hw / 2f, hy = top - hh;
            g.DrawLine(p, hx0, top, hx0, hy + penW * 0.4f);
            g.DrawLine(p, hx1, top, hx1, hy + penW * 0.4f);
            g.DrawLine(p, hx0, hy, hx1, hy);

            using var path = new GraphicsPath();
            float d = penW / 2f;
            AddRoundedRect(path, new RectangleF(left + d, top + d, w - penW, (bottom - top) - penW), r * 0.16f);
            g.DrawPath(p, path);
            g.DrawLine(p, left + d * 1.5f, cy + w * 0.04f, right - d * 1.5f, cy + w * 0.04f);
        }

        private static void AddRoundedRect(GraphicsPath path, RectangleF b, float radius)
        {
            float d = radius * 2;
            path.StartFigure();
            path.AddArc(b.X, b.Y, d, d, 180, 90);
            path.AddArc(b.Right - d, b.Y, d, d, 270, 90);
            path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
            path.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
        }

        public void Dispose()
        {
            try { if (_created && User32.IsWindow(Handle)) User32.DestroyWindow(Handle); } catch { }
            DestroyHandle();
            GC.SuppressFinalize(this);
        }
    }
}
