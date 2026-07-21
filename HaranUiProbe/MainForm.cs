using System.Text;
using WinForms = System.Windows.Forms;

namespace HaranUiProbe;

public sealed class MainForm : WinForms.Form
{
    private WinForms.TextBox _filter = null!;
    private WinForms.NumericUpDown _left = null!, _top = null!, _width = null!, _height = null!, _bottomOff = null!;
    private WinForms.NumericUpDown _minScore = null!, _pollMs = null!;
    private WinForms.CheckBox _fromBottom = null!, _autoPoll = null!;
    private WinForms.Label _result = null!, _scores = null!, _tplCount = null!;
    private WinForms.PictureBox _previewRoi = null!, _previewFull = null!;
    private WinForms.TextBox _log = null!;
    private WinForms.Timer _timer = null!;
    private Bitmap? _lastRoi;

    public MainForm()
    {
        Text = "HARAN UI 探测 v2.1 · 自定义区域 + 多模板";
        Width = 1080;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);

        var root = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Absolute, 210));
        root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Absolute, 200));
        root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));

        root.Controls.Add(BuildTop(), 0, 0);
        root.Controls.Add(BuildPreview(), 0, 1);
        _log = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Multiline = true,
            ScrollBars = WinForms.ScrollBars.Both,
            WordWrap = false,
            ReadOnly = true,
            Font = new Font("Consolas", 9f)
        };
        root.Controls.Add(_log, 0, 2);
        Controls.Add(root);

        _timer = new WinForms.Timer();
        _timer.Tick += (_, _) => CaptureMatch(log: false);
        LoadRoiUi(StatusBarCapture.LoadRoi());
        RefreshTplCount();
        Append("待机底栏会切换：Currently no Repair Data / archiving Result File … → 空闲请存多张模板。");
        Append("待判 Waiting for Input 也建议存 1～2 张。可用「框选区域」自定义搜索范围。");
    }

    private WinForms.Control BuildTop()
    {
        var p = new WinForms.Panel { Dock = WinForms.DockStyle.Fill, Padding = new Padding(8) };
        int y = 4;
        p.Controls.Add(new WinForms.Label
        {
            Left = 8, Top = y, Width = 1040, Height = 34,
            Text = "① 打开 HARAN  ② 框选/设置区域  ③ 空闲时「加入空闲模板」可多次  ④ 待判时「加入待判模板」  ⑤ 匹配/轮询\n" +
                   "空闲会闪/切换状态文字时，每种外观各存一张（至少 2 张空闲模板）。"
        });
        y = 42;
        p.Controls.Add(L(8, y, 60, "过滤"));
        _filter = new WinForms.TextBox { Left = 70, Top = y, Width = 240, Text = "HARAN;Repair Station;Semi-automatic" };
        p.Controls.Add(_filter);

        _fromBottom = new WinForms.CheckBox { Left = 330, Top = y + 2, Width = 140, Text = "相对底边定位", Checked = true };
        _fromBottom.CheckedChanged += (_, _) => SyncRoiEnable();
        p.Controls.Add(_fromBottom);

        p.Controls.Add(L(480, y, 40, "底↑"));
        _bottomOff = Num(520, y, 55, 0, 400, 0);
        p.Controls.Add(_bottomOff);
        p.Controls.Add(L(585, y, 25, "H"));
        _height = Num(610, y, 55, 8, 800, 48);
        p.Controls.Add(_height);
        p.Controls.Add(L(675, y, 25, "L"));
        _left = Num(700, y, 55, 0, 4000, 0);
        p.Controls.Add(_left);
        p.Controls.Add(L(765, y, 25, "T"));
        _top = Num(790, y, 55, 0, 4000, 0);
        p.Controls.Add(_top);
        p.Controls.Add(L(855, y, 25, "W"));
        _width = Num(880, y, 60, 0, 4000, 0); // 0 = 到右边
        p.Controls.Add(_width);

        y = 78;
        p.Controls.Add(L(8, y, 40, "阈值"));
        _minScore = new WinForms.NumericUpDown
        {
            Left = 50, Top = y, Width = 65, DecimalPlaces = 2, Increment = 0.01M,
            Minimum = 0.50M, Maximum = 0.99M, Value = 0.86M
        };
        p.Controls.Add(_minScore);
        p.Controls.Add(L(130, y, 50, "轮询ms"));
        _pollMs = Num(185, y, 65, 200, 5000, 500);
        p.Controls.Add(_pollMs);
        _autoPoll = new WinForms.CheckBox { Left = 265, Top = y + 2, Width = 90, Text = "自动轮询" };
        _autoPoll.CheckedChanged += (_, _) =>
        {
            _timer.Interval = (int)_pollMs.Value;
            _timer.Enabled = _autoPoll.Checked;
            if (_autoPoll.Checked) CaptureMatch(true);
        };
        p.Controls.Add(_autoPoll);

        _tplCount = new WinForms.Label { Left = 370, Top = y + 4, Width = 280, Text = "模板: —" };
        p.Controls.Add(_tplCount);

        y = 112;
        AddBtn(p, 8, y, 90, "截取区域", (_, _) => CaptureMatch(true));
        AddBtn(p, 105, y, 100, "框选区域…", (_, _) => PickRegion());
        AddBtn(p, 215, y, 120, "加入空闲模板", (_, _) => AddTpl(true));
        AddBtn(p, 345, y, 120, "加入待判模板", (_, _) => AddTpl(false));
        AddBtn(p, 475, y, 90, "立即匹配", (_, _) => CaptureMatch(true));
        AddBtn(p, 575, y, 100, "打开模板夹", (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", StatusBarCapture.TemplateRoot));
        AddBtn(p, 685, y, 100, "清空空闲模板", (_, _) => { StatusBarCapture.ClearTemplates(true); RefreshTplCount(); Append("已清空空闲模板"); });
        AddBtn(p, 795, y, 100, "清空待判模板", (_, _) => { StatusBarCapture.ClearTemplates(false); RefreshTplCount(); Append("已清空待判模板"); });
        AddBtn(p, 905, y, 80, "保存ROI", (_, _) => { SaveRoiFromUi(); Append("ROI 已保存"); });

        y = 150;
        _result = new WinForms.Label
        {
            Left = 8, Top = y, Width = 520, Height = 28,
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            Text = "状态：尚未截取"
        };
        p.Controls.Add(_result);
        _scores = new WinForms.Label { Left = 540, Top = y + 4, Width = 500, Height = 24, Text = "相似度：—" };
        p.Controls.Add(_scores);

        y = 182;
        p.Controls.Add(new WinForms.Label
        {
            Left = 8, Top = y, Width = 1000, Height = 20,
            ForeColor = Color.DimGray,
            Text = "W=0 表示铺满到窗口右边。框选后会自动改为「非底边定位」并写入 L/T/W/H。"
        });
        SyncRoiEnable();
        return p;
    }

    private WinForms.Control BuildPreview()
    {
        var p = new WinForms.Panel { Dock = WinForms.DockStyle.Fill };
        p.Controls.Add(new WinForms.Label { Left = 8, Top = 2, Width = 200, Text = "ROI 预览" });
        p.Controls.Add(new WinForms.Label { Left = 420, Top = 2, Width = 300, Text = "全窗预览（红框=当前区域）" });
        _previewRoi = new WinForms.PictureBox
        {
            Left = 8, Top = 22, Width = 400, Height = 160,
            BorderStyle = WinForms.BorderStyle.FixedSingle,
            SizeMode = WinForms.PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };
        _previewFull = new WinForms.PictureBox
        {
            Left = 420, Top = 22, Width = 620, Height = 160,
            BorderStyle = WinForms.BorderStyle.FixedSingle,
            SizeMode = WinForms.PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };
        p.Controls.Add(_previewRoi);
        p.Controls.Add(_previewFull);
        return p;
    }

    private void SyncRoiEnable()
    {
        _bottomOff.Enabled = _fromBottom.Checked;
        _top.Enabled = !_fromBottom.Checked;
    }

    private StatusBarCapture.RoiConfig RoiFromUi() => new()
    {
        FromBottom = _fromBottom.Checked,
        BottomOffset = (int)_bottomOff.Value,
        Left = (int)_left.Value,
        Top = (int)_top.Value,
        Width = (int)_width.Value,
        Height = (int)_height.Value
    };

    private void LoadRoiUi(StatusBarCapture.RoiConfig r)
    {
        _fromBottom.Checked = r.FromBottom;
        _bottomOff.Value = Math.Clamp(r.BottomOffset, 0, 400);
        _left.Value = Math.Clamp(r.Left, 0, 4000);
        _top.Value = Math.Clamp(r.Top, 0, 4000);
        _width.Value = Math.Clamp(r.Width, 0, 4000);
        _height.Value = Math.Clamp(r.Height <= 0 ? 48 : r.Height, 8, 800);
        SyncRoiEnable();
    }

    private void SaveRoiFromUi() => StatusBarCapture.SaveRoi(RoiFromUi());

    private string[] Filters()
    {
        var f = (_filter.Text ?? "").Split(new[] { ';', ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return f.Length == 0 ? new[] { "HARAN" } : f;
    }

    private (IntPtr hwnd, string title, uint pid)? FindWin()
    {
        var wins = StatusBarCapture.FindWindows(Filters());
        if (wins.Count == 0) return null;
        var best = wins.OrderByDescending(w => w.Title.Length).First();
        return (best.Hwnd, best.Title, best.Pid);
    }

    private void CaptureMatch(bool log)
    {
        try
        {
            var win = FindWin();
            if (win == null)
            {
                SetResult("未找到 HARAN 窗口", Color.DarkOrange);
                if (log) Append("未找到窗口");
                return;
            }

            var roi = RoiFromUi();
            var crop = StatusBarCapture.CaptureRoiWithOverlay(win.Value.hwnd, roi, out var fullMarked);
            if (crop == null)
            {
                SetResult("截图失败", Color.Red);
                return;
            }

            _lastRoi?.Dispose();
            _lastRoi = new Bitmap(crop);
            _previewRoi.Image?.Dispose();
            _previewRoi.Image = new Bitmap(crop);
            if (fullMarked != null)
            {
                _previewFull.Image?.Dispose();
                _previewFull.Image = fullMarked;
            }
            crop.Dispose();

            var min = (double)_minScore.Value;
            var (kind, idleSc, waitSc, hit) = StatusBarCapture.MatchMulti(_lastRoi, min);
            _scores.Text = $"空闲最佳={idleSc:F3}  待判最佳={waitSc:F3}  阈值={min:F2}  命中文件={hit ?? "-"}  |  {win.Value.title}";

            switch (kind)
            {
                case StatusBarCapture.MatchKind.Waiting:
                    SetResult("状态：Waiting / 可判定", Color.DarkGreen);
                    break;
                case StatusBarCapture.MatchKind.Idle:
                    SetResult("状态：空闲（含闪烁/归档等已录模板）", Color.SteelBlue);
                    break;
                default:
                    SetResult(
                        StatusBarCapture.CountTemplates(true) == 0 || StatusBarCapture.CountTemplates(false) == 0
                            ? "状态：未知（请为空闲和待判各加入模板）"
                            : "状态：未知（闪烁态请再「加入空闲模板」；或调区域/阈值）",
                        Color.DarkOrange);
                    break;
            }

            if (log)
                Append($"[{DateTime.Now:HH:mm:ss}] {kind} idle={idleSc:F3} wait={waitSc:F3} hit={hit} roi=({roi.Left},{roi.Top},{roi.Width}x{roi.Height}) fromBottom={roi.FromBottom}");
        }
        catch (Exception ex)
        {
            SetResult("异常: " + ex.Message, Color.Red);
            if (log) Append(ex.ToString());
        }
    }

    private void AddTpl(bool idle)
    {
        try
        {
            CaptureMatch(false);
            if (_lastRoi == null)
            {
                WinForms.MessageBox.Show(this, "请先截取到有效区域。");
                return;
            }
            var path = StatusBarCapture.AddTemplate(_lastRoi, idle);
            RefreshTplCount();
            Append($"已加入{(idle ? "空闲" : "待判")}模板: {Path.GetFileName(path)}  （{(idle ? "空闲" : "待判")}共 {StatusBarCapture.CountTemplates(idle)} 张）");
            SaveRoiFromUi();
            CaptureMatch(true);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, ex.Message);
        }
    }

    private void PickRegion()
    {
        try
        {
            var win = FindWin();
            if (win == null)
            {
                WinForms.MessageBox.Show(this, "未找到 HARAN 窗口");
                return;
            }
            using var full = StatusBarCapture.CaptureFullWindow(win.Value.hwnd);
            if (full == null)
            {
                WinForms.MessageBox.Show(this, "全窗截图失败");
                return;
            }
            using var dlg = new RegionPickerForm(full);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var r = dlg.SelectedRoi;
            _fromBottom.Checked = false;
            _left.Value = r.X;
            _top.Value = r.Y;
            _width.Value = r.Width;
            _height.Value = r.Height;
            SyncRoiEnable();
            SaveRoiFromUi();
            Append($"已框选 ROI: X={r.X} Y={r.Y} W={r.Width} H={r.Height}");
            CaptureMatch(true);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, ex.Message);
        }
    }

    private void SetResult(string t, Color c)
    {
        _result.Text = t;
        _result.ForeColor = c;
    }

    private void RefreshTplCount()
    {
        _tplCount.Text = $"模板: 空闲 {StatusBarCapture.CountTemplates(true)} 张 · 待判 {StatusBarCapture.CountTemplates(false)} 张";
    }

    private void Append(string s) => _log.AppendText(s + "\r\n");

    private static WinForms.Label L(int x, int y, int w, string t) =>
        new() { Left = x, Top = y + 3, Width = w, Text = t };

    private static WinForms.NumericUpDown Num(int x, int y, int w, int min, int max, int val) =>
        new() { Left = x, Top = y, Width = w, Minimum = min, Maximum = max, Value = Math.Clamp(val, min, max) };

    private static void AddBtn(WinForms.Control parent, int x, int y, int w, string t, EventHandler h)
    {
        var b = new WinForms.Button { Left = x, Top = y, Width = w, Height = 28, Text = t };
        b.Click += h;
        parent.Controls.Add(b);
    }

    protected override void OnFormClosed(WinForms.FormClosedEventArgs e)
    {
        _timer.Enabled = false;
        SaveRoiFromUi();
        _lastRoi?.Dispose();
        _previewRoi.Image?.Dispose();
        _previewFull.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
