using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 견적/가격 기록 관리 화면(견적기록관리_개발기획서_확정본.md §6.1, Step 4~5 구현).
/// "견적 기준" 탭: 견적 헤더 목록/필터/편집 + 품목 라인 그리드(리비전·단가반영·승격·문서출력은
/// Step 7/9/10/14, 아직 미착수). "실적 기준" 탭(§6.2/§7.1)은 OutboundDetailTable.CskuCode
/// 백필(Step 8) 이후 구현 예정이라 지금은 안내 문구만 표시한다.
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

    // 라인 그리드(Step 5)
    private ExcelLikeDataGridView _lineGrid = new();
    private BindingList<PriceQuoteLine> _lines = [];
    private Label _totalsLabel = new();
    private Button _btnAddLine = new();
    private Button _btnRemoveLine = new();

    /// <summary>Applied 이후(Applied/Superseded/Rejected/Void)는 라인 수정을 막는다(§4.1 —
    /// "Applied 이후 라인 수정 금지. 수정은 개정 견적으로"). 개정 견적(Step 9)이 아직 없어
    /// 지금은 그냥 잠그기만 한다.</summary>
    private static readonly string[] LockedLineStatuses = ["Applied", "Superseded", "Rejected", "Void"];

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
        ScanAndPromoteScheduledQuotes();
    }

    private void InitializeComponent()
    {
        Text = "견적·단가 관리";
        Size = new Size(1180, 880);
        MinimumSize = new Size(980, 640);
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
        // 바로 주면 컨트롤의 기본(작은) 크기 기준 비율로 계산돼 나중에 실제 폭으로 늘어날 때 크게
        // 어긋난다(PersistentSplitContainer가 저장된 값에 쓰는 것과 같은 BeginInvoke 지연 필요).
        // 단, 사용자가 이미 조절해 저장해둔 값이 있으면 그걸 우선해야 하므로 여기서는 저장된 값이
        // 없을 때만 기본값을 준다(그렇지 않으면 PersistentSplitContainer의 "기억" 기능이 매번
        // 이 기본값에 덮어써져 무의미해진다).
        split.HandleCreated += (s, e) => ApplyDefaultSplitterDistance(split, "PriceQuoteForm.MainSplit", 520);
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
        _formTypeCombo.SelectedIndexChanged += (s, e) => { UpdateLineGridColumnVisibility(); RecalculateAllLines(); };

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
        _statusCombo.SelectedIndexChanged += (s, e) => UpdateLineLockState();

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
        _priceBasisCombo.SelectedIndexChanged += (s, e) => RecalculateAllLines();
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
        AddRow(form, "메모", _noteText, height: 55);

        // 헤더 폼은 필드 개수가 고정돼 있어 내용 높이가 이미 정확히 계산 가능하다(14행×27 + 메모
        // 55 = 433). 사용자가 굳이 조절할 이유가 없는 영역이라 SplitContainer 대신 고정
        // Absolute/Percent 행의 TableLayoutPanel로 나눈다 — SplitContainer의 SplitterDistance는
        // "생성 시점엔 아직 최종 크기가 아님" 문제 때문에 중첩 상황에서 타이밍이 계속 어긋났는데,
        // TableLayoutPanel의 Absolute/Percent 행 계산은 그런 타이밍 문제 자체가 없다.
        var detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 440));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailLayout.Controls.Add(form, 0, 0);
        detailLayout.Controls.Add(CreateLineSection(), 0, 1);
        outer.Controls.Add(detailLayout, 0, 0);

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

    private Control CreateLineSection()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 0) };
        toolbar.Controls.Add(new Label { Text = "품목 라인", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 6, 10, 0) });
        _btnAddLine = new Button { Text = "CSKU 선택...", Size = new Size(100, 26) };
        _btnAddLine.Click += OnAddLineFromCskuClick;
        _btnRemoveLine = new Button { Text = "행 삭제", Size = new Size(80, 26) };
        _btnRemoveLine.Click += OnRemoveLineClick;
        toolbar.Controls.Add(_btnAddLine);
        toolbar.Controls.Add(_btnRemoveLine);
        panel.Controls.Add(toolbar, 0, 0);

        panel.Controls.Add(CreateLineGrid(), 0, 1);

        _totalsLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 6, 0), Font = new Font(Font, FontStyle.Bold) };
        panel.Controls.Add(_totalsLabel, 0, 2);

        return panel;
    }

    private Control CreateLineGrid()
    {
        _lineGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PriceQuoteForm.LineGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        var moneyStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight };
        _lineGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU", DataPropertyName = "CskuCode", Width = 110, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "ItemNameSnap", HeaderText = "품명", DataPropertyName = "ItemNameSnap", Width = 160 },
            new DataGridViewTextBoxColumn { Name = "Spec", HeaderText = "규격", DataPropertyName = "Spec", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "단위", DataPropertyName = "Unit", Width = 55 },
            new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "수량", DataPropertyName = "Qty", Width = 60, DefaultCellStyle = moneyStyle },
            new DataGridViewTextBoxColumn { Name = "OldPrice", HeaderText = "직전가", DataPropertyName = "OldPrice", Width = 85, ReadOnly = true, DefaultCellStyle = moneyStyle },
            new DataGridViewTextBoxColumn { Name = "NewPrice", HeaderText = "신규가", DataPropertyName = "NewPrice", Width = 85, DefaultCellStyle = moneyStyle },
            new DataGridViewTextBoxColumn { Name = "IncreasePct", HeaderText = "증가%", Width = 65, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "SupplyAmount", HeaderText = "공급가", DataPropertyName = "SupplyAmount", Width = 90, ReadOnly = true, DefaultCellStyle = moneyStyle },
            new DataGridViewTextBoxColumn { Name = "Tax", HeaderText = "세액", DataPropertyName = "Tax", Width = 80, ReadOnly = true, DefaultCellStyle = moneyStyle },
            new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "합계", DataPropertyName = "Total", Width = 90, ReadOnly = true, DefaultCellStyle = moneyStyle },
            // 자유 텍스트로 둔다 — DataGridViewComboBoxColumn은 목록에 없는 기존 값이 들어오면
            // DataError를 던지는 게 이 코드베이스에서 이미 겪은 함정이라(ChannelConfigForm 보조소스
            // 탭 참고) 굳이 콤보로 만들지 않는다. 원가상승/환율/물동조정/신규 등을 자유 입력.
            new DataGridViewTextBoxColumn { Name = "ChangeReason", HeaderText = "사유", DataPropertyName = "ChangeReason", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "비고", DataPropertyName = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _lineGrid.CellFormatting += OnLineGridCellFormatting;
        _lineGrid.CellEndEdit += OnLineGridCellEndEdit;
        return _lineGrid;
    }

    /// <summary>
    /// PersistenceKey에 저장된 값이 없을 때만 기본 분할 위치를 적용한다. 호출 시점(HandleCreated
    /// 직후)엔 아직 TableLayoutPanel/Dock 레이아웃이 최종 크기로 정착하지 않아 SplitterDistance가
    /// 유효 범위를 벗어나 ArgumentOutOfRangeException이 날 수 있으므로, PersistentSplitContainer
    /// 자신의 재시도 로직과 동일하게 BeginInvoke로 몇 차례 다시 시도한다.
    /// </summary>
    private static void ApplyDefaultSplitterDistance(PersistentSplitContainer split, string persistenceKey, int defaultDistance, int attempt = 0)
    {
        if (attempt == 0 && new SplitterSettingsService().LoadDistance(persistenceKey) is not null) return;

        split.BeginInvoke(new Action(() =>
        {
            if (split.IsDisposed) return;
            try
            {
                split.SplitterDistance = defaultDistance;
            }
            catch (ArgumentOutOfRangeException)
            {
                if (attempt < 10) ApplyDefaultSplitterDistance(split, persistenceKey, defaultDistance, attempt + 1);
            }
        }));
    }

    private static void AddRow(TableLayoutPanel form, string label, Control control, int height = 27)
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

        // 콤보 값을 세팅하면서 발생하는 SelectedIndexChanged(양식/단가기준)가 RecalculateAllLines를
        // 트리거할 수 있으므로, 그 전에 이번 견적의 실제 라인으로 먼저 바꿔둔다.
        var lines = quote.Id != 0 ? _quoteRepository.GetQuote(quote.Id).Lines : [];
        _lines = new BindingList<PriceQuoteLine>(lines);
        _lineGrid.DataSource = _lines;

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

        UpdateLineGridColumnVisibility();
        UpdateLineLockState();
        RefreshTotals();
        _btnDelete.Enabled = quote.Id != 0;
    }

    private void OnAddLineFromCskuClick(object? sender, EventArgs e)
    {
        if (_current == null) return;
        // _current.PriceKind는 마지막 저장 시점 값이라, 저장 전에 콤보만 바꾼 상태에서는 낡은
        // 값이다 — 지금 화면에 보이는 콤보 선택값을 기준으로 판단해야 한다.
        if ((string)_priceKindCombo.SelectedItem! != "Supply")
        {
            // 매입 견적은 CSKU 개념이 없다(§6.1 — "Msku 피커 + 매입채널" 전용 조합이 필요하나
            // 아직 없음). 지금은 빈 행을 추가해 Msku/단가를 직접 입력하게 한다.
            _lines.Add(new PriceQuoteLine { Unit = "kg" });
            return;
        }
        if (_channelCombo.SelectedItem is not SalesChannel channel)
        {
            MessageBox.Show("채널을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new CskuPickerDialog(channel.ChannelCode);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedCskuCode == null) return;

        var line = new PriceQuoteLine
        {
            CskuCode = picker.SelectedCskuCode,
            Msku = picker.SelectedMsku ?? string.Empty,
            ItemNameSnap = picker.SelectedItemName ?? string.Empty,
            Unit = picker.SelectedUnit ?? "EA",
            OldPrice = picker.SelectedUnitPrice,
            NewPrice = picker.SelectedUnitPrice,
            Qty = (string)_formTypeCombo.SelectedItem! == "WithQty" ? 1 : 0,
        };
        RecalculateLine(line);
        _lines.Add(line);
        RefreshTotals();
    }

    private void OnRemoveLineClick(object? sender, EventArgs e)
    {
        var selected = _lineGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as PriceQuoteLine)
            .Where(l => l != null)
            .Cast<PriceQuoteLine>()
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("삭제할 행을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (var line in selected) _lines.Remove(line);
        RefreshTotals();
    }

    private void OnLineGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_lineGrid.Columns[e.ColumnIndex].Name != "IncreasePct") return;
        if (e.RowIndex < 0 || e.RowIndex >= _lines.Count) return;
        var line = _lines[e.RowIndex];
        if (line.OldPrice is null || line.OldPrice.Value == 0)
        {
            e.Value = string.Empty;
        }
        else
        {
            var pct = (line.NewPrice - line.OldPrice.Value) / line.OldPrice.Value * 100m;
            e.Value = $"{pct:+0.0;-0.0;0.0}%";
        }
        e.FormattingApplied = true;
    }

    private void OnLineGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _lines.Count) return;
        var columnName = _lineGrid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("Qty" or "NewPrice")) return;

        RecalculateLine(_lines[e.RowIndex]);
        _lineGrid.InvalidateRow(e.RowIndex);
        RefreshTotals();
    }

    /// <summary>양식(UnitOnly/WithQty)·단가기준(VatExcl/VatIncl)에 따라 라인의 파생값을 다시
    /// 계산한다(§5). UnitOnly는 수량/공급가/세액/합계를 전부 0으로 비운다(양식 자체가 단가만
    /// 다루므로).</summary>
    private void RecalculateLine(PriceQuoteLine line)
    {
        if ((string)_formTypeCombo.SelectedItem! != "WithQty")
        {
            line.Qty = 0;
            line.SupplyAmount = 0;
            line.Tax = 0;
            line.Total = 0;
            return;
        }

        if ((string)_priceBasisCombo.SelectedItem! == "VatIncl")
        {
            line.Total = Math.Round(line.Qty * line.NewPrice, MidpointRounding.AwayFromZero);
            line.SupplyAmount = Math.Round(line.Total / 1.1m, MidpointRounding.AwayFromZero);
            line.Tax = line.Total - line.SupplyAmount;
        }
        else
        {
            line.SupplyAmount = Math.Round(line.Qty * line.NewPrice, MidpointRounding.AwayFromZero);
            line.Tax = Math.Round(line.SupplyAmount * 0.1m, MidpointRounding.AwayFromZero);
            line.Total = line.SupplyAmount + line.Tax;
        }
    }

    private void RecalculateAllLines()
    {
        foreach (var line in _lines) RecalculateLine(line);
        _lineGrid.Refresh();
        RefreshTotals();
    }

    private void UpdateLineGridColumnVisibility()
    {
        var isWithQty = (string?)_formTypeCombo.SelectedItem == "WithQty";
        foreach (var name in new[] { "Qty", "SupplyAmount", "Tax", "Total" })
        {
            if (_lineGrid.Columns.Contains(name)) _lineGrid.Columns[name]!.Visible = isWithQty;
        }
    }

    /// <summary>Applied 이후 상태는 라인 수정을 잠근다(§4.1). 개정 견적(Step 9, 미착수)이 준비되기
    /// 전까지는 "다음 단계에서 처리 예정"이라는 안내만 하고 실제 개정 흐름은 제공하지 않는다.</summary>
    private void UpdateLineLockState()
    {
        var locked = LockedLineStatuses.Contains((string?)_statusCombo.SelectedItem);
        _lineGrid.ReadOnly = locked;
        _btnAddLine.Enabled = !locked;
        _btnRemoveLine.Enabled = !locked;
    }

    private void RefreshTotals()
    {
        if ((string?)_formTypeCombo.SelectedItem != "WithQty")
        {
            _totalsLabel.Text = string.Empty;
            return;
        }
        var supply = _lines.Sum(l => l.SupplyAmount);
        var tax = _lines.Sum(l => l.Tax);
        var total = _lines.Sum(l => l.Total);
        _totalsLabel.Text = $"공급가계 {supply:N0} · 세액계 {tax:N0} · 총합계 {total:N0}";
    }

    /// <summary>
    /// 상태 전이 규칙(§4.1)을 저장 직전에 확인한다. 전체 상태 그래프를 강제하진 않고(관리자가
    /// 실수를 바로잡을 여지를 남김), 문서가 명시적으로 요구하는 3가지만 막는다: Sent는 전달기록
    /// 필수, Scheduled는 자동반영+미래 적용일 필수, Superseded/OfsMapping Draft는 수동 전이 금지
    /// (둘 다 시스템/승격 흐름 전용 — Step 9/10 미착수).
    /// </summary>
    private bool ValidateStatusTransition(out string? error)
    {
        error = null;
        var newStatus = (string)_statusCombo.SelectedItem!;

        if (_current!.Origin == "OfsMapping" && newStatus != "Draft")
        {
            error = "자동 생성된 Draft(OFS 매핑)는 승격 기능(다음 단계, 미착수)을 거치지 않고는 상태를 바꿀 수 없습니다(§7.2).";
            return false;
        }
        if (newStatus == "Superseded")
        {
            error = "Superseded는 개정 견적을 발행할 때 시스템이 자동으로 설정합니다(다음 단계, 미착수) — 직접 선택할 수 없습니다.";
            return false;
        }
        if (newStatus == "Sent" && (string.IsNullOrWhiteSpace(_deliveryMethodCombo.Text) || _notDeliveredCheckBox.Checked))
        {
            error = "Sent 상태로 저장하려면 전달방법과 전달일시를 입력해야 합니다(§4.1) — DB에만 보관하려면 Draft로 두세요.";
            return false;
        }
        if (newStatus == "Scheduled")
        {
            if (!_autoApplyCheckBox.Checked)
            {
                error = "Scheduled 상태는 '적용일 되는 즉시 자동 반영'이 켜져 있어야 합니다(§4.1).";
                return false;
            }
            if (_effectiveFromPicker.Value.Date <= DateTime.Today)
            {
                error = "Scheduled 상태는 적용일이 미래여야 합니다. 오늘이거나 이미 지났으면 Applied로 저장하세요.";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 화면을 열 때마다(§4.1 "프로그램 기동 시") 적용일이 된 Scheduled 견적을 찾아 사용자 확인 후
    /// 일괄 Applied로 반영한다. 무통보 자동 UPDATE는 하지 않는다 — 확인 없이 조용히 바뀌면 언제
    /// 단가가 바뀌었는지 추적이 안 된다.
    /// </summary>
    private void ScanAndPromoteScheduledQuotes()
    {
        var due = _quoteRepository.GetAll(latestOnly: true)
            .Where(q => q.Status == "Scheduled" && q.AutoApply && q.EffectiveFrom is not null && q.EffectiveFrom.Value.Date <= DateTime.Today)
            .ToList();
        if (due.Count == 0) return;

        var preview = string.Join("\n", due.Take(10).Select(q => $"- {q.QuoteNo} ({q.ChannelCode}) 적용일 {q.EffectiveFrom:yyyy-MM-dd}"));
        var more = due.Count > 10 ? $"\n... 외 {due.Count - 10}건" : "";
        var confirm = MessageBox.Show(
            $"적용일이 되어 자동 반영 대상인 견적이 {due.Count}건 있습니다.\n\n{preview}{more}\n\n지금 Applied로 반영하시겠습니까?",
            "적용 대기 견적 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        foreach (var quote in due)
        {
            var (_, lines) = _quoteRepository.GetQuote(quote.Id);
            quote.Status = "Applied";
            _quoteRepository.SaveQuote(quote, lines);
        }
        RefreshList();
        _statusLabel.Text = $"적용일 도래 견적 {due.Count}건을 Applied로 반영했습니다.";
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
        if (!ValidateStatusTransition(out var transitionError))
        {
            MessageBox.Show(transitionError, "저장 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        var formType = (string)_formTypeCombo.SelectedItem!;
        if (formType == "WithQty" && _lines.Any(l => l.Qty <= 0))
        {
            MessageBox.Show("상세형(WithQty) 견적은 모든 라인의 수량을 1 이상 입력해야 합니다(D5).", "저장 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_current.PriceKind == "Supply" && _lines.Any(l => string.IsNullOrWhiteSpace(l.CskuCode)))
        {
            MessageBox.Show("납품(Supply) 견적의 모든 라인에는 CSKU가 필요합니다 — 'CSKU 선택...'으로 채워주세요(§3.2).", "저장 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _quoteRepository.SaveQuote(_current, _lines.ToList());
        RefreshList();
        _statusLabel.Text = $"'{_current.QuoteNo}' 저장 완료 (라인 {_lines.Count}건).";
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        if (_current == null || _current.Id == 0) return;

        var (_, lines) = _quoteRepository.GetQuote(_current.Id);
        // 라인의 채널+CSKU+적용기간에 해당하는 출고확정 이력이 하나라도 있으면 삭제를 막는다(D4/§4.3).
        // 개정 견적(Step 9, 미착수)이 준비되기 전까지는 이 조건에 걸리면 그냥 되돌릴 방법이 없다는
        // 뜻이므로, 안내 문구로 분명히 알린다.
        var blockedLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.CskuCode))
            .Where(l => _quoteRepository.HasOutboundHistory(_current.ChannelCode, l.CskuCode, l.CskuCode, _current.EffectiveFrom ?? DateTime.MinValue, _current.EffectiveTo))
            .ToList();
        if (blockedLines.Count > 0)
        {
            MessageBox.Show(
                $"출고 이력이 있는 라인이 {blockedLines.Count}건 있어 삭제할 수 없습니다(D4).\n" +
                "개정 견적 기능(다음 단계, 미착수)이 준비되면 그쪽으로 처리해야 합니다.",
                "삭제 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show($"'{_current.QuoteNo}' 견적을 삭제하시겠습니까? (라인 {lines.Count}건 포함)", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _quoteRepository.Delete(_current.Id);
        RefreshList();
        NewQuote();
        _statusLabel.Text = "삭제했습니다.";
    }
}
