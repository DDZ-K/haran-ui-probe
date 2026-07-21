using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using WinForms = System.Windows.Forms;

namespace HaranUiProbe;

/// <summary>
/// 探测 HARAN / Viscom 复判界面能否通过 UI Automation 读到
/// "Waiting for Input" / "Currently no Repair Data" 等文字。
/// </summary>
public sealed class MainForm : WinForms.Form
{
    private readonly WinForms.TextBox _filter;
    private readonly WinForms.CheckBox _onlyHit;
    private readonly WinForms.CheckBox _autoPoll;
    private readonly WinForms.NumericUpDown _pollMs;
    private readonly WinForms.Button _btnScan;
    private readonly WinForms.Button _btnSave;
    private readonly WinForms.Label _summary;
    private readonly WinForms.TextBox _log;
    private readonly WinForms.Timer _timer;

    public MainForm()
    {
        Text = "HARAN UI 控件探测 v1.0.1";
        Width = 980;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);

        var top = new WinForms.Panel { Dock = WinForms.DockStyle.Top, Height = 120, Padding = new Padding(10) };
        var y = 8;
        var lbl = new WinForms.Label
        {
            Left = 10, Top = y, Width = 900, Height = 40,
            Text = "用途：检查 HARAN 窗口能否读到 State 文字（Waiting for Input / no Repair Data）。\n" +
                   "请先打开 HARAN 复判界面，再点「扫描一次」。若 HARAN 用管理员运行，本工具也请管理员打开。"
        };
        top.Controls.Add(lbl);
        y = 52;

        top.Controls.Add(new WinForms.Label { Left = 10, Top = y + 3, Width = 70, Text = "标题过滤" });
        _filter = new WinForms.TextBox
        {
            Left = 80, Top = y, Width = 280,
            Text = "HARAN;Repair Station;Semi-automatic"
        };
        top.Controls.Add(_filter);

        _onlyHit = new WinForms.CheckBox
        {
            Left = 380, Top = y + 2, Width = 220,
            Text = "只显示命中关键字的控件",
            Checked = false
        };
        top.Controls.Add(_onlyHit);

        _autoPoll = new WinForms.CheckBox
        {
            Left = 620, Top = y + 2, Width = 100,
            Text = "自动轮询"
        };
        top.Controls.Add(_autoPoll);

        top.Controls.Add(new WinForms.Label { Left = 730, Top = y + 3, Width = 50, Text = "间隔ms" });
        _pollMs = new WinForms.NumericUpDown
        {
            Left = 785, Top = y, Width = 80,
            Minimum = 200, Maximum = 10000, Value = 500, Increment = 100
        };
        top.Controls.Add(_pollMs);

        y = 88;
        _btnScan = new WinForms.Button { Left = 10, Top = y, Width = 120, Height = 28, Text = "扫描一次" };
        _btnScan.Click += (_, _) => RunScan();
        top.Controls.Add(_btnScan);

        _btnSave = new WinForms.Button { Left = 140, Top = y, Width = 140, Height = 28, Text = "保存结果到文件" };
        _btnSave.Click += (_, _) => SaveLog();
        top.Controls.Add(_btnSave);

        _summary = new WinForms.Label
        {
            Left = 300, Top = y + 4, Width = 640, Height = 24,
            Text = "就绪判断：尚未扫描"
        };
        top.Controls.Add(_summary);

