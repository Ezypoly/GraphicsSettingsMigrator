namespace GraphicsSettingsMigrator;

internal static class DarkTheme
{
    private static readonly Color Window = Color.FromArgb(24, 24, 27);
    private static readonly Color Surface = Color.FromArgb(32, 33, 36);
    private static readonly Color SurfaceAlt = Color.FromArgb(38, 39, 43);
    private static readonly Color Border = Color.FromArgb(68, 70, 76);
    private static readonly Color Text = Color.FromArgb(232, 232, 235);
    private static readonly Color Muted = Color.FromArgb(170, 172, 178);
    private static readonly Color Accent = Color.FromArgb(55, 95, 145);

    public static void Apply(Form form, TabControl tabs)
    {
        form.BackColor = Window;
        form.ForeColor = Text;
        ApplyRecursive(form);

        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.DrawItem += (_, e) =>
        {
            var selected = e.Index == tabs.SelectedIndex;
            using var background = new SolidBrush(selected ? SurfaceAlt : Window);
            using var foreground = new SolidBrush(selected ? Color.White : Muted);
            e.Graphics.FillRectangle(background, e.Bounds);
            var text = tabs.TabPages[e.Index].Text;
            TextRenderer.DrawText(e.Graphics, text, form.Font, e.Bounds, foreground.Color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }

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
                grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(43, 44, 49);
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 44, 49);
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Accent;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
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
            case Button button:
                button.BackColor = Color.FromArgb(47, 49, 54);
                button.ForeColor = Text;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 61, 67);
                button.FlatAppearance.MouseDownBackColor = Accent;
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
}
