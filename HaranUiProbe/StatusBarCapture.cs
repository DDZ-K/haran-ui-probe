using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace HaranUiProbe;

/// <summary>窗口查找、区域截图、多模板匹配。</summary>
public static class StatusBarCapture
{
    public enum MatchKind
    {
        Unknown,
        Idle,
        Waiting
    }

    public sealed class RoiConfig
    {
        /// <summary>相对窗口客户区：左边距</summary>
        public int Left { get; set; }
        /// <summary>相对窗口：上边距；若 FromBottom=true 则忽略，改用 BottomOffset</summary>
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; } = 40;
        /// <summary>true：从窗口底边向上 Height 像素，Top 无效；Left/Width 仍生效（Width=0 表示到右边缘）</summary>
        public bool FromBottom { get; set; } = true;
        public int BottomOffset { get; set; } // 距底边再上移多少
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static string TemplateRoot
    {
        get
        {
            var d = Path.Combine(AppContext.BaseDirectory, "templates");
            Directory.CreateDirectory(Path.Combine(d, "idle"));
            Directory.CreateDirectory(Path.Combine(d, "waiting"));
            return d;
        }
    }

    public static string IdleDir => Path.Combine(TemplateRoot, "idle");
    public static string WaitingDir => Path.Combine(TemplateRoot, "waiting");
    public static string RoiConfigPath => Path.Combine(TemplateRoot, "roi.txt");

    public static void SaveRoi(RoiConfig r)
    {
        Directory.CreateDirectory(TemplateRoot);
        File.WriteAllText(RoiConfigPath,
            $"fromBottom={r.FromBottom}\nbottomOffset={r.BottomOffset}\nleft={r.Left}\ntop={r.Top}\nwidth={r.Width}\nheight={r.Height}\n");
    }

