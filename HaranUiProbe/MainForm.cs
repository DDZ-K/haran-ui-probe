using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using WinForms = System.Windows.Forms;

namespace HaranUiProbe;

/// <summary>
/// v2：主推状态栏截图模板匹配；保留 UIA 扫描作对照。
/// </summary>
public sealed class MainForm : WinForms.Form
{
    private readonly WinForms.TabControl _tabs;
    private readonly WinForms.TextBox _filter;
    private readonly WinForms.NumericUpDown _barPx;
    private readonly WinForms.NumericUpDown _minScore;
    private readonly WinForms.NumericUpDown _pollMs;
    private readonly WinForms.CheckBox _autoPoll;
    private readonly WinForms.Label _result;
    private readonly WinForms.Label _scores;
    private readonly WinForms.PictureBox _preview;
    private readonly WinForms.TextBox _log;
    private readonly WinForms.Timer _timer;
    private Bitmap? _lastBar;

    // UIA tab
    private readonly WinForms.TextBox _uiaLog;
    private readonly WinForms.CheckBox _onlyHit;

    public MainForm()
    {
        Text = "HARAN UI 探测 v2.0 · 状态栏截图";
        Width = 1000;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);

        _tabs = new WinForms.TabControl { Dock = WinForms.DockStyle.Fill };
        var tabBar = new WinForms.TabPage("状态栏截图（推荐）");
        var tabUia = new WinForms.TabPage("UIA 控件扫描（对照）");
        _tabs.TabPages.Add(tabBar);
        _tabs.TabPages.Add(tabUia);
        Controls.Add(_tabs);

        BuildStatusTab(tabBar);
        BuildUiaTab(tabUia);

