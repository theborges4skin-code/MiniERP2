using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 견적/가격 기록 관리 화면(견적기록관리_개발기획서_확정본.md §6.1, Step 4 — 골격만 구현).
/// "견적 기준" 탭: 견적 헤더 목록/필터/편집(라인 그리드는 Step 5, 리비전·단가반영·승격·문서출력은
/// Step 7/9/10/14). "실적 기준" 탭(§6.2/§7.1)은 OutboundDetailTable.CskuCode 백필(Step 8) 이후
/// 구현 예정이라 지금은 안내 문구만 표시한다.
/// </summary>
public class PriceQuoteForm : Form
{
    private readonly PriceQuoteRepository _quoteRepository = new();
    private readonly SalesChannelRepository _channelRepository = new();
    private List<SalesChannel> _channels = [];

    private ComboBox _filterChannelCombo = new();
    private ComboBox _filterPriceKindCombo = new();
    private ComboBox _filterStatusCombo = new();
    private CheckBox _latestOnlyCheckBox = new();
    private ExcelLikeDataGridView _listGrid = new();
    private BindingList<PriceQuote> _quotes = [];
    private Label _statusLabel = new();

    // 헤더 편집 패널
    private TextBox _quoteNoText = new();
    private ComboBox _channelCombo = new();
    private ComboBox _priceKindCombo = new();
    private ComboBox _formTypeCombo = new();
    private Label _originLabel = new();
    private TextBox _titleText = new();
    private DateTimePicker _quoteDatePicker = new();
    private DateTimePicker _effectiveFromPicker = new();
    private CheckBox _noExpiryCheckBox = new();
    private DateTimePicker _effectiveToPicker = new();
    private CheckBox _autoApplyCheckBox = new();
    private ComboBox _statusCombo = new();
    private ComboBox _deliveryMethodCombo = new();
    private CheckBox _notDeliveredCheckBox = new();
    private DateTimePicker _deliveredAtPicker = new();
    private TextBox _deliveredToText = new();
    private ComboBox _priceBasisCombo = new();
    private TextBox _noteText = new();
    private Button _btnSave = new();
    private Button _btnDelete = new();

    private PriceQuote? _current;

    private static readonly string[] PriceKinds = ["Supply", "Purchase"];
    private static readonly string[] FormTypes = ["UnitOnly", "WithQty"];
    private static readonly string[] Statuses = ["Draft", "Sent", "Scheduled", "Applied", "Superseded", "Rejected", "Void"];
    private static readonly string[] DeliveryMethods = ["메일", "카톡", "전화", "대면", "문서발송"];
    private static readonly string[] PriceBases = ["VatExcl", "VatIncl"];

    public PriceQuoteForm()
    {
        InitializeComponent();
        LoadChannels();
        RefreshList();
        NewQuote();
    }

    private void InitializeComponent()
    {
        Text = "견적·단가 관리";
        Size = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;

        var tabControl = new TabControl { Dock = DockStyle.Fill };
        tabControl.TabPages.Add(CreateQuoteBasisTab());
        tabControl.TabPages.Add(CreatePerformanceBasisTab());

        Controls.Add(tabControl);
    }

    private TabPage CreateQuoteBasisTab()
    {
        var tab = new TabPage("견적 기준");
        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        mainLayout.Controls.Add(CreateFilterBar(), 0, 0);

        var split = new PersistentSplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            PersistenceKey = "PriceQuoteForm.MainSplit",
        };
        split.Panel1.Controls.Add(CreateListGrid());
        split.Panel2.Controls.Add(CreateDetailPanel());
        // 생성 시점엔 아직 Dock=Fill로 실제 크기가 정해지기 전이라, SplitterDistance를 그 자리에서
        // 바로 주면 컨트롤의 기본(작은) 크기 기준 비율로 계산돼 나중에 실제 폭으로 늘어날 때
        // 크게 어긋난다(PersistentSplitContainer가 저장된 값에 쓰는 것과 같은 BeginInvoke 지연 필요).
        split.HandleCreated += (s, e) => split.BeginInvoke(new Action(() => split.SplitterDistance = 520));
        mainLayout.Controls.Add(split, 0, 1);

