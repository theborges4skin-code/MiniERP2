namespace MiniERP2.MFG.Forms;

/// <summary>화면 구성 공용 헬퍼(디자이너 없이 코드로 화면을 만든다 — MiniERP2와 같은 방식).</summary>
/// <summary>
/// 화면 색 규칙(일반/다크 모드 공용). 원칙: <b>배경색을 칠하면 글자색도 반드시 짝으로 지정한다</b> — 배경만 칠하면 다크 모드에서
/// 기본 글자색(흰색)이 밝은 배경 위에 나와 안 보인다(2026-10-21 사용자 지적). 그래서 배경은 아래 Apply* 메서드로만 칠한다.
/// 글자색만 바꾸는 경고/연결/흐림 색도 모드별로 대비가 충분한 값을 쓴다.
/// </summary>
internal static class Theme
{
    public static bool IsDark => Application.IsDarkModeEnabled;

    private static Color Pick(int light, int dark) => Color.FromArgb(unchecked((int)0xFF000000) | (IsDark ? dark : light));

    // 입력칸(엑셀 노란 셀과 같은 의미)
    public static Color InputBack => Pick(0xFFF9D6, 0x3D3720);
    public static Color InputFore => Pick(0x1A1A1A, 0xF5EED2);
    // 확정 표시
    public static Color ConfirmedBack => Pick(0xDCF2E0, 0x1E4029);
    public static Color ConfirmedFore => Pick(0x14532D, 0xC6EFD1);
    // 미확정 표시
    public static Color DraftBack => Pick(0xFFEBCD, 0x4A3417);
    public static Color DraftFore => Pick(0x7A4A00, 0xFFDDB0);
    // 읽기전용·계산칸
    public static Color ReadOnlyBack => Pick(0xF0F0F0, 0x2B2B2E);
    public static Color ReadOnlyFore => Pick(0x4D4D4D, 0xBDBDBD);
    // 상단 막대
    public static Color BarBack => Pick(0xE6E6E6, 0x2D2D30);

    // 글자색만 바꾸는 표시(배경은 기본)
    public static Color WarnFore => Pick(0xB03A2E, 0xFF8C7C);
    public static Color LinkFore => Pick(0x1F5F8B, 0x80C2F7);
    public static Color MutedFore => Pick(0x6B6B6B, 0x9E9E9E);
    public static Color OkFore => Pick(0x1E7B45, 0x72D79C);
    public static Color TextFore => SystemColors.ControlText;

    public static void ApplyInput(DataGridViewCellStyle s) { s.BackColor = InputBack; s.ForeColor = InputFore; }
    public static void ApplyInput(Control c) { c.BackColor = InputBack; c.ForeColor = InputFore; }
    public static void ApplyConfirmed(DataGridViewCellStyle s) { s.BackColor = ConfirmedBack; s.ForeColor = ConfirmedFore; }
    public static void ApplyConfirmed(Control c) { c.BackColor = ConfirmedBack; c.ForeColor = ConfirmedFore; }
    public static void ApplyDraft(Control c) { c.BackColor = DraftBack; c.ForeColor = DraftFore; }
    public static void ApplyReadOnly(DataGridViewCellStyle s) { s.BackColor = ReadOnlyBack; s.ForeColor = ReadOnlyFore; }

    /// <summary>칠한 배경·글자색을 지우고 기본(그리드/시스템 색)으로 되돌린다.</summary>
    public static void ClearColors(DataGridViewCellStyle s) { s.BackColor = Color.Empty; s.ForeColor = Color.Empty; }
}

internal static class Ui
{

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
        if (input) Theme.ApplyInput(t);
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