    public static RoiConfig LoadRoi()
    {
        var r = new RoiConfig();
        if (!File.Exists(RoiConfigPath)) return r;
        foreach (var line in File.ReadAllLines(RoiConfigPath))
        {
            var p = line.Split('=', 2);
            if (p.Length != 2) continue;
            var k = p[0].Trim().ToLowerInvariant();
            var v = p[1].Trim();
            switch (k)
            {
                case "frombottom": r.FromBottom = v is "1" or "true"; break;
                case "bottomoffset": int.TryParse(v, out var bo); r.BottomOffset = bo; break;
                case "left": int.TryParse(v, out var l); r.Left = l; break;
                case "top": int.TryParse(v, out var t); r.Top = t; break;
                case "width": int.TryParse(v, out var w); r.Width = w; break;
                case "height": int.TryParse(v, out var h); r.Height = h; break;
            }
        }
        return r;
    }

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
            if (title.Contains("HARAN UI", StringComparison.OrdinalIgnoreCase)) return true;
            if (title.Contains("记事本", StringComparison.OrdinalIgnoreCase)) return true;
            if (title.Contains("Notepad", StringComparison.OrdinalIgnoreCase)) return true;
            if (titleFilters.Length == 0 ||
                titleFilters.Any(f => title.Contains(f, StringComparison.OrdinalIgnoreCase)))
                list.Add((h, title, pid));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static Bitmap? CaptureFullWindow(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rc)) return null;
        var w = rc.Right - rc.Left;
        var h = rc.Bottom - rc.Top;
        if (w < 20 || h < 20) return null;
        var full = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                    PrintWindow(hwnd, hdc, 0);
            }
            finally { g.ReleaseHdc(hdc); }
        }
        if (IsMostlyBlack(full))
        {
            using var g2 = Graphics.FromImage(full);
            try
            {
                g2.CopyFromScreen(rc.Left, rc.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }
            catch { full.Dispose(); return null; }
        }
        return full;
    }

    public static Rectangle ResolveRoi(int winW, int winH, RoiConfig roi)
    {
        var height = Math.Clamp(roi.Height <= 0 ? 40 : roi.Height, 8, winH);
        int left = Math.Max(0, roi.Left);
        int width = roi.Width <= 0 ? (winW - left) : roi.Width;
        width = Math.Clamp(width, 8, winW - left);
        int top;
        if (roi.FromBottom)
        {
            var bo = Math.Max(0, roi.BottomOffset);
            top = winH - bo - height;
        }
        else
        {
            top = Math.Clamp(roi.Top, 0, winH - height);
        }
        top = Math.Clamp(top, 0, winH - height);
        return new Rectangle(left, top, width, height);
    }

    public static Bitmap? CaptureRoi(IntPtr hwnd, RoiConfig roi)
    {
        using var full = CaptureFullWindow(hwnd);
        if (full == null) return null;
        var rect = ResolveRoi(full.Width, full.Height, roi);
        if (rect.Width < 4 || rect.Height < 4) return null;
        return full.Clone(rect, PixelFormat.Format24bppRgb);
    }

    public static Bitmap? CaptureRoiWithOverlay(IntPtr hwnd, RoiConfig roi, out Bitmap? fullCopy)
    {
        fullCopy = CaptureFullWindow(hwnd);
        if (fullCopy == null) return null;
        var rect = ResolveRoi(fullCopy.Width, fullCopy.Height, roi);
        var crop = fullCopy.Clone(rect, PixelFormat.Format24bppRgb);
        // 画红框到副本供预览
        var marked = new Bitmap(fullCopy);
        using (var g = Graphics.FromImage(marked))
        using (var pen = new Pen(Color.Red, 3))
            g.DrawRectangle(pen, rect);
        fullCopy.Dispose();
        fullCopy = marked;
        return crop;
    }

    public static string AddTemplate(Bitmap img, bool idle)
    {
        var dir = idle ? IdleDir : WaitingDir;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        img.Save(path, ImageFormat.Png);
        return path;
    }

    public static int CountTemplates(bool idle)
    {
        var dir = idle ? IdleDir : WaitingDir;
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Length : 0;
    }

    public static void ClearTemplates(bool? idleOnly = null)
    {
        if (idleOnly != false)
            foreach (var f in Directory.GetFiles(IdleDir, "*.png")) File.Delete(f);
        if (idleOnly != true)
            foreach (var f in Directory.GetFiles(WaitingDir, "*.png")) File.Delete(f);
    }

    public static double Similarity(Bitmap a, Bitmap b)
    {
        using var aa = Resize(a, 320, 32);
        using var bb = Resize(b, 320, 32);
        long sum = 0;
        long n = 0;
        for (var y = 0; y < aa.Height; y++)
        for (var x = 0; x < aa.Width; x++)
        {
            var ca = aa.GetPixel(x, y);
            var cb = bb.GetPixel(x, y);
            sum += Math.Abs((ca.R + ca.G + ca.B) / 3 - (cb.R + cb.G + cb.B) / 3);
            n++;
        }
        if (n == 0) return 0;
        return Math.Max(0, 1.0 - sum / (double)n / 255.0);
    }

    public static (MatchKind kind, double bestIdle, double bestWait, string? hitFile) MatchMulti(
        Bitmap current, double minScore)
    {
        double bestIdle = 0, bestWait = 0;
        string? hitIdle = null, hitWait = null;
        foreach (var f in Directory.GetFiles(IdleDir, "*.png"))
        {
            try
            {
                using var t = LoadClone(f);
                var s = Similarity(current, t);
                if (s > bestIdle) { bestIdle = s; hitIdle = Path.GetFileName(f); }
            }
            catch { /* skip bad file */ }
        }
        foreach (var f in Directory.GetFiles(WaitingDir, "*.png"))
        {
            try
            {
                using var t = LoadClone(f);
                var s = Similarity(current, t);
                if (s > bestWait) { bestWait = s; hitWait = Path.GetFileName(f); }
            }
            catch { /* */ }
        }

        // 待判优先：同时超过阈值时取更高分，同分偏 waiting
        if (bestWait >= minScore && bestWait >= bestIdle)
            return (MatchKind.Waiting, bestIdle, bestWait, hitWait);
        if (bestIdle >= minScore)
            return (MatchKind.Idle, bestIdle, bestWait, hitIdle);
        return (MatchKind.Unknown, bestIdle, bestWait, null);
    }

    private static Bitmap LoadClone(string path)
    {
        using var fs = File.OpenRead(path);
        using var tmp = new Bitmap(fs);
        return new Bitmap(tmp);
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