        _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
        mainLayout.Controls.Add(_statusLabel, 0, 2);

        tab.Controls.Add(mainLayout);
        return tab;
    }

    private TabPage CreatePerformanceBasisTab()
    {
        var tab = new TabPage("실적 기준");
        tab.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "실적 기준 조회(§7.1)는 아직 구현되지 않았습니다.\n" +
                   "발주/출고 이력에 CSKU가 채워진 뒤(§9 Step 8) 채널·기간별 판매 실적/단가 조회가 추가될 예정입니다.",
            ForeColor = SystemColors.GrayText,
        });
        return tab;
    }

    private Control CreateFilterBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 0, 0) };

        _filterChannelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _filterChannelCombo.SelectedIndexChanged += (s, e) => RefreshList();

        _filterPriceKindCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        _filterPriceKindCombo.Items.AddRange(["(전체)", "Supply", "Purchase"]);
        _filterPriceKindCombo.SelectedIndex = 0;
        _filterPriceKindCombo.SelectedIndexChanged += (s, e) => RefreshList();

        _filterStatusCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _filterStatusCombo.Items.Add("(전체)");
        _filterStatusCombo.Items.AddRange(Statuses);
        _filterStatusCombo.SelectedIndex = 0;
        _filterStatusCombo.SelectedIndexChanged += (s, e) => RefreshList();

        _latestOnlyCheckBox = new CheckBox { Text = "최신본만", AutoSize = true, Checked = true, Padding = new Padding(8, 4, 0, 0) };
        _latestOnlyCheckBox.CheckedChanged += (s, e) => RefreshList();

        var btnRefresh = new Button { Text = "새로고침", Size = new Size(80, 26) };
        btnRefresh.Click += (s, e) => RefreshList();

        var btnNew = new Button { Text = "새 견적", Size = new Size(80, 26) };
        btnNew.Click += (s, e) => NewQuote();

        bar.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_filterChannelCombo);
        bar.Controls.Add(new Label { Text = "구분:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        bar.Controls.Add(_filterPriceKindCombo);
        bar.Controls.Add(new Label { Text = "상태:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        bar.Controls.Add(_filterStatusCombo);
        bar.Controls.Add(_latestOnlyCheckBox);
        bar.Controls.Add(btnRefresh);
        bar.Controls.Add(btnNew);
        return bar;
    }

    private Control CreateListGrid()
    {
        _listGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PriceQuoteForm.ListGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _listGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "QuoteNo", HeaderText = "견적번호", DataPropertyName = "QuoteNo", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "QuoteDate", HeaderText = "견적일", DataPropertyName = "QuoteDate", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { Name = "EffectiveFrom", HeaderText = "적용일", DataPropertyName = "EffectiveFrom", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { Name = "ChannelCode", HeaderText = "채널", DataPropertyName = "ChannelCode", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "제목", DataPropertyName = "Title", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "PriceKind", HeaderText = "구분", DataPropertyName = "PriceKind", Width = 70 },
            new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "상태", DataPropertyName = "Status", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Origin", HeaderText = "출처", DataPropertyName = "Origin", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "RevisionNo", HeaderText = "Rev", DataPropertyName = "RevisionNo", Width = 40 }
        );
        _listGrid.SelectionChanged += OnListGridSelectionChanged;
        return _listGrid;
    }

    private Control CreateDetailPanel()
    {
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(10) };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        // AutoSize 행 + Dock=Fill 자식 조합은 이 코드베이스에서 반복적으로 컬럼/행 크기 계산이
        // 꼬였던 조합이다(FboOrderForm.cs 주석 참고). 행마다 고정 높이(Absolute)를 직접 주는
        // 방식으로 피해간다 — 필드 개수가 고정돼 있어 굳이 AutoSize가 필요하지 않다.
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _quoteNoText = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        _channelCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _channelCombo.SelectedIndexChanged += (s, e) => UpdateChannelHint();

        _priceKindCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _priceKindCombo.Items.AddRange(PriceKinds);
        _priceKindCombo.SelectedIndexChanged += (s, e) => PopulateChannelCombo();

        _formTypeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _formTypeCombo.Items.AddRange(FormTypes);

        _originLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText };
        _titleText = new TextBox { Dock = DockStyle.Fill };
        _quoteDatePicker = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };

        _effectiveFromPicker = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };
        var effectivePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        effectivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        effectivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        effectivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _noExpiryCheckBox = new CheckBox { Text = "무기한", AutoSize = true, Checked = true, Padding = new Padding(6, 4, 6, 0) };
        _noExpiryCheckBox.CheckedChanged += (s, e) => _effectiveToPicker.Enabled = !_noExpiryCheckBox.Checked;
        _effectiveToPicker = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Enabled = false };
        effectivePanel.Controls.Add(_effectiveFromPicker, 0, 0);
        effectivePanel.Controls.Add(_noExpiryCheckBox, 1, 0);
        effectivePanel.Controls.Add(_effectiveToPicker, 2, 0);

        _autoApplyCheckBox = new CheckBox { Text = "적용일 되는 즉시 자동 반영", AutoSize = true, Dock = DockStyle.Fill };
        _statusCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _statusCombo.Items.AddRange(Statuses);

        _deliveryMethodCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _deliveryMethodCombo.Items.AddRange(DeliveryMethods);

        var deliveredPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        deliveredPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        deliveredPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _notDeliveredCheckBox = new CheckBox { Text = "미전달", AutoSize = true, Checked = true, Padding = new Padding(0, 4, 6, 0) };
        _notDeliveredCheckBox.CheckedChanged += (s, e) => _deliveredAtPicker.Enabled = !_notDeliveredCheckBox.Checked;
        _deliveredAtPicker = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Enabled = false };
        deliveredPanel.Controls.Add(_notDeliveredCheckBox, 0, 0);
        deliveredPanel.Controls.Add(_deliveredAtPicker, 1, 0);

        _deliveredToText = new TextBox { Dock = DockStyle.Fill };
        _priceBasisCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _priceBasisCombo.Items.AddRange(PriceBases);
        _noteText = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };

        AddRow(form, "견적번호", _quoteNoText);
        AddRow(form, "채널", _channelCombo);
        AddRow(form, "구분", _priceKindCombo);
        AddRow(form, "견적양식", _formTypeCombo);
        AddRow(form, "출처", _originLabel);
        AddRow(form, "제목", _titleText);
        AddRow(form, "견적일", _quoteDatePicker);
        AddRow(form, "적용기간", effectivePanel);
        AddRow(form, "", _autoApplyCheckBox);
        AddRow(form, "상태", _statusCombo);
        AddRow(form, "전달방법", _deliveryMethodCombo);
        AddRow(form, "전달일시", deliveredPanel);
        AddRow(form, "전달받는사람", _deliveredToText);
        AddRow(form, "단가기준", _priceBasisCombo);
        AddRow(form, "메모", _noteText, height: 90);

        outer.Controls.Add(form, 0, 0);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        _btnSave = new Button { Text = "저장", Size = new Size(90, 30), Font = new Font(Font, FontStyle.Bold) };
        _btnSave.Click += OnSaveClick;
        _btnDelete = new Button { Text = "삭제", Size = new Size(90, 30) };
        _btnDelete.Click += OnDeleteClick;
        buttonPanel.Controls.Add(_btnSave);
        buttonPanel.Controls.Add(_btnDelete);
        outer.Controls.Add(buttonPanel, 0, 1);

        return outer;
    }

    private static void AddRow(TableLayoutPanel form, string label, Control control, int height = 32)
    {
        var row = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        if (!string.IsNullOrEmpty(label))
            form.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) }, 0, row);
        control.Margin = new Padding(3, 3, 3, 3);
        form.Controls.Add(control, 1, row);
    }

    private void LoadChannels()
    {
        _channels = _channelRepository.GetAll();

        _filterChannelCombo.Items.Clear();
        _filterChannelCombo.Items.Add("(전체)");
        foreach (var ch in _channels.Where(c => c.IsPurchase || c.IsSales).OrderBy(c => c.ChannelName))
            _filterChannelCombo.Items.Add(ch.ChannelName);
        _filterChannelCombo.SelectedIndex = 0;

        PopulateChannelCombo();
    }

    /// <summary>구분(납품/매입)에 따라 상세 패널의 채널 콤보 후보를 좁힌다 — 매입 견적에 판매전용
    /// 채널을 고르는 실수를 막기 위함(§2.1, D1).</summary>
    private void PopulateChannelCombo()
    {
        var isPurchase = _priceKindCombo.SelectedItem as string == "Purchase";
        var candidates = _channels.Where(c => isPurchase ? c.IsPurchase : c.IsSales).OrderBy(c => c.ChannelName).ToList();

        var previouslySelected = _channelCombo.SelectedItem as SalesChannel;
        _channelCombo.DataSource = candidates;
        _channelCombo.DisplayMember = nameof(SalesChannel.ChannelName);
        _channelCombo.ValueMember = nameof(SalesChannel.ChannelCode);
        if (previouslySelected != null)
        {
            var match = candidates.FirstOrDefault(c => c.ChannelCode == previouslySelected.ChannelCode);
            if (match != null) _channelCombo.SelectedItem = match;
        }
        UpdateChannelHint();
    }

    private void UpdateChannelHint()
    {
        // 채널 콤보가 비어있는(구분에 맞는 채널이 하나도 없는) 경우를 조용히 넘어가지 않도록 안내.
        if (_channelCombo.Items.Count == 0)
            _statusLabel.Text = "선택한 구분(납품/매입)에 해당하는 채널이 없습니다 — 채널설정에서 먼저 지정하세요.";
    }

    private void RefreshList()
    {
        string? channelCode = null;
        if (_filterChannelCombo.SelectedIndex > 0)
        {
            var name = (string)_filterChannelCombo.SelectedItem!;
            channelCode = _channels.FirstOrDefault(c => c.ChannelName == name)?.ChannelCode;
        }
        var priceKind = _filterPriceKindCombo.SelectedIndex > 0 ? (string)_filterPriceKindCombo.SelectedItem! : null;
        var status = _filterStatusCombo.SelectedIndex > 0 ? (string)_filterStatusCombo.SelectedItem! : null;

        var quotes = _quoteRepository.GetAll(channelCode, priceKind, _latestOnlyCheckBox.Checked);
        if (status != null) quotes = quotes.Where(q => q.Status == status).ToList();

        _quotes = new BindingList<PriceQuote>(quotes);
        _listGrid.DataSource = _quotes;
        _statusLabel.Text = $"견적 {quotes.Count}건 조회됨.";
    }

    private void OnListGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_listGrid.CurrentRow?.DataBoundItem is not PriceQuote quote) return;
        LoadIntoDetailPanel(quote);
    }

    private void NewQuote()
    {
        var quote = new PriceQuote
        {
            QuoteNo = _quoteRepository.GenerateNextQuoteNo(DateTime.Today),
            PriceKind = "Supply",
            QuoteFormType = "UnitOnly",
            Origin = "Manual",
            QuoteDate = DateTime.Today,
            EffectiveFrom = DateTime.Today,
            Status = "Draft",
            PriceBasis = "VatExcl",
        };
        LoadIntoDetailPanel(quote);
    }

    private void LoadIntoDetailPanel(PriceQuote quote)
    {
        _current = quote;

        _quoteNoText.Text = quote.QuoteNo;
        _priceKindCombo.SelectedItem = quote.PriceKind;
        PopulateChannelCombo();
        if (!string.IsNullOrEmpty(quote.ChannelCode))
        {
            var match = _channels.FirstOrDefault(c => c.ChannelCode == quote.ChannelCode);
            if (match != null) _channelCombo.SelectedItem = match;
        }
        _formTypeCombo.SelectedItem = quote.QuoteFormType;
        _originLabel.Text = quote.Origin switch
        {
            "OfsMapping" => "자동(OFS 매핑)",
            "Import" => "가져오기",
            _ => "수동",
        };
        _titleText.Text = quote.Title;
        _quoteDatePicker.Value = quote.QuoteDate ?? DateTime.Today;
        _effectiveFromPicker.Value = quote.EffectiveFrom ?? DateTime.Today;
        _noExpiryCheckBox.Checked = quote.EffectiveTo is null;
        _effectiveToPicker.Value = quote.EffectiveTo ?? DateTime.Today;
        _autoApplyCheckBox.Checked = quote.AutoApply;
        _statusCombo.SelectedItem = quote.Status;
        _deliveryMethodCombo.Text = quote.DeliveryMethod;
        _notDeliveredCheckBox.Checked = quote.DeliveredAt is null;
        _deliveredAtPicker.Value = quote.DeliveredAt ?? DateTime.Now;
        _deliveredToText.Text = quote.DeliveredTo;
        _priceBasisCombo.SelectedItem = quote.PriceBasis;
        _noteText.Text = quote.Note;

        _btnDelete.Enabled = quote.Id != 0;
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (_current == null) return;
        if (_channelCombo.SelectedItem is not SalesChannel channel)
        {
            MessageBox.Show("채널을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(_titleText.Text))
        {
            MessageBox.Show("제목을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _current.QuoteNo = _quoteNoText.Text.Trim();
        _current.ChannelCode = channel.ChannelCode;
        _current.PriceKind = (string)_priceKindCombo.SelectedItem!;
        _current.QuoteFormType = (string)_formTypeCombo.SelectedItem!;
        _current.Title = _titleText.Text.Trim();
        _current.QuoteDate = _quoteDatePicker.Value.Date;
        _current.EffectiveFrom = _effectiveFromPicker.Value.Date;
        _current.EffectiveTo = _noExpiryCheckBox.Checked ? null : _effectiveToPicker.Value.Date;
        _current.AutoApply = _autoApplyCheckBox.Checked;
        _current.Status = (string)_statusCombo.SelectedItem!;
        _current.DeliveryMethod = _deliveryMethodCombo.Text.Trim();
        _current.DeliveredAt = _notDeliveredCheckBox.Checked ? null : _deliveredAtPicker.Value;
        _current.DeliveredTo = _deliveredToText.Text.Trim();
        _current.PriceBasis = (string)_priceBasisCombo.SelectedItem!;
        _current.Note = _noteText.Text.Trim();

        // Step 5(라인 그리드) 전까지는 라인 없이 헤더만 저장한다 — 기존 라인이 있는 견적을 다시
        // 저장하면 지금은 라인이 전부 사라지므로, 라인이 이미 있는 견적의 저장은 막는다.
        var (_, existingLines) = _current.Id != 0 ? _quoteRepository.GetQuote(_current.Id) : (null, []);
        if (existingLines.Count > 0)
        {
            MessageBox.Show(
                "이 견적에는 이미 품목 라인이 있습니다. 라인 편집 화면(다음 단계)이 준비되기 전까지는 헤더만 저장하면 라인이 사라지므로 저장을 막았습니다.",
                "저장 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _quoteRepository.SaveQuote(_current, []);
        RefreshList();
        _statusLabel.Text = $"'{_current.QuoteNo}' 저장 완료.";
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        if (_current == null || _current.Id == 0) return;

        var (_, lines) = _quoteRepository.GetQuote(_current.Id);
        if (lines.Count > 0)
        {
            MessageBox.Show("품목 라인이 있는 견적은 라인 편집 화면(다음 단계)에서 출고 이력을 확인한 뒤 삭제해야 합니다.", "삭제 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show($"'{_current.QuoteNo}' 견적을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _quoteRepository.Delete(_current.Id);
        RefreshList();
        NewQuote();
        _statusLabel.Text = "삭제했습니다.";
    }
}
