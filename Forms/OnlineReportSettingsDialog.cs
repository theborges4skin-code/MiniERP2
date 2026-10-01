using System.Drawing.Text;
using MiniERP2.Config;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 온라인 매출 종합보고서 설정 — 채널 블록(순서·채널·택배비 집계), 고정 품목 열, 거래처 표, 비용·수출 항목,
/// 택배비 단가, 글꼴·기준 배율. 저장하면 online_report_config.json에 쓴다.
/// </summary>
public class OnlineReportSettingsDialog : Form
{
    private readonly OnlineReportConfig _config;
    private readonly DataGridView _blockGrid = NewGrid();
    private readonly DataGridView _columnGrid = NewGrid();
    private readonly DataGridView _partnerGrid = NewGrid();
    private readonly TextBox _costItems = MultiLine();
    private readonly TextBox _exportMarkets = MultiLine();
    private readonly TextBox _ignoredCodes = new() { Dock = DockStyle.Fill };
    private readonly TextBox _ignoredPrefixes = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _fallbackNumber = Number(0, 999, 0);
    private readonly NumericUpDown _baseFee = Number(0, 100000, 0);
    private readonly NumericUpDown _threshold = Number(0, 100000, 0);
    private readonly NumericUpDown _packSmall = Number(0, 100000, 0);
    private readonly NumericUpDown _packLarge = Number(0, 100000, 0);
    private readonly NumericUpDown _fulfillment = Number(0, 100000, 0);
    private readonly NumericUpDown _baseScale = Number(10, 100, 0);
    private readonly ComboBox _font = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };

    public OnlineReportSettingsDialog(OnlineReportConfig config, IReadOnlyList<ChannelConfig> channels, IReadOnlyList<string> productGroups)
    {
        _config = config;
        Text = "온라인 매출 보고서 설정";
        Size = new Size(980, 640);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildBlockTab(channels));
        tabs.TabPages.Add(BuildColumnTab(productGroups));
        tabs.TabPages.Add(BuildPartnerTab());
        tabs.TabPages.Add(BuildEtcTab());

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "취소", Size = new Size(84, 28), DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "저장", Size = new Size(84, 28) };
        var reset = new Button { Text = "기본값으로", Size = new Size(96, 28) };
        ok.Click += (_, _) => { if (Collect()) DialogResult = DialogResult.OK; };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "설정을 기본값으로 되돌립니까? (저장을 눌러야 반영됩니다)", "기본값", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Populate(OnlineReportConfigService.CreateDefault());
        };
        buttons.Controls.AddRange([cancel, ok, reset]);
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(buttons);
        Populate(config);
    }

    public OnlineReportConfig Result => _config;

    // ── 탭 구성 ──

    private TabPage BuildBlockTab(IReadOnlyList<ChannelConfig> channels)
    {
        _blockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "블록명", Width = 130 });
        _blockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Codes", HeaderText = "채널코드(쉼표로 구분)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _blockGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Ship", HeaderText = "택배비 집계", Width = 80 });
        _blockGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Rocket", HeaderText = "로켓+그로스", Width = 80 });

        var channelList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var c in channels.OrderBy(c => c.ChannelName)) channelList.Items.Add($"{c.ChannelCode}  {c.ChannelName}");
        channelList.DoubleClick += (_, _) =>
        {
            if (channelList.SelectedItem is not string item || _blockGrid.CurrentRow is null || _blockGrid.CurrentRow.IsNewRow) return;
            var code = item.Split("  ")[0];
            var cell = _blockGrid.CurrentRow.Cells["Codes"];
            var codes = SplitList(cell.Value as string);
            if (!codes.Contains(code, StringComparer.OrdinalIgnoreCase)) codes.Add(code);
            cell.Value = string.Join(", ", codes);
        };

        var right = new Panel { Dock = DockStyle.Right, Width = 260, Padding = new Padding(6, 0, 0, 0) };
        right.Controls.Add(channelList);
        right.Controls.Add(new Label { Dock = DockStyle.Top, Height = 34, Text = "채널 목록 — 더블클릭하면 선택한 블록에 추가됩니다." });

        return Tab("채널 블록", "위에서 아래 순서대로 채널×상품그룹 페이지에 그립니다. 쿠팡로켓·그로스처럼 고객이 낸 배송비가 아닌 채널은 택배비 집계를 끕니다.",
            WithMoveButtons(_blockGrid), right);
    }

    private TabPage BuildColumnTab(IReadOnlyList<string> productGroups)
    {
        _columnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Number", HeaderText = "번호", Width = 60 });
        _columnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Header", HeaderText = "머리글", Width = 140 });
        _columnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Members", HeaderText = "함께 모을 그룹 번호(쉼표, 비우면 번호 하나)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        var groupList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var g in productGroups) groupList.Items.Add(g);
        var right = new Panel { Dock = DockStyle.Right, Width = 220, Padding = new Padding(6, 0, 0, 0) };
        right.Controls.Add(groupList);
        var fallbackRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
        fallbackRow.Controls.Add(new Label { Text = "분류 안 되는 그룹 → 번호", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        fallbackRow.Controls.Add(_fallbackNumber);
        right.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = "현재 마스터의 상품그룹" });
        right.Controls.Add(fallbackRow);

        return Tab("품목 열", "고정 품목 열입니다(왼→오 순서). 예: 프래그런스/DB 열은 번호 10, 함께 모을 그룹 8, 10.",
            WithMoveButtons(_columnGrid), right);
    }

    private TabPage BuildPartnerTab()
    {
        _partnerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "표시명", Width = 160 });
        _partnerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Keywords", HeaderText = "거래처 마감보드 거래처명에 들어 있는 글자(쉼표)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        return Tab("거래처", "요약 페이지 거래처 표의 순서입니다. 예: 이공인터 ← \"이공\" (이공그로스수동마감 + 이공이공인터내셔널 합산).",
            WithMoveButtons(_partnerGrid), null);
    }

    private TabPage BuildEtcTab()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(string label, Control control, int height = 30)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            table.Controls.Add(control);
        }

        using (var fonts = new InstalledFontCollection())
            foreach (var family in fonts.Families.Where(f => f.Name.Any(ch => ch >= '가' && ch <= '힣') || f.Name.Contains("Pretendard") || f.Name.Contains("Malgun")))
                _font.Items.Add(family.Name);

        Add("비용 항목(한 줄에 하나)", _costItems, 80);
        Add("수출 시장(한 줄에 하나)", _exportMarkets, 60);
        Add("경고하지 않을 채널코드(쉼표)", _ignoredCodes);
        Add("경고하지 않을 채널명 시작 글자(쉼표)", _ignoredPrefixes);
        Add("택배 기준단가(고객부담 배송비)", _baseFee);
        Add("부자재비 경계 운임(이하=소형)", _threshold);
        Add("부자재비 소형", _packSmall);
        Add("부자재비 대형", _packLarge);
        Add("풀필먼트 건당 비용", _fulfillment);
        Add("글꼴", _font);
        Add("채널별 페이지 기준 인쇄 배율(%)", _baseScale);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var page = new TabPage("기타");
        page.Controls.Add(table);
        return page;
    }

    // ── 값 채우기/읽기 ──

    private void Populate(OnlineReportConfig config)
    {
        _blockGrid.Rows.Clear();
        foreach (var b in config.Blocks) _blockGrid.Rows.Add(b.Name, string.Join(", ", b.ChannelCodes), b.CountShipping, b.IsRocketGrowth);
        _columnGrid.Rows.Clear();
        foreach (var c in config.GroupColumns) _columnGrid.Rows.Add(c.Number, c.Header, string.Join(", ", c.MemberNumbers));
        _partnerGrid.Rows.Clear();
        foreach (var p in config.Partners) _partnerGrid.Rows.Add(p.DisplayName, string.Join(", ", p.PartyNameKeywords));
        _costItems.Text = string.Join(Environment.NewLine, config.CostItems);
        _exportMarkets.Text = string.Join(Environment.NewLine, config.ExportMarkets);
        _ignoredCodes.Text = string.Join(", ", config.IgnoredChannelCodes);
        _ignoredPrefixes.Text = string.Join(", ", config.IgnoredChannelNamePrefixes);
        _fallbackNumber.Value = config.FallbackColumnNumber;
        _baseFee.Value = config.Freight.BaseFee;
        _threshold.Value = config.Freight.PackingThreshold;
        _packSmall.Value = config.Freight.PackingSmall;
        _packLarge.Value = config.Freight.PackingLarge;
        _fulfillment.Value = config.Freight.FulfillmentUnitCost;
        _baseScale.Value = Math.Clamp(config.BaseScalePercent, 10, 100);
        _font.Text = config.FontName;
    }

    private bool Collect()
    {
        var blocks = new List<OnlineReportBlock>();
        foreach (DataGridViewRow row in _blockGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var name = (row.Cells["Name"].Value as string)?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            blocks.Add(new OnlineReportBlock
            {
                Name = name,
                ChannelCodes = SplitList(row.Cells["Codes"].Value as string),
                CountShipping = row.Cells["Ship"].Value is true,
                IsRocketGrowth = row.Cells["Rocket"].Value is true,
            });
        }
        if (blocks.Select(b => b.Name).Distinct().Count() != blocks.Count)
        {
            MessageBox.Show(this, "블록명이 겹칩니다. 블록명은 서로 달라야 합니다.", "설정 오류");
            return false;
        }

        var columns = new List<OnlineReportGroupColumn>();
        foreach (DataGridViewRow row in _columnGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (!int.TryParse(Convert.ToString(row.Cells["Number"].Value), out var number)) continue;
            columns.Add(new OnlineReportGroupColumn
            {
                Number = number,
                Header = (row.Cells["Header"].Value as string)?.Trim() ?? number.ToString(),
                MemberNumbers = SplitList(Convert.ToString(row.Cells["Members"].Value)).Select(s => int.TryParse(s, out var n) ? n : -1).Where(n => n >= 0).ToList(),
            });
        }
        if (columns.Count == 0)
        {
            MessageBox.Show(this, "품목 열이 하나도 없습니다.", "설정 오류");
            return false;
        }
        if (columns.All(c => c.Number != (int)_fallbackNumber.Value))
        {
            MessageBox.Show(this, $"분류 안 되는 그룹이 들어갈 번호 {_fallbackNumber.Value}에 해당하는 품목 열이 없습니다.", "설정 오류");
            return false;
        }

        var partners = new List<OnlineReportPartner>();
        foreach (DataGridViewRow row in _partnerGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var name = (row.Cells["Name"].Value as string)?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            partners.Add(new OnlineReportPartner { DisplayName = name, PartyNameKeywords = SplitList(row.Cells["Keywords"].Value as string) });
        }

        _config.Blocks = blocks;
        _config.GroupColumns = columns;
        _config.Partners = partners;
        _config.FallbackColumnNumber = (int)_fallbackNumber.Value;
        _config.CostItems = Lines(_costItems.Text);
        _config.ExportMarkets = Lines(_exportMarkets.Text);
        _config.IgnoredChannelCodes = SplitList(_ignoredCodes.Text);
        _config.IgnoredChannelNamePrefixes = SplitList(_ignoredPrefixes.Text);
        _config.Freight = new OnlineReportFreightSettings
        {
            BaseFee = _baseFee.Value,
            PackingThreshold = _threshold.Value,
            PackingSmall = _packSmall.Value,
            PackingLarge = _packLarge.Value,
            FulfillmentUnitCost = _fulfillment.Value,
        };
        _config.FontName = string.IsNullOrWhiteSpace(_font.Text) ? "맑은 고딕" : _font.Text.Trim();
        _config.BaseScalePercent = (int)_baseScale.Value;
        return true;
    }

    // ── 도우미 ──

    private static List<string> SplitList(string? text) =>
        (text ?? "").Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        RowHeadersWidth = 28,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
    };

    private static TextBox MultiLine() => new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };

    private static NumericUpDown Number(decimal min, decimal max, int decimals) =>
        new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, ThousandsSeparator = true, Width = 120, TextAlign = HorizontalAlignment.Right };

    private static TabPage Tab(string title, string help, Control main, Control? right)
    {
        var page = new TabPage(title) { Padding = new Padding(6) };
        page.Controls.Add(main);
        if (right is not null) page.Controls.Add(right);
        page.Controls.Add(new Label { Dock = DockStyle.Top, Height = 34, Text = help, ForeColor = Color.DimGray });
        return page;
    }

    /// <summary>표 왼쪽에 위/아래 이동 버튼을 붙인다(순서가 곧 출력 순서).</summary>
    private static Control WithMoveButtons(DataGridView grid)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 44, FlowDirection = FlowDirection.TopDown };
        var up = new Button { Text = "▲", Size = new Size(36, 30) };
        var down = new Button { Text = "▼", Size = new Size(36, 30) };
        up.Click += (_, _) => MoveRow(grid, -1);
        down.Click += (_, _) => MoveRow(grid, +1);
        bar.Controls.AddRange([up, down]);
        panel.Controls.Add(grid);
        panel.Controls.Add(bar);
        return panel;
    }

    private static void MoveRow(DataGridView grid, int delta)
    {
        if (grid.CurrentRow is null || grid.CurrentRow.IsNewRow) return;
        var index = grid.CurrentRow.Index;
        var target = index + delta;
        var lastData = grid.Rows.Count - (grid.AllowUserToAddRows ? 2 : 1);
        if (target < 0 || target > lastData) return;
        var values = grid.Rows[index].Cells.Cast<DataGridViewCell>().Select(c => c.Value!).ToArray();
        grid.Rows.RemoveAt(index);
        grid.Rows.Insert(target, values);
        grid.CurrentCell = grid.Rows[target].Cells[grid.CurrentCell?.ColumnIndex ?? 0];
    }
}
