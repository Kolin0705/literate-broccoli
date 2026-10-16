namespace ScreenTranslator;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(17, 23, 33);
    public static readonly Color Surface = Color.FromArgb(27, 35, 48);
    public static readonly Color Text = Color.FromArgb(234, 239, 247);
    public static readonly Color Muted = Color.FromArgb(158, 173, 194);
    public static readonly Color Accent = Color.FromArgb(93, 222, 184);
    public static readonly Color Warning = Color.FromArgb(255, 193, 112);

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(106, 38),
            Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 10, 8),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Background : Text, Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9F), UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    public static Label Label(string text, float size = 9F, Color? color = null) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Muted,
        Font = new Font("Microsoft YaHei UI", size), Margin = new Padding(0, 0, 0, 8)
    };

    public static TextBox Input(bool secret = false) => new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Surface, ForeColor = Text, Font = new Font("Microsoft YaHei UI", 11F),
        UseSystemPasswordChar = secret, Margin = new Padding(0, 0, 0, 16)
    };
}
