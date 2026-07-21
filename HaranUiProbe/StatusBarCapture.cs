using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace HaranUiProbe;

/// <summary>截取目标窗口底栏并与模板比对。</summary>
public static class StatusBarCapture
{
    public enum MatchKind
    {
        Unknown,
        IdleNoRepairData,
        WaitingForInput
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const int PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static string TemplateDir
    {
        get
        {
            var d = Path.Combine(AppContext.BaseDirectory, "templates");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string IdleTemplatePath => Path.Combine(TemplateDir, "status_idle.png");
    public static string WaitingTemplatePath => Path.Combine(TemplateDir, "status_waiting.png");

    public static IReadOnlyList<(IntPtr Hwnd, string Title, uint Pid)> FindWindows(string[] titleFilters)
    {
        var self = (uint)Environment.ProcessId;
        var list = new List<(IntPtr, string, uint)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == self) return true;
            var len = GetWindowTextLength(h);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (title.IndexOf("HARAN UI 控件探测", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (title.IndexOf("记事本", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (title.IndexOf("Notepad", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (titleFilters.Length == 0 ||
                titleFilters.Any(f => title.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                list.Add((h, title, pid));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// 截取窗口底部条带。barRatio=底栏高度占窗口高度比例；也可 barPixels 固定像素。
    /// </summary>
    public static Bitmap? CaptureBottomBar(IntPtr hwnd, int barPixels = 36, double barRatio = 0)
    {
        if (!GetWindowRect(hwnd, out var rc)) return null;
        var w = rc.Right - rc.Left;
        var h = rc.Bottom - rc.Top;
        if (w < 50 || h < 50) return null;

        var barH = barPixels;
        if (barRatio > 0)
            barH = Math.Max(20, (int)(h * barRatio));
        barH = Math.Min(barH, h / 2);

        using var full = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            try
            {
                // 优先 PrintWindow（可抓部分被挡窗口）
                if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                    PrintWindow(hwnd, hdc, 0);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        // 若 PrintWindow 全黑，回退 CopyFromScreen
        if (IsMostlyBlack(full))
        {
            using var g2 = Graphics.FromImage(full);
            try
            {
                g2.CopyFromScreen(rc.Left, rc.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }
            catch
            {
                return null;
            }
        }

        var rect = new Rectangle(0, h - barH, w, barH);
        var bar = full.Clone(rect, PixelFormat.Format24bppRgb);
        return bar;
    }

    public static void SaveTemplate(Bitmap bar, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bar.Save(path, ImageFormat.Png);
    }

    public static Bitmap? LoadTemplate(string path)
    {
        if (!File.Exists(path)) return null;
        // 打开副本，避免文件锁
        using var fs = File.OpenRead(path);
        using var tmp = new Bitmap(fs);
        return new Bitmap(tmp);
    }

    /// <summary>返回 0~1，1=完全相同。</summary>
    public static double Similarity(Bitmap a, Bitmap b)
    {
        if (a == null || b == null) return 0;
        using var aa = Resize(a, 320, 24);
        using var bb = Resize(b, 320, 24);
        long sum = 0;
        long n = 0;
        for (var y = 0; y < aa.Height; y++)
        for (var x = 0; x < aa.Width; x++)
        {
            var ca = aa.GetPixel(x, y);
            var cb = bb.GetPixel(x, y);
            var ga = (ca.R + ca.G + ca.B) / 3;
            var gb = (cb.R + cb.G + cb.B) / 3;
            sum += Math.Abs(ga - gb);
            n++;
        }
        if (n == 0) return 0;
        var mad = sum / (double)n; // 0..255
        return Math.Max(0, 1.0 - mad / 255.0);
    }

    public static (MatchKind kind, double idleScore, double waitScore) Match(
        Bitmap current,
        Bitmap? idleTpl,
        Bitmap? waitTpl,
        double minScore = 0.88)
    {
        var idle = idleTpl == null ? 0 : Similarity(current, idleTpl);
        var wait = waitTpl == null ? 0 : Similarity(current, waitTpl);
        if (idle < minScore && wait < minScore)
            return (MatchKind.Unknown, idle, wait);
        if (wait >= idle && wait >= minScore)
            return (MatchKind.WaitingForInput, idle, wait);
        if (idle >= minScore)
            return (MatchKind.IdleNoRepairData, idle, wait);
        return (MatchKind.Unknown, idle, wait);
    }

    private static Bitmap Resize(Bitmap src, int w, int h)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        g.DrawImage(src, 0, 0, w, h);
        return dst;
    }

    private static bool IsMostlyBlack(Bitmap bmp)
    {
        long bright = 0;
        var step = Math.Max(1, bmp.Width / 40);
        var samples = 0;
        for (var y = 0; y < bmp.Height; y += step)
        for (var x = 0; x < bmp.Width; x += step)
        {
            var c = bmp.GetPixel(x, y);
            bright += c.R + c.G + c.B;
            samples++;
        }
        return samples > 0 && bright / samples < 30;
    }
}