        _log = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Multiline = true,
            ScrollBars = WinForms.ScrollBars.Both,
            WordWrap = false,
            ReadOnly = true,
            Font = new Font("Consolas", 9f)
        };

        Controls.Add(_log);
        Controls.Add(top);

        _timer = new WinForms.Timer();
        _timer.Tick += (_, _) => RunScan();
        _autoPoll.CheckedChanged += (_, _) =>
        {
            _timer.Interval = (int)_pollMs.Value;
            _timer.Enabled = _autoPoll.Checked;
            if (_autoPoll.Checked) RunScan();
        };
        _pollMs.ValueChanged += (_, _) => _timer.Interval = (int)_pollMs.Value;
    }

    private void RunScan()
    {
        try
        {
            _btnScan.Enabled = false;
            Cursor = WinForms.Cursors.WaitCursor;
            var filters = (_filter.Text ?? "")
                .Split(new[] { ';', '，', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (filters.Length == 0)
                filters = new[] { "HARAN" };

            var sb = new StringBuilder();
            sb.AppendLine($"==== 扫描时间 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
            sb.AppendLine($"过滤器: {string.Join(" | ", filters)}");
            sb.AppendLine();

            var hitsWaiting = new List<string>();
            var hitsNoData = new List<string>();
            var allInteresting = new List<string>();
            var windowCount = 0;
            var elementCount = 0;

            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
            var selfPid = Environment.ProcessId;
            var selfTitleHint = "HARAN UI 控件探测";

            foreach (var w in windows)
            {
                string title;
                try { title = w.Name ?? ""; }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(title)) continue;

                // 排除本探针窗口（说明文字里也写了 Waiting for Input，会误报）
                int wpid = -1;
                try { wpid = w.Properties.ProcessId.ValueOrDefault; } catch { /* */ }
                if (wpid == selfPid) continue;
                if (title.IndexOf(selfTitleHint, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (title.IndexOf("HaranUiProbe", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (title.IndexOf("haran-probe-", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (title.IndexOf("记事本", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (title.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (title.IndexOf("Notepad", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                if (!filters.Any(f => title.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                windowCount++;
                string pid = wpid > 0 ? wpid.ToString() : "?";

                sb.AppendLine("--------------------------------------------------");
                sb.AppendLine($"窗口: {title}");
                sb.AppendLine($"  PID={pid}  Class≈{Safe(() => w.ClassName)}  Type={Safe(() => w.ControlType.ToString())}");
                sb.AppendLine();

                // 自身
                CollectElement(w, 0, sb, _onlyHit.Checked, hitsWaiting, hitsNoData, allInteresting, ref elementCount);

                // 整棵子树（限制深度与数量，避免卡死）
                Walk(w, 1, 12, 4000, sb, _onlyHit.Checked, hitsWaiting, hitsNoData, allInteresting, ref elementCount);
                sb.AppendLine();
            }

            if (windowCount == 0)
            {
                sb.AppendLine("未找到标题匹配的窗口（已排除本探针 / 记事本）。");
                sb.AppendLine("请确认 HARAN 已打开；过滤器可改成更短关键字，例如只写 HARAN。");
                sb.AppendLine();
                sb.AppendLine("当前顶层窗口标题列表（前 40 个，已跳过本进程）：");
                var n = 0;
                foreach (var w in windows)
                {
                    string t;
                    try { t = w.Name ?? ""; } catch { continue; }
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    int wpid = -1;
                    try { wpid = w.Properties.ProcessId.ValueOrDefault; } catch { /* */ }
                    if (wpid == selfPid) continue;
                    sb.AppendLine($"  - {t}");
                    if (++n >= 40) break;
                }
            }

            sb.AppendLine();
            sb.AppendLine("==== 摘要 ====");
            sb.AppendLine($"(已排除本探针 PID={selfPid} 与记事本，避免说明文字误报)");
            sb.AppendLine($"匹配窗口数: {windowCount}");
            sb.AppendLine($"遍历控件数: {elementCount}");
            sb.AppendLine($"含 Waiting for Input: {hitsWaiting.Count}");
            foreach (var h in hitsWaiting.Take(20)) sb.AppendLine("  * " + h);
            sb.AppendLine($"含 Currently no Repair Data / no Repair Data: {hitsNoData.Count}");
            foreach (var h in hitsNoData.Take(20)) sb.AppendLine("  * " + h);

            string verdict;
            if (hitsWaiting.Count > 0 && hitsNoData.Count == 0)
                verdict = "【可读】在目标窗检测到 Waiting for Input → 控件方案大概率可用";
            else if (hitsNoData.Count > 0 && hitsWaiting.Count == 0)
                verdict = "【可读】在目标窗检测到 no Repair Data（空闲）→ 控件方案大概率可用；请再在待判时扫一次";
            else if (hitsWaiting.Count > 0 && hitsNoData.Count > 0)
                verdict = "【可读】目标窗内两种状态文字都出现过 → 很好，可用文字做门闩";
            else if (windowCount > 0)
                verdict = "【未读到目标字】找到 HARAN 窗但树内无 Waiting/no Repair Data → 状态栏可能自绘，需截图模板/OCR";
            else
                verdict = "【未找到窗口】请打开 HARAN 或调整过滤器";

            // 额外：把窗口标题里是否含这些字也单独标出（标题≠状态栏）
            if (windowCount > 0 && hitsWaiting.Count == 0 && hitsNoData.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("提示：若底部肉眼能看见 State: Currently no Repair Data，");
                sb.AppendLine("但本摘要为 0，说明该文字未暴露给 UI Automation（自绘/贴图状态栏）。");
            }

            sb.AppendLine();
            sb.AppendLine(verdict);
            if (allInteresting.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("其他可疑关键字（State/Board/Repair/Input/Fault）：");
                foreach (var x in allInteresting.Distinct().Take(40))
                    sb.AppendLine("  - " + x);
            }

            _log.Text = sb.ToString();
            _summary.Text = verdict;
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        catch (Exception ex)
        {
            _log.Text = "扫描异常:\r\n" + ex;
            _summary.Text = "扫描异常: " + ex.Message;
        }
        finally
        {
            _btnScan.Enabled = true;
            Cursor = WinForms.Cursors.Default;
        }
    }

    private static void Walk(
        AutomationElement parent,
        int depth,
        int maxDepth,
        int maxElements,
        StringBuilder sb,
        bool onlyHit,
        List<string> hitsWaiting,
        List<string> hitsNoData,
        List<string> interesting,
        ref int elementCount)
    {
        if (depth > maxDepth || elementCount >= maxElements) return;
        AutomationElement[] children;
        try { children = parent.FindAllChildren(); }
        catch { return; }

        foreach (var c in children)
        {
            if (elementCount >= maxElements) return;
            CollectElement(c, depth, sb, onlyHit, hitsWaiting, hitsNoData, interesting, ref elementCount);
            Walk(c, depth + 1, maxDepth, maxElements, sb, onlyHit, hitsWaiting, hitsNoData, interesting, ref elementCount);
        }
    }

    private static void CollectElement(
        AutomationElement el,
        int depth,
        StringBuilder sb,
        bool onlyHit,
        List<string> hitsWaiting,
        List<string> hitsNoData,
        List<string> interesting,
        ref int elementCount)
    {
        elementCount++;
        string name = "", help = "", value = "", cls = "", ctype = "";
        try { name = el.Name ?? ""; } catch { /* */ }
        try { help = el.Properties.HelpText.ValueOrDefault ?? ""; } catch { /* */ }
        try { cls = el.ClassName ?? ""; } catch { /* */ }
        try { ctype = el.ControlType.ToString(); } catch { /* */ }
        try
        {
            if (el.Patterns.Value.IsSupported)
                value = el.Patterns.Value.Pattern.Value ?? "";
        }
        catch { /* */ }
        try
        {
            if (el.Patterns.Text.IsSupported)
            {
                var t = el.Patterns.Text.Pattern.DocumentRange?.GetText(200) ?? "";
                if (!string.IsNullOrWhiteSpace(t) && t != value)
                    value = string.IsNullOrEmpty(value) ? t : value + " | " + t;
            }
        }
        catch { /* */ }
        try
        {
            if (el.Patterns.LegacyIAccessible.IsSupported)
            {
                var leg = el.Patterns.LegacyIAccessible.Pattern.Name ?? "";
                var legVal = el.Patterns.LegacyIAccessible.Pattern.Value ?? "";
                if (!string.IsNullOrWhiteSpace(leg) && string.IsNullOrWhiteSpace(name)) name = leg;
                if (!string.IsNullOrWhiteSpace(legVal) && string.IsNullOrWhiteSpace(value)) value = legVal;
            }
        }
        catch { /* */ }

        var blob = $"{name} {value} {help}";
        var isWaiting = Contains(blob, "Waiting for Input");
        var isNoData = Contains(blob, "Currently no Repair Data") || Contains(blob, "no Repair Data");
        var isInteresting = isWaiting || isNoData
            || Contains(blob, "State")
            || Contains(blob, "Board")
            || Contains(blob, "Repair")
            || Contains(blob, "Input")
            || Contains(blob, "Fault")
            || Contains(blob, "Continue with");

        if (isWaiting) hitsWaiting.Add(TrimOne($"[depth={depth}] {ctype} name={name} value={value}"));
        if (isNoData) hitsNoData.Add(TrimOne($"[depth={depth}] {ctype} name={name} value={value}"));
        if (isInteresting) interesting.Add(TrimOne($"[{ctype}] {name} | {value}"));

        if (onlyHit && !isInteresting) return;
        if (!onlyHit && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(help))
            return;

        var pad = new string(' ', Math.Min(depth, 16) * 2);
        var mark = isWaiting ? " <<WAITING>>" : isNoData ? " <<NO_DATA>>" : isInteresting ? " <<HIT>>" : "";
        sb.AppendLine($"{pad}[{depth}] {ctype} class={cls}");
        if (!string.IsNullOrWhiteSpace(name)) sb.AppendLine($"{pad}  Name: {name}{mark}");
        if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"{pad}  Value: {value}{mark}");
        if (!string.IsNullOrWhiteSpace(help)) sb.AppendLine($"{pad}  Help: {help}");
    }

    private static bool Contains(string hay, string needle)
        => hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string TrimOne(string s)
        => s.Length <= 240 ? s : s[..240] + "...";

    private static string Safe(Func<string?> f)
    {
        try { return f() ?? ""; }
        catch { return ""; }
    }

    private void SaveLog()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "probe-logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"haran-probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, _log.Text ?? "", Encoding.UTF8);
            WinForms.MessageBox.Show(this, "已保存:\n" + path, "保存成功",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, ex.Message, "保存失败",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }
}
