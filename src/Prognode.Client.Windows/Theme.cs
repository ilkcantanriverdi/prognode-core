namespace Prognode.Client.Windows;

/// <summary>PROGNODE dark palette shared by the client windows.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(7, 21, 32);
    public static readonly Color Panel = Color.FromArgb(14, 39, 59);
    public static readonly Color Line = Color.FromArgb(38, 66, 86);
    public static readonly Color Text = Color.FromArgb(230, 237, 244);
    public static readonly Color Muted = Color.FromArgb(142, 166, 184);
    public static readonly Color Accent = Color.FromArgb(22, 207, 187);
    public static readonly Color Critical = Color.FromArgb(255, 118, 131);
    public static readonly Color Warning = Color.FromArgb(255, 202, 115);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 16f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Code = new("Consolas", 20f, FontStyle.Bold);

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = Body;
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "prognode.ico");
        if (File.Exists(icon)) form.Icon = new Icon(icon);
    }

    public static Button Primary(string text) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.FromArgb(3, 22, 29),
        Font = new Font("Segoe UI Semibold", 9.5f), Padding = new Padding(14, 6, 14, 6), Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
    };

    public static Button Secondary(string text) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = Text,
        Padding = new Padding(12, 6, 12, 6), Cursor = Cursors.Hand, FlatAppearance = { BorderColor = Line },
    };

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, MaximumSize = new Size(560, 0), Margin = new Padding(0, 4, 0, 4),
    };

    public static TextBox Input(string placeholder) => new()
    {
        PlaceholderText = placeholder, BackColor = Panel, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Width = 260,
    };
}
