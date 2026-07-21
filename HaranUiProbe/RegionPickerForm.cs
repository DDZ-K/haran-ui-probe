namespace HaranUiProbe;

/// <summary>在全窗截图上拖拽框选 ROI。</summary>
public sealed class RegionPickerForm : Form
{
    private readonly Bitmap _src;
    private Point? _start;
    private Rectangle _rect;
    private readonly PictureBox _pb;
    private readonly Label _info;

    public Rectangle SelectedRoi { get; private set; }

    public RegionPickerForm(Bitmap fullWindow)
    {
        _src = new Bitmap(fullWindow);
        Text = "拖拽框选截取区域 — 松开后确定";
        Width = Math.Min(1200, _src.Width + 40);
        Height = Math.Min(800, _src.Height + 80);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);

        _info = new Label
        {
            Dock = DockStyle.Top, Height = 28,
            Text = "按住左键拖拽选择区域；确认后关闭。Esc 取消。"
        };
        _pb = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = _src,
            Cursor = Cursors.Cross
        };
        _pb.MouseDown += OnDown;
        _pb.MouseMove += OnMove;
        _pb.MouseUp += OnUp;
        _pb.Paint += OnPaint;
        Controls.Add(_pb);
        Controls.Add(_info);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
    }

    private Point ToImage(Point p)
    {
        // Zoom mode mapping
        if (_pb.Image == null) return p;
        var img = _pb.Image;
        var cw = _pb.ClientSize.Width;
        var ch = _pb.ClientSize.Height;
        var scale = Math.Min(cw / (float)img.Width, ch / (float)img.Height);
        var w = img.Width * scale;
        var h = img.Height * scale;
        var ox = (cw - w) / 2f;
        var oy = (ch - h) / 2f;
        var ix = (int)((p.X - ox) / scale);
        var iy = (int)((p.Y - oy) / scale);
        ix = Math.Clamp(ix, 0, img.Width - 1);
        iy = Math.Clamp(iy, 0, img.Height - 1);
        return new Point(ix, iy);
    }

    private void OnDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _start = ToImage(e.Location);
        _rect = new Rectangle(_start.Value, Size.Empty);
        _pb.Invalidate();
    }

    private void OnMove(object? s, MouseEventArgs e)
    {
        if (_start == null || e.Button != MouseButtons.Left) return;
        var cur = ToImage(e.Location);
        var x = Math.Min(_start.Value.X, cur.X);
        var y = Math.Min(_start.Value.Y, cur.Y);
        var w = Math.Abs(cur.X - _start.Value.X);
        var h = Math.Abs(cur.Y - _start.Value.Y);
        _rect = new Rectangle(x, y, w, h);
        _info.Text = $"区域: X={x} Y={y} W={w} H={h}";
        _pb.Invalidate();
    }

    private void OnUp(object? s, MouseEventArgs e)
    {
        if (_start == null) return;
        _start = null;
        if (_rect.Width < 8 || _rect.Height < 8)
        {
            MessageBox.Show(this, "区域太小，请重新拖拽。");
            return;
        }
        SelectedRoi = _rect;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnPaint(object? s, PaintEventArgs e)
    {
        if (_rect.Width <= 0 || _pb.Image == null) return;
        var img = _pb.Image;
        var cw = _pb.ClientSize.Width;
        var ch = _pb.ClientSize.Height;
        var scale = Math.Min(cw / (float)img.Width, ch / (float)img.Height);
        var w = img.Width * scale;
        var h = img.Height * scale;
        var ox = (cw - w) / 2f;
        var oy = (ch - h) / 2f;
        var r = new RectangleF(
            ox + _rect.X * scale,
            oy + _rect.Y * scale,
            _rect.Width * scale,
            _rect.Height * scale);
        using var pen = new Pen(Color.Lime, 2);
        using var brush = new SolidBrush(Color.FromArgb(40, Color.Lime));
        e.Graphics.FillRectangle(brush, r);
        e.Graphics.DrawRectangle(pen, Rectangle.Round(r));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _src.Dispose();
        base.Dispose(disposing);
    }
}
