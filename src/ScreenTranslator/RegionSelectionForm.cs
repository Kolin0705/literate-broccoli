using System.Drawing;
using System.Drawing.Drawing2D;
using ScreenTranslator.Services;

namespace ScreenTranslator;

public sealed class RegionSelectionForm : Form
{
    private readonly Rectangle _desktop;
    private readonly Bitmap _screenshot;
    private readonly Font _instructionFont = new("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private Point _start;
    private Point _current;
    private bool _dragging;
    private string _instruction = "拖动鼠标框选游戏文字区域   ·   松开确认   ·   Esc / 右键取消";

    public Rectangle? SelectedRegion { get; private set; }

    public RegionSelectionForm()
    {
        _desktop = SystemInformation.VirtualScreen;
        _screenshot = ScreenCaptureService.Capture(_desktop);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _desktop;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        Text = "框选翻译区域";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x80; // WS_EX_TOOLWINDOW keeps the temporary overlay out of Alt+Tab.
            return parameters;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Restore the full physical desktop after WinForms' initial screen placement.
        Bounds = _desktop;
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(_screenshot, Point.Empty);
        using (var shade = new SolidBrush(Color.FromArgb(145, 8, 13, 23)))
            e.Graphics.FillRectangle(shade, ClientRectangle);

        Rectangle selection = CurrentSelection;
        if (selection.Width > 0 && selection.Height > 0)
        {
            e.Graphics.DrawImage(_screenshot, selection, selection, GraphicsUnit.Pixel);
            using var border = new Pen(Color.FromArgb(88, 218, 183), 2F) { Alignment = PenAlignment.Inset };
            e.Graphics.DrawRectangle(border, selection);
            string size = $"{selection.Width} × {selection.Height}";
            var label = new Rectangle(selection.Left, Math.Max(0, selection.Top - 29), 140, 26);
            using var background = new SolidBrush(Color.FromArgb(230, 20, 29, 44));
            e.Graphics.FillRectangle(background, label);
            TextRenderer.DrawText(e.Graphics, size, _instructionFont, label, Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        Rectangle primary = Screen.PrimaryScreen?.Bounds ?? _desktop;
        var instructions = new Rectangle(primary.Left - _desktop.Left + 24, primary.Top - _desktop.Top + 24,
            Math.Max(1, Math.Min(760, primary.Width - 48)), 46);
        using (var background = new SolidBrush(Color.FromArgb(235, 20, 29, 44)))
            e.Graphics.FillRectangle(background, instructions);
        instructions.Inflate(-12, 0);
        TextRenderer.DrawText(e.Graphics, _instruction, _instructionFont, instructions, Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            CancelSelection();
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;

        _start = ClampToDesktop(e.Location);
        _current = _start;
        _dragging = true;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;

        _current = ClampToDesktop(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left)
            return;

        _current = ClampToDesktop(e.Location);
        _dragging = false;
        Capture = false;
        Rectangle selection = CurrentSelection;
        if (selection.Width < 12 || selection.Height < 12)
        {
            _instruction = "区域太小，请重新拖动框选文字（至少 12 × 12 像素）   ·   Esc 取消";
            Invalidate();
            return;
        }

        SelectedRegion = new Rectangle(_desktop.Left + selection.Left, _desktop.Top + selection.Top,
            selection.Width, selection.Height);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CancelSelection();
        }
        base.OnKeyDown(e);
    }

    private Rectangle CurrentSelection => Rectangle.FromLTRB(Math.Min(_start.X, _current.X),
        Math.Min(_start.Y, _current.Y), Math.Max(_start.X, _current.X), Math.Max(_start.Y, _current.Y));

    private Point ClampToDesktop(Point location) => new(Math.Clamp(location.X, 0, _desktop.Width),
        Math.Clamp(location.Y, 0, _desktop.Height));

    private void CancelSelection()
    {
        SelectedRegion = null;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenshot.Dispose();
            _instructionFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
