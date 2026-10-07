using MiniERP2.UI;

namespace MiniERP2.Controls;

/// <summary>
/// 채널 수가 많아져 드롭다운이 불편해진 채널 선택 ComboBox를, 클릭(또는 F4/Alt+↓) 시 검색 가능한
/// 여러 열 팝업으로 대신 고르게 한다. ComboBox 자체(Items/DataSource/SelectedIndexChanged)는 그대로
/// 두고 드롭다운 열기만 가로채므로, 기존 화면 코드는 Attach 한 줄 외에 바뀌지 않는다.
/// </summary>
public static class ChannelPickerPopup
{
    public static void Attach(ComboBox combo, string title = "채널 선택")
    {
        var interceptor = new DropDownInterceptor(combo, title);
        if (combo.IsHandleCreated) interceptor.AssignHandle(combo.Handle);
        combo.HandleCreated += (_, _) => interceptor.AssignHandle(combo.Handle);
        combo.HandleDestroyed += (_, _) => interceptor.ReleaseHandle();
    }

    private static void ShowPicker(ComboBox combo, string title)
    {
        if (!combo.Enabled || combo.Items.Count == 0) return;

        var items = new List<(int Index, string Text)>();
        for (int i = 0; i < combo.Items.Count; i++)
            items.Add((i, combo.GetItemText(combo.Items[i]) ?? string.Empty));

        using var dialog = new PickerDialog(title, items, combo.SelectedIndex, combo);
        if (FormManager.ShowDialogSafe(dialog, combo.FindForm()) != DialogResult.OK) return;
        if (dialog.SelectedComboIndex >= 0 && dialog.SelectedComboIndex != combo.SelectedIndex)
            combo.SelectedIndex = dialog.SelectedComboIndex;
    }

    private sealed class DropDownInterceptor : NativeWindow
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        private readonly ComboBox _combo;
        private readonly string _title;

        public DropDownInterceptor(ComboBox combo, string title)
        {
            _combo = combo;
            _title = title;
        }

        protected override void WndProc(ref Message m)
        {
            bool open = m.Msg switch
            {
                WM_LBUTTONDOWN or WM_LBUTTONDBLCLK => true,
                WM_KEYDOWN => (Keys)(int)m.WParam == Keys.F4,
                WM_SYSKEYDOWN => (Keys)(int)m.WParam is Keys.Down or Keys.Up,
                _ => false,
            };
            if (!open)
            {
                base.WndProc(ref m);
                return;
            }

            if (_combo.CanFocus) _combo.Focus();
            // 메시지 처리 도중 모달을 띄우지 않도록 한 박자 늦춘다.
            _combo.BeginInvoke(() => ShowPicker(_combo, _title));
        }
    }

    private sealed class PickerDialog : Form
    {
        private readonly List<(int Index, string Text)> _allItems;
        private readonly TextBox _searchBox = new() { Dock = DockStyle.Fill, PlaceholderText = "검색 (이름 일부 또는 초성)" };
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, MultiColumn = true, IntegralHeight = false, BorderStyle = BorderStyle.None };
        private readonly Label _countLabel = new() { Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.DimGray, Padding = new Padding(6, 6, 0, 0) };

        public int SelectedComboIndex { get; private set; } = -1;

        public PickerDialog(string title, List<(int Index, string Text)> items, int currentIndex, Control anchor)
        {
            _allItems = items;
            Text = title;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            KeyPreview = true;
            Font = anchor.FindForm()?.Font ?? Font;

            var top = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(8, 6, 8, 2) };
            top.Controls.Add(_searchBox);
            top.Controls.Add(_countLabel);
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) };
            listHost.Controls.Add(_list);
            Controls.Add(listHost);
            Controls.Add(top);

            using (var g = CreateGraphics())
            {
                int widest = items.Count == 0 ? 100 : items.Max(i => (int)g.MeasureString(i.Text, _list.Font).Width);
                _list.ColumnWidth = Math.Clamp(widest + 24, 100, 320);
            }

            ApplyFilter(currentIndex);
            PlaceNear(anchor);

            _searchBox.TextChanged += (_, _) => ApplyFilter(-1);
            _searchBox.KeyDown += OnSearchKeyDown;
            _list.DoubleClick += (_, _) => Confirm();
            _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Confirm(); e.Handled = true; } };
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            Shown += (_, _) => _searchBox.Focus();
        }

        private void PlaceNear(Control anchor)
        {
            var area = Screen.FromControl(anchor).WorkingArea;
            int rows = Math.Max(1, (int)Math.Ceiling(_allItems.Count / 4.0));
            int cols = Math.Min(4, Math.Max(1, _allItems.Count));
            int width = Math.Min(area.Width - 40, Math.Max(360, cols * _list.ColumnWidth + 40));
            int height = Math.Min(area.Height - 40, Math.Max(220, Math.Min(rows, 18) * _list.ItemHeight + 110));
            Size = new Size(width, height);

            StartPosition = FormStartPosition.Manual;
            var origin = anchor.PointToScreen(new Point(0, anchor.Height));
            int x = Math.Clamp(origin.X, area.Left, area.Right - width);
            int y = origin.Y + height <= area.Bottom ? origin.Y : Math.Max(area.Top, area.Bottom - height);
            Location = new Point(x, y);
        }

        private void ApplyFilter(int preferIndex)
        {
            string q = _searchBox.Text.Trim();
            var filtered = q.Length == 0 ? _allItems : _allItems.Where(i => Matches(i.Text, q)).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var item in filtered) _list.Items.Add(new Entry(item.Index, item.Text));
            _list.EndUpdate();

            int sel = preferIndex >= 0 ? filtered.FindIndex(i => i.Index == preferIndex) : -1;
            if (sel < 0 && filtered.Count > 0) sel = 0;
            _list.SelectedIndex = sel;
            _countLabel.Text = $"{filtered.Count}/{_allItems.Count}";
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            int count = _list.Items.Count;
            if (e.KeyCode == Keys.Enter) { Confirm(); e.Handled = e.SuppressKeyPress = true; return; }
            if (count == 0) return;

            int perColumn = Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.ItemHeight));
            int delta = e.KeyCode switch
            {
                Keys.Down => 1,
                Keys.Up => -1,
                Keys.PageDown => perColumn,
                Keys.PageUp => -perColumn,
                _ => 0,
            };
            if (delta == 0) return;
            _list.SelectedIndex = Math.Clamp(Math.Max(0, _list.SelectedIndex) + delta, 0, count - 1);
            e.Handled = e.SuppressKeyPress = true;
        }

        private void Confirm()
        {
            if (_list.SelectedItem is not Entry entry) return;
            SelectedComboIndex = entry.Index;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool Matches(string text, string query)
        {
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            return query.All(IsChoseong) && ToChoseong(text).Contains(query, StringComparison.Ordinal);
        }

        private static readonly char[] Choseong =
            ['ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'];

        private static bool IsChoseong(char c) => Array.IndexOf(Choseong, c) >= 0;

        private static string ToChoseong(string text) =>
            new(text.Select(c => c is >= '가' and <= '힣' ? Choseong[(c - '가') / 588] : c).ToArray());

        private sealed record Entry(int Index, string Text)
        {
            public override string ToString() => Text;
        }
    }
}
