namespace MiniERP2.MFG.Forms;

/// <summary>화면 구성 공용 헬퍼(디자이너 없이 코드로 화면을 만든다 — MiniERP2와 같은 방식).</summary>
internal static class Ui
{
    public static readonly Color InputBack = Color.FromArgb(255, 249, 214);   // 노란 입력칸(엑셀 노란 셀과 같은 의미)
    public static readonly Color ConfirmedBack = Color.FromArgb(220, 242, 224);
    public static readonly Color WarnFore = Color.FromArgb(192, 57, 43);
    public static readonly Color LinkFore = Color.FromArgb(31, 97, 141);
    public static readonly Color MutedFore = Color.Gray;

    public static DataGridView Grid(bool readOnly = true)
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = readOnly,
            RowHeadersVisible = false,
            SelectionMode = readOnly ? DataGridViewSelectionMode.FullRowSelect : DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            // 셀 단위 복사(MiniERP2 그리드와 같은 동작) — 머리글 없이 값만 복사.
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
        };
        g.RowTemplate.Height = 24;
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(g, true);
        return g;
    }

    public static DataGridViewTextBoxColumn Col(string name, string header, int width, bool right = false, bool readOnly = false)
    {
        var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.Automatic };
        if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return c;
    }

    public static Button Button(string text, EventHandler onClick, int width = 0)
    {
        var b = new Button { Text = text, AutoSize = width == 0, Height = 28, Margin = new Padding(2) };
        if (width > 0) b.Width = width;
        b.Click += onClick;
        return b;
    }

    public static FlowLayoutPanel Bar(params Control[] controls)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(2) };
        p.Controls.AddRange(controls);
        return p;
    }

    public static Label Label(string text, bool bold = false) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(4, 8, 4, 4),
        Font = bold ? new Font(Control.DefaultFont, FontStyle.Bold) : Control.DefaultFont,
    };

    public static TextBox Text(int width = 160, bool multiline = false, bool input = true)
    {
        var t = new TextBox { Width = width, Multiline = multiline, Margin = new Padding(2, 4, 2, 2) };
        if (multiline) { t.Height = 60; t.ScrollBars = ScrollBars.Vertical; }
        if (input) t.BackColor = InputBack;
        return t;
    }

    /// <summary>라벨-입력칸 2열 표.</summary>
    public static TableLayoutPanel Form(params (string Label, Control Control)[] rows)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (label, control) in rows)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 8, 4) });
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            t.Controls.Add(control);
        }
        return t;
    }

    public static bool Confirm(IWin32Window owner, string message, string title = "확인") =>
        MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static void Info(IWin32Window owner, string message, string title = "안내") =>
        MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Warn(IWin32Window owner, string message, string title = "확인 필요") =>
        MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    /// <summary>한 줄 입력 대화상자.</summary>
    public static string? Prompt(IWin32Window owner, string title, string label, string initial = "", bool multiline = false)
    {
        using var f = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, Width = 460, Height = multiline ? 260 : 160,
        };
        var lbl = new Label { Text = label, Dock = DockStyle.Top, AutoSize = false, Height = 34, Padding = new Padding(8, 8, 8, 0) };
        var tb = new TextBox { Text = initial, Dock = DockStyle.Fill, Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
        var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 80 };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(4) };
        bar.Controls.AddRange([cancel, ok]);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 4) };
        body.Controls.Add(tb);
        f.Controls.Add(body);
        f.Controls.Add(bar);
        f.Controls.Add(lbl);
        f.AcceptButton = multiline ? null : ok;
        f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }

    /// <summary>긴 텍스트(이관 리포트 등) 보기 창.</summary>
    public static void ShowText(IWin32Window owner, string title, string text)
    {
        using var f = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, Width = 900, Height = 640 };
        var tb = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = text, Font = new Font("Consolas", 9.5f) };
        f.Controls.Add(tb);
        f.ShowDialog(owner);
    }

    public static long? SelectedId(DataGridView g) =>
        g.CurrentRow?.Tag is long id ? id : null;

    public static IEnumerable<long> SelectedIds(DataGridView g) =>
        g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<long>()
            .Concat(g.SelectedCells.Cast<DataGridViewCell>().Select(c => g.Rows[c.RowIndex].Tag).OfType<long>()).Distinct();

    /// <summary>데이터 다시 채운 뒤 같은 행(Tag)에 커서를 되돌린다.</summary>
    public static void Reselect(DataGridView g, long? id)
    {
        if (id is null) return;
        foreach (DataGridViewRow r in g.Rows)
        {
            if (r.Tag is long t && t == id && r.Visible)
            {
                var col = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                if (col is not null) g.CurrentCell = r.Cells[col.Index];
                return;
            }
        }
    }

    public static void ApplyFilter(DataGridView g, string search)
    {
        // 검색어 필터: 모든 칸 텍스트에 공백 구분 단어가 모두 포함된 행만 보인다.
        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        g.CurrentCell = null;
        foreach (DataGridViewRow r in g.Rows)
        {
            var hay = string.Join(" ", r.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString()));
            r.Visible = words.All(w => hay.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
    }
}