        _timer = new WinForms.Timer();
        _timer.Tick += (_, _) => CaptureAndMatch(log: false);
    }

    private void BuildStatusTab(WinForms.TabPage tab)
    {
        var top = new WinForms.Panel { Dock = WinForms.DockStyle.Top, Height = 150, Padding = new Padding(8) };
        var y = 6;
        top.Controls.Add(new WinForms.Label
        {
            Left = 8, Top = y, Width = 960, Height = 36,
            Text = "用法：1) 打开 HARAN  2) 空闲时点「保存为空闲模板」  3) 待判(Waiting)时点「保存为待判模板」  4) 点匹配/开轮询。\n" +
                   "不读控件文字，只比底栏截图。阈值越高越严（默认 0.88）。"
        });
        y = 48;
        top.Controls.Add(new WinForms.Label { Left = 8, Top = y + 3, Width = 70, Text = "标题过滤" });
        _filter = new WinForms.TextBox
        {
            Left = 80, Top = y, Width = 260,
            Text = "HARAN;Repair Station;Semi-automatic"
        };
        top.Controls.Add(_filter);

        top.Controls.Add(new WinForms.Label { Left = 350, Top = y + 3, Width = 70, Text = "底栏像素" });
        _barPx = new WinForms.NumericUpDown
        {
            Left = 425, Top = y, Width = 60,
            Minimum = 20, Maximum = 120, Value = 40
        };
        top.Controls.Add(_barPx);

        top.Controls.Add(new WinForms.Label { Left = 500, Top = y + 3, Width = 50, Text = "阈值" });
        _minScore = new WinForms.NumericUpDown
        {
            Left = 550, Top = y, Width = 70,
            DecimalPlaces = 2, Increment = 0.01M,
            Minimum = 0.50M, Maximum = 0.99M, Value = 0.88M
        };
        top.Controls.Add(_minScore);

        top.Controls.Add(new WinForms.Label { Left = 640, Top = y + 3, Width = 50, Text = "轮询ms" });
        _pollMs = new WinForms.NumericUpDown
        {
            Left = 695, Top = y, Width = 70,
            Minimum = 200, Maximum = 5000, Value = 500, Increment = 100
        };
        top.Controls.Add(_pollMs);

        _autoPoll = new WinForms.CheckBox { Left = 780, Top = y + 2, Width = 100, Text = "自动轮询" };
        _autoPoll.CheckedChanged += (_, _) =>
        {
            _timer.Interval = (int)_pollMs.Value;
            _timer.Enabled = _autoPoll.Checked;
            if (_autoPoll.Checked) CaptureAndMatch(log: true);
        };
        top.Controls.Add(_autoPoll);

        y = 86;
        var b1 = Btn(8, y, 100, "截取底栏");
        b1.Click += (_, _) => CaptureAndMatch(log: true);
        top.Controls.Add(b1);

        var b2 = Btn(118, y, 130, "保存为空闲模板");
        b2.Click += (_, _) => SaveTpl(idle: true);
        top.Controls.Add(b2);

        var b3 = Btn(258, y, 130, "保存为待判模板");
        b3.Click += (_, _) => SaveTpl(idle: false);
        top.Controls.Add(b3);

        var b4 = Btn(398, y, 100, "立即匹配");
        b4.Click += (_, _) => CaptureAndMatch(log: true);
        top.Controls.Add(b4);

        var b5 = Btn(508, y, 120, "打开模板目录");
        b5.Click += (_, _) =>
        {
            System.Diagnostics.Process.Start("explorer.exe", StatusBarCapture.TemplateDir);
        };
        top.Controls.Add(b5);

        var b6 = Btn(638, y, 100, "保存日志");
        b6.Click += (_, _) => SaveLog(_log);
        top.Controls.Add(b6);

        y = 120;
        _result = new WinForms.Label
        {
            Left = 8, Top = y, Width = 500, Height = 24,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            Text = "状态：尚未截取"
        };
        top.Controls.Add(_result);
        _scores = new WinForms.Label
        {
            Left = 520, Top = y + 2, Width = 440, Height = 22,
            Text = "相似度：—"
        };
        top.Controls.Add(_scores);

        var mid = new WinForms.Panel { Dock = WinForms.DockStyle.Top, Height = 90 };
        mid.Controls.Add(new WinForms.Label { Left = 8, Top = 4, Width = 200, Text = "当前底栏预览：" });
        _preview = new WinForms.PictureBox
        {
            Left = 8, Top = 24, Width = 960, Height = 56,
            BorderStyle = WinForms.BorderStyle.FixedSingle,
            SizeMode = WinForms.PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };
        mid.Controls.Add(_preview);

        _log = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Multiline = true,
            ScrollBars = WinForms.ScrollBars.Both,
            WordWrap = false,
            ReadOnly = true,
            Font = new Font("Consolas", 9f)
        };

        tab.Controls.Add(_log);
        tab.Controls.Add(mid);
        tab.Controls.Add(top);
    }

    private void BuildUiaTab(WinForms.TabPage tab)
    {
        var top = new WinForms.Panel { Dock = WinForms.DockStyle.Top, Height = 70 };
        _onlyHit = new WinForms.CheckBox
        {
            Left = 12, Top = 12, Width = 220,
            Text = "只显示关键字控件",
            Checked = true
        };
        top.Controls.Add(_onlyHit);
        var b = Btn(12, 38, 120, "UIA 扫描一次");
        b.Click += (_, _) => RunUiaScan();
        top.Controls.Add(b);
        top.Controls.Add(new WinForms.Label
        {
            Left = 150, Top = 42, Width = 700,
            Text = "对照用：此前已确认状态栏文字读不到属正常。"
        });

        _uiaLog = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Multiline = true,
            ScrollBars = WinForms.ScrollBars.Both,
            WordWrap = false,
            ReadOnly = true,
            Font = new Font("Consolas", 9f)
        };
        tab.Controls.Add(_uiaLog);
        tab.Controls.Add(top);
    }

    private static WinForms.Button Btn(int x, int y, int w, string t) =>
        new() { Left = x, Top = y, Width = w, Height = 28, Text = t };

    private string[] Filters()
    {
        var f = (_filter.Text ?? "")
            .Split(new[] { ';', ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return f.Length == 0 ? new[] { "HARAN" } : f;
    }

    private void CaptureAndMatch(bool log)
    {
        try
        {
            var wins = StatusBarCapture.FindWindows(Filters());
            if (wins.Count == 0)
            {
                _result.Text = "状态：未找到 HARAN 窗口";
                _result.ForeColor = Color.DarkOrange;
                if (log) AppendLog(_log, "未找到窗口，请检查 HARAN 是否打开、过滤关键字。");
                return;
            }

            // 优先标题最长的（通常主窗）
            var best = wins.OrderByDescending(w => w.Title.Length).First();
            using var bar = StatusBarCapture.CaptureBottomBar(best.Hwnd, barPixels: (int)_barPx.Value);
            if (bar == null)
            {
                _result.Text = "状态：截图失败";
                _result.ForeColor = Color.Red;
                if (log) AppendLog(_log, "截取底栏失败。");
                return;
            }

            _lastBar?.Dispose();
            _lastBar = new Bitmap(bar);
            _preview.Image?.Dispose();
            _preview.Image = new Bitmap(bar);

            using var idle = StatusBarCapture.LoadTemplate(StatusBarCapture.IdleTemplatePath);
            using var wait = StatusBarCapture.LoadTemplate(StatusBarCapture.WaitingTemplatePath);
            var min = (double)_minScore.Value;
            var (kind, idleSc, waitSc) = StatusBarCapture.Match(bar, idle, wait, min);
            _scores.Text = $"相似度：空闲={idleSc:F3}  待判={waitSc:F3}  阈值={min:F2}  |  窗={best.Title}";

            switch (kind)
            {
                case StatusBarCapture.MatchKind.WaitingForInput:
                    _result.Text = "状态：Waiting for Input（可判定）";
                    _result.ForeColor = Color.DarkGreen;
                    break;
                case StatusBarCapture.MatchKind.IdleNoRepairData:
                    _result.Text = "状态：Currently no Repair Data（空闲）";
                    _result.ForeColor = Color.SteelBlue;
                    break;
                default:
                    _result.Text = idle == null || wait == null
                        ? "状态：未知（请先保存空闲+待判两套模板）"
                        : "状态：未知（不像两套模板，可调阈值/底栏像素）";
                    _result.ForeColor = Color.DarkOrange;
                    break;
            }

            if (log)
            {
                AppendLog(_log,
                    $"[{DateTime.Now:HH:mm:ss}] PID={best.Pid} bar={bar.Width}x{bar.Height} " +
                    $"idle={idleSc:F3} wait={waitSc:F3} => {kind}\r\n  {best.Title}");
            }
        }
        catch (Exception ex)
        {
            _result.Text = "状态：异常 " + ex.Message;
            _result.ForeColor = Color.Red;
            if (log) AppendLog(_log, "异常: " + ex);
        }
    }

    private void SaveTpl(bool idle)
    {
        try
        {
            if (_lastBar == null)
                CaptureAndMatch(log: false);
            if (_lastBar == null)
            {
                WinForms.MessageBox.Show(this, "请先成功截取底栏。", "提示");
                return;
            }

            // 再截一次保证最新
            CaptureAndMatch(log: false);
            if (_lastBar == null) return;

            var path = idle ? StatusBarCapture.IdleTemplatePath : StatusBarCapture.WaitingTemplatePath;
            StatusBarCapture.SaveTemplate(_lastBar, path);
            var name = idle ? "空闲 (no Repair Data)" : "待判 (Waiting for Input)";
            AppendLog(_log, $"已保存{name}模板 → {path}");
            WinForms.MessageBox.Show(this, $"已保存{name}模板：\n{path}", "OK");
            CaptureAndMatch(log: true);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, ex.Message, "保存失败");
        }
    }

    private void RunUiaScan()
    {
        try
        {
            var filters = Filters();
            var sb = new StringBuilder();
            sb.AppendLine($"==== UIA {DateTime.Now:HH:mm:ss} ====");
            var selfPid = Environment.ProcessId;
            var hitsW = 0;
            var hitsN = 0;
            var winN = 0;
            var elN = 0;

            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
            foreach (var w in windows)
            {
                string title;
                try { title = w.Name ?? ""; } catch { continue; }
                int pid;
                try { pid = w.Properties.ProcessId.ValueOrDefault; } catch { continue; }
                if (pid == selfPid) continue;
                if (string.IsNullOrWhiteSpace(title)) continue;
                if (!filters.Any(f => title.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                winN++;
                sb.AppendLine($"窗口: {title} PID={pid}");
                WalkUia(w, 0, 10, 3000, sb, _onlyHit.Checked, ref hitsW, ref hitsN, ref elN);
            }
            sb.AppendLine($"摘要: 窗={winN} 控件={elN} Waiting命中={hitsW} NoData命中={hitsN}");
            if (hitsW == 0 && hitsN == 0)
                sb.AppendLine("（状态栏自绘时为 0 属正常，请用「状态栏截图」页）");
            _uiaLog.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            _uiaLog.Text = ex.ToString();
        }
    }

    private static void WalkUia(
        AutomationElement parent, int depth, int maxD, int maxE,
        StringBuilder sb, bool onlyHit, ref int hitsW, ref int hitsN, ref int elN)
    {
        if (depth > maxD || elN >= maxE) return;
        AutomationElement[] children;
        try { children = depth == 0 ? new[] { parent } : parent.FindAllChildren(); }
        catch { return; }
        if (depth == 0)
        {
            // fall through to process parent then children
        }
        IEnumerable<AutomationElement> seq = depth == 0
            ? new[] { parent }.Concat(SafeChildren(parent))
            : children;

        if (depth == 0)
        {
            foreach (var c in SafeChildren(parent))
                WalkUiaNode(c, 1, maxD, maxE, sb, onlyHit, ref hitsW, ref hitsN, ref elN);
            return;
        }

        foreach (var c in children)
            WalkUiaNode(c, depth, maxD, maxE, sb, onlyHit, ref hitsW, ref hitsN, ref elN);
    }

    private static AutomationElement[] SafeChildren(AutomationElement p)
    {
        try { return p.FindAllChildren(); }
        catch { return Array.Empty<AutomationElement>(); }
    }

    private static void WalkUiaNode(
        AutomationElement el, int depth, int maxD, int maxE,
        StringBuilder sb, bool onlyHit, ref int hitsW, ref int hitsN, ref int elN)
    {
        if (depth > maxD || elN >= maxE) return;
        elN++;
        string name = "", val = "";
        try { name = el.Name ?? ""; } catch { /* */ }
        try
        {
            if (el.Patterns.Value.IsSupported)
                val = el.Patterns.Value.Pattern.Value ?? "";
        }
        catch { /* */ }
        var blob = name + " " + val;
        var w = blob.IndexOf("Waiting for Input", StringComparison.OrdinalIgnoreCase) >= 0;
        var n = blob.IndexOf("no Repair Data", StringComparison.OrdinalIgnoreCase) >= 0;
        if (w) hitsW++;
        if (n) hitsN++;
        if (!onlyHit || w || n || blob.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(val))
                sb.AppendLine($"{new string(' ', depth * 2)}[{depth}] {name} | {val}");
        }
        foreach (var c in SafeChildren(el))
            WalkUiaNode(c, depth + 1, maxD, maxE, sb, onlyHit, ref hitsW, ref hitsN, ref elN);
    }

    private static void AppendLog(WinForms.TextBox box, string line)
    {
        box.AppendText(line + "\r\n");
    }

    private void SaveLog(WinForms.TextBox box)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "probe-logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"haran-statusbar-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, box.Text ?? "", Encoding.UTF8);
            // 也存最后一帧预览
            if (_lastBar != null)
            {
                var img = Path.Combine(dir, $"haran-statusbar-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                _lastBar.Save(img);
            }
            WinForms.MessageBox.Show(this, "已保存:\n" + path, "OK");
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, ex.Message, "失败");
        }
    }

    protected override void OnFormClosed(WinForms.FormClosedEventArgs e)
    {
        _timer.Enabled = false;
        _lastBar?.Dispose();
        _preview.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
