using System.Runtime.InteropServices;

namespace GraphicsSettingsMigrator;

internal static class DarkTheme
{
    public const string ClassicName = "Classic dark";
    public const string ConsoleName = "Console";
    private static bool _consoleStyle = true;
    private static Color Window => _consoleStyle ? Color.FromArgb(10, 14, 12) : Color.FromArgb(24, 24, 27);
    private static Color Surface => _consoleStyle ? Color.FromArgb(15, 21, 18) : Color.FromArgb(32, 33, 36);
    private static Color SurfaceAlt => _consoleStyle ? Color.FromArgb(19, 27, 23) : Color.FromArgb(38, 39, 43);
    private static Color Header => _consoleStyle ? Color.FromArgb(18, 31, 25) : Color.FromArgb(43, 44, 49);
    private static Color Border => _consoleStyle ? Color.FromArgb(42, 66, 54) : Color.FromArgb(68, 70, 76);
    private static Color Text => _consoleStyle ? Color.FromArgb(213, 230, 219) : Color.FromArgb(232, 232, 235);
    private static Color Muted => _consoleStyle ? Color.FromArgb(132, 158, 143) : Color.FromArgb(170, 172, 178);
    private static Color Accent => _consoleStyle ? Color.FromArgb(27, 103, 67) : Color.FromArgb(55, 95, 145);
    private static Color AccentBright => _consoleStyle ? Color.FromArgb(66, 190, 116) : Color.FromArgb(90, 140, 200);
    private static Color Hover => _consoleStyle ? Color.FromArgb(25, 51, 38) : Color.FromArgb(58, 61, 67);
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    public static void Apply(Form form, TabControl tabs, bool consoleStyle)
    {
        SetStyle(form, tabs, consoleStyle);
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.DrawItem += (_, e) =>
        {
            var selected = e.Index == tabs.SelectedIndex;
            using var background = new SolidBrush(selected ? SurfaceAlt : Window);
            using var foreground = new SolidBrush(selected ? Color.White : Muted);
            e.Graphics.FillRectangle(background, e.Bounds);
            var text = tabs.TabPages[e.Index].Text;
            TextRenderer.DrawText(e.Graphics, text, tabs.Font, e.Bounds, foreground.Color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected && _consoleStyle)
            {
                using var underline = new SolidBrush(AccentBright);
                e.Graphics.FillRectangle(underline,
                    new Rectangle(e.Bounds.Left + 5, e.Bounds.Bottom - 3, e.Bounds.Width - 10, 2));
            }
        };

        form.HandleCreated += (_, _) => ApplyNativeDarkMode(form, tabs);
        tabs.HandleCreated += (_, _) => ApplyNativeDarkMode(form, tabs);
        if (form.IsHandleCreated && tabs.IsHandleCreated) ApplyNativeDarkMode(form, tabs);
    }

    public static void SetStyle(Form form, TabControl tabs, bool consoleStyle)
    {
        _consoleStyle = consoleStyle;
        form.Font = new Font(consoleStyle ? "Cascadia Mono" : "Segoe UI", 9F, FontStyle.Regular);
        form.BackColor = Window;
        form.ForeColor = Text;
        tabs.Font = form.Font;
        tabs.Padding = consoleStyle ? new Point(14, 5) : new Point(12, 4);
        ApplyRecursive(form);
        if (form.IsHandleCreated && tabs.IsHandleCreated) ApplyNativeDarkMode(form, tabs);
        tabs.Invalidate();
    }

    public static void Apply(Control control) => ApplyRecursive(control);

    private static void ApplyRecursive(Control control)
    {
        control.ForeColor = Text;
        switch (control)
        {
            case DataGridView grid:
                grid.BackgroundColor = Window;
                grid.GridColor = Border;
                grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.CellBorderStyle = _consoleStyle
                    ? DataGridViewCellBorderStyle.SingleHorizontal : DataGridViewCellBorderStyle.Single;
                grid.ColumnHeadersBorderStyle = _consoleStyle
                    ? DataGridViewHeaderBorderStyle.Single : DataGridViewHeaderBorderStyle.Raised;
                grid.ColumnHeadersHeight = _consoleStyle ? 27 : 23;
                grid.RowTemplate.Height = _consoleStyle ? 24 : 22;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Header;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Header;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Accent;
                grid.DefaultCellStyle.SelectionForeColor = _consoleStyle ? Text : Color.White;
                grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
                foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
                break;
            case TextBox box:
                box.BackColor = Surface;
                box.ForeColor = Text;
                box.BorderStyle = BorderStyle.FixedSingle;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = Surface;
                numeric.ForeColor = Text;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListBox list:
                list.BackColor = Surface;
                list.ForeColor = Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.BackColor = Surface;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case Button button:
                button.BackColor = Header;
                button.ForeColor = Text;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = Hover;
                button.FlatAppearance.MouseDownBackColor = Accent;
                break;
            case CheckBox checkBox:
                checkBox.BackColor = Window;
                checkBox.ForeColor = Text;
                checkBox.FlatStyle = FlatStyle.Flat;
                break;
            case Label label:
                label.BackColor = Window;
                label.ForeColor = label.Enabled ? Text : Muted;
                break;
            case TabPage page:
                page.BackColor = Window;
                break;
            default:
                control.BackColor = Window;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyRecursive(child);
    }

    private static void ApplyNativeDarkMode(Form form, TabControl tabs)
    {
        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode,
                    ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkModeBefore20H1,
                    ref enabled, sizeof(int));

            var windowColor = ColorTranslator.ToWin32(Window);
            var textColor = ColorTranslator.ToWin32(Text);
            var borderColor = ColorTranslator.ToWin32(Border);
            DwmSetWindowAttribute(form.Handle, DwmCaptionColor, ref windowColor, sizeof(int));
            DwmSetWindowAttribute(form.Handle, DwmTextColor, ref textColor, sizeof(int));
            DwmSetWindowAttribute(form.Handle, DwmBorderColor, ref borderColor, sizeof(int));
            SetWindowTheme(form.Handle, "DarkMode_Explorer", null);
            SetWindowTheme(tabs.Handle, "DarkMode_Explorer", null);
        }
        catch
        {
            // Older Windows versions keep the themed client area and use their normal frame.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int valueSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
}
