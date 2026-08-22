using System.Runtime.InteropServices;

namespace GraphicsSettingsMigrator;

internal static class DarkTheme
{
    private static readonly Color Window = Color.FromArgb(10, 14, 12);
    private static readonly Color Surface = Color.FromArgb(15, 21, 18);
    private static readonly Color SurfaceAlt = Color.FromArgb(19, 27, 23);
    private static readonly Color Header = Color.FromArgb(18, 31, 25);
    private static readonly Color Border = Color.FromArgb(42, 66, 54);
    private static readonly Color Text = Color.FromArgb(213, 230, 219);
    private static readonly Color Muted = Color.FromArgb(132, 158, 143);
    private static readonly Color Accent = Color.FromArgb(27, 103, 67);
    private static readonly Color AccentBright = Color.FromArgb(66, 190, 116);
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    public static void Apply(Form form, TabControl tabs)
    {
        form.Font = new Font("Cascadia Mono", 9F, FontStyle.Regular);
        form.BackColor = Window;
        form.ForeColor = Text;
        ApplyRecursive(form);

        tabs.Font = form.Font;
        tabs.Padding = new Point(14, 5);
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
            if (selected)
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
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.ColumnHeadersHeight = 27;
                grid.RowTemplate.Height = 24;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Header;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Header;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Accent;
                grid.DefaultCellStyle.SelectionForeColor = Text;
                grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
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
            case Button button:
                button.BackColor = Header;
                button.ForeColor = Text;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 51, 38);
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
