using System;
using System.Drawing;
using System.Runtime.InteropServices;
using ChillDesktop.Native;

namespace ChillDesktop
{
    /// <summary>
    /// 把一段 GDI+ 绘制通过 UpdateLayeredWindow 提交给 WS_EX_LAYERED 窗口。
    /// 必须用 top-down 32bpp DIB 且把像素预乘 alpha，否则画面上下颠倒或发虚/不显示。
    /// </summary>
    internal static class LayeredRenderer
    {
        public static void Present(IntPtr hwnd, int x, int y, int w, int h, Action<Graphics> paint)
        {
            IntPtr hdcScreen = User32.GetDC(IntPtr.Zero);
            IntPtr hdcMem = Gdi32.CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            IntPtr bits = IntPtr.Zero;

            try
            {
                var bmi = new Gdi32.BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h; // top-down
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = Gdi32.BI_RGB;

                hBitmap = Gdi32.CreateDIBSection(hdcScreen, ref bmi, Gdi32.DIB_RGB_COLORS,
                    out bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    Log.Write("CreateDIBSection 失败 err=" + Marshal.GetLastWin32Error());
                    return;
                }

                oldBitmap = Gdi32.SelectObject(hdcMem, hBitmap);

                using (var g = Graphics.FromHdc(hdcMem))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; // 透明 DIB 上 ClearType 会留彩边，用灰度抗锯齿
                    paint(g);
                }

                Premultiply(bits, w * h);

                var dst = new User32.POINT { X = x, Y = y };
                var size = new User32.SIZE(w, h);
                var src = new User32.POINT { X = 0, Y = 0 };
                var blend = new User32.BLENDFUNCTION
                {
                    BlendOp = User32.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = User32.AC_SRC_ALPHA,
                };

                bool ok = User32.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size,
                    hdcMem, ref src, 0, ref blend, User32.ULW_ALPHA);
                if (!ok)
                    Log.Write("UpdateLayeredWindow 失败 err=" + Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                Log.Error("LayeredRenderer.Present", ex);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) Gdi32.SelectObject(hdcMem, oldBitmap);
                if (hBitmap != IntPtr.Zero) Gdi32.DeleteObject(hBitmap);
                if (hdcMem != IntPtr.Zero) Gdi32.DeleteDC(hdcMem);
                User32.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        private static void Premultiply(IntPtr bits, int count)
        {
            // 内存布局为 BGRA。GDI+ 输出 straight alpha，ULW_ALPHA 要求预乘。
            int[] px = new int[count];
            Marshal.Copy(bits, px, 0, count);
            for (int i = 0; i < count; i++)
            {
                int v = px[i];
                int a = (v >> 24) & 0xff;
                if (a == 0) { px[i] = 0; continue; }
                if (a == 255) continue;
                int b = (v & 0xff) * a / 255;
                int g = ((v >> 8) & 0xff) * a / 255;
                int r = ((v >> 16) & 0xff) * a / 255;
                px[i] = (a << 24) | (r << 16) | (g << 8) | b;
            }
            Marshal.Copy(px, 0, bits, count);
        }
    }
}
