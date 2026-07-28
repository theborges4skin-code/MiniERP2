using System.Runtime.InteropServices;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 거래명세표(VAT별도/포함)·견적서(기본/수량포함)·가격조정 공문을
/// 작성하고 XLSX로 저장하는 통합 문서 관리 폼.
/// </summary>
public class DocsForm : Form
{
    private readonly DocPartyRepository _partyRepo = new();
    private readonly DocFavoritePhraseRepository _phraseRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly SettingsService _settingsService = new();
    private readonly DocHistoryRepository _historyRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();

    /// <summary>공급받는자를 채널에서 불러왔을 때, 어느 채널인지 기억해 CSKU 불러오기 필터 기본값으로 쓴다.</summary>
    private string? _currentBuyerChannelCode;

    // ── 현재 문서 유형 ────────────────────────────────────────────────
    private DocType _currentType = DocType.TradeStatementVatExcl;

    // ── 툴바 컨트롤 ──────────────────────────────────────────────────
    private ComboBox _docTypeCombo = new();
    private Label _statusLabel = new();

    // ── 공급자 필드 (전 유형 공통) ────────────────────────────────────
    private TextBox _supRegNo = new(), _supCompany = new(), _supCeo = new();
    private TextBox _supAddress = new(), _supBizType = new(), _supBizItem = new();
    private TextBox _supTel = new(), _supEmail = new();

    // ── 우측 패널 컨테이너 ───────────────────────────────────────────
    private Panel _rightContainer = new();
    private Panel _tradeStmtBuyerPanel = new();  // 거래명세표 공급받는자
    private Panel _quoteRightPanel = new();       // 견적서 수신/인사문
    private Panel _priceAdjRightPanel = new();    // 가격조정 수신/본문

    // ── 거래명세표 전용 필드 ──────────────────────────────────────────
    private TextBox _buyRegNo = new(), _buyCompany = new(), _buyCeo = new();
    private TextBox _buyAddress = new(), _buyBizType = new(), _buyBizItem = new();
    private TextBox _buyTel = new(), _buyEmail = new();

    // ── 거래명세표 추가 필드 ──────────────────────────────────────────
    private TextBox _taxEmailBox = new(), _unpaidBox = new(), _receiverBox = new();
    private Panel _tradeStmtExtrasPanel = new();

    // ── 매출장(내부용) 전용 필드 ────────────────────────────────────────
    private Panel _gridExtrasContainer = new();
    private Panel _salesLedgerExtrasPanel = new();
    private CheckBox _showCostCheck = new(), _showProfitCheck = new();

    // ── 견적서 전용 필드 ──────────────────────────────────────────────
    private TextBox _quoteRecipient = new(), _quoteTitle = new();
    private TextBox _quoteHeaderText = new(), _quotePriceBasis = new();

    // ── 가격조정 전용 필드 ────────────────────────────────────────────
    private TextBox _adjRecipient = new(), _adjDocNo = new();
    private TextBox _adjTitle = new(), _adjPriceBasis = new();
    private DateTimePicker _adjApplyDatePicker = new();
    private TextBox _adjBodyText = new();

    // ── 공통 필드 ────────────────────────────────────────────────────
    private DateTimePicker _issueDatePicker = new();
    private TextBox _footerNoteBox = new();
    private Label _stampLabel = new();
    private string? _stampImagePath;

    // ── 명세 그리드 ───────────────────────────────────────────────────
    private DataGridView _lineGrid = new();

    // ── 콤보박스 항목 인덱스 → DocType 매핑 ───────────────────────────
    private static readonly DocType[] TypeMap =
    {
        DocType.TradeStatementVatExcl,
        DocType.TradeStatementVatIncl,
        DocType.QuoteBasic,
        DocType.QuoteWithQty,
        DocType.PriceAdjustment,
        DocType.SalesLedger
    };

    public DocsForm()
    {
        InitializeComponent();
        LoadDefaultSupplier();
        _lineGrid.CellValueChanged += OnGridCellValueChanged;
    }

    // ══════════════════════════════════════════════════════════════════
    //  레이아웃 구성
    // ══════════════════════════════════════════════════════════════════

    private void InitializeComponent()
    {
        Text = "문서 관리";
        Size = new Size(1100, 860);
        MinimumSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterParent;

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));   // 툴바
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 185));  // 거래처 블록
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 명세 그리드
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));  // 하단 공통 필드

        main.Controls.Add(BuildToolbar(), 0, 0);
        main.Controls.Add(BuildPartyArea(), 0, 1);
        main.Controls.Add(BuildGridPanel(), 0, 2);
        main.Controls.Add(BuildCommonFooter(), 0, 3);
        Controls.Add(main);

        FormClosing += (s, e) => _lineGrid.EndEdit();
    }

    // ── 툴바 ─────────────────────────────────────────────────────────
    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 6, 0) };

        var lbl = new Label { Text = "문서 유형:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) };
        _docTypeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _docTypeCombo.Items.AddRange(new[]
        {
            "거래명세표 (VAT 별도)",
            "거래명세표 (VAT 포함)",
            "견적서 (기본)",
            "견적서 (수량포함)",
            "가격조정에 관한 건",
            "매출장 (내부용)"
        });
        _docTypeCombo.SelectedIndex = 0;
        _docTypeCombo.SelectedIndexChanged += OnDocTypeChanged;

        var btnGen   = Btn("엑셀 생성 및 저장", 140, OnGenerateClick);
        var btnPdf   = Btn("PDF 출력", 80, OnPrintPdfClick);
        var btnHistory = Btn("이력 조회", 80, OnHistoryClick);
        var btnSupLoad = Btn("공급자 불러오기", 120, (s, e) => LoadPartyFromDb(supplier: true));
        var btnSupSave = Btn("공급자 저장", 90, (s, e) => SavePartyToDb(supplier: true));
        var btnBuyLoad = Btn("거래처 불러오기", 120, (s, e) => LoadPartyFromDb(supplier: false));
        var btnBuySave = Btn("거래처 저장", 90, (s, e) => SavePartyToDb(supplier: false));
        var btnPartyManage = Btn("거래처 관리", 90, OnManagePartiesClick);
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };

        bar.Controls.AddRange(new Control[]
        {
            lbl, _docTypeCombo, Gap(16), btnGen, btnPdf, btnHistory, Gap(12),
            btnSupLoad, btnSupSave, Gap(8), btnBuyLoad, btnBuySave, Gap(8), btnPartyManage,
            _statusLabel
        });
        return bar;
    }

    // ── 거래처 영역 (공급자 고정 + 우측 가변) ──────────────────────────
    private Control BuildPartyArea()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(6, 4, 6, 2)
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        outer.Controls.Add(BuildSupplierPanel(), 0, 0);
        outer.Controls.Add(BuildRightContainer(), 1, 0);
        return outer;
    }

    private Control BuildSupplierPanel()
    {
        var group = new GroupBox { Text = "공  급  자", Dock = DockStyle.Fill };
        var tbl = MakePartyTable();

        _supRegNo = TB(); _supRegNo.PlaceholderText = "000-00-00000";
        _supCompany = TB(); _supCeo = TB(); _supAddress = TB();
        _supBizType = TB(); _supBizItem = TB();
        _supTel = TB(); _supTel.PlaceholderText = "02-0000-0000";
        _supEmail = TB();

        PartyRows(tbl, _supRegNo, _supCompany, _supCeo, _supAddress, _supBizType, _supBizItem, _supTel, _supEmail);
        group.Controls.Add(tbl);
        return group;
    }

    private Control BuildRightContainer()
    {
        _rightContainer = new Panel { Dock = DockStyle.Fill };

        // 거래명세표 공급받는자 패널
        _tradeStmtBuyerPanel = BuildBuyerPanel();
        _tradeStmtBuyerPanel.Dock = DockStyle.Fill;

        // 견적서 우측 패널
        _quoteRightPanel = BuildQuoteRightPanel();
        _quoteRightPanel.Dock = DockStyle.Fill;

        // 가격조정 우측 패널
        _priceAdjRightPanel = BuildPriceAdjRightPanel();
        _priceAdjRightPanel.Dock = DockStyle.Fill;

        _rightContainer.Controls.Add(_tradeStmtBuyerPanel);
        _rightContainer.Controls.Add(_quoteRightPanel);
        _rightContainer.Controls.Add(_priceAdjRightPanel);

        _quoteRightPanel.Visible = false;
        _priceAdjRightPanel.Visible = false;

        return _rightContainer;
    }

    private Panel BuildBuyerPanel()
    {
        var group = new GroupBox { Text = "공급받는자", Dock = DockStyle.Fill };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var channelBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnChannelLoad = Btn("채널에서 불러오기", 120, OnLoadPartyFromChannelClick);
        channelBar.Controls.Add(btnChannelLoad);

        var tbl = MakePartyTable();

        _buyRegNo = TB(); _buyRegNo.PlaceholderText = "000-00-00000";
        _buyCompany = TB(); _buyCeo = TB(); _buyAddress = TB();
        _buyBizType = TB(); _buyBizItem = TB();
        _buyTel = TB(); _buyEmail = TB();

        PartyRows(tbl, _buyRegNo, _buyCompany, _buyCeo, _buyAddress, _buyBizType, _buyBizItem, _buyTel, _buyEmail);
        inner.Controls.Add(channelBar, 0, 0);
        inner.Controls.Add(tbl, 0, 1);
        group.Controls.Add(inner);

        var wrapper = new Panel { Dock = DockStyle.Fill };
        group.Dock = DockStyle.Fill;
        wrapper.Controls.Add(group);
        return wrapper;
    }

    private Panel BuildQuoteRightPanel()
    {
        var group = new GroupBox { Text = "수신 및 문서 정보", Dock = DockStyle.Fill };
        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(4)
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _quoteRecipient = TB(); _quoteRecipient.PlaceholderText = "수신처명";
        _quoteTitle = TB(); _quoteTitle.Text = "견 적 서";
        _quotePriceBasis = TB(); _quotePriceBasis.Text = "VAT 포함";
        _quoteHeaderText = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "인사문 — 한 줄에 하나씩 입력(생략 시 기본 문구 사용)"
        };

        tbl.Controls.Add(Lbl("수신"), 0, 0);       tbl.Controls.Add(_quoteRecipient, 1, 0);
        tbl.Controls.Add(Lbl("문서제목"), 2, 0);    tbl.Controls.Add(_quoteTitle, 3, 0);
        tbl.Controls.Add(Lbl("가격기준"), 0, 1);    tbl.Controls.Add(_quotePriceBasis, 1, 1);
        tbl.Controls.Add(Lbl("인사문"), 0, 2);      tbl.Controls.Add(_quoteHeaderText, 1, 2);
        tbl.SetColumnSpan(_quoteHeaderText, 3);

        group.Controls.Add(tbl);
        var wrapper = new Panel { Dock = DockStyle.Fill };
        group.Dock = DockStyle.Fill;
        wrapper.Controls.Add(group);
        return wrapper;
    }

    private Panel BuildPriceAdjRightPanel()
    {
        var group = new GroupBox { Text = "수신 및 공문 정보", Dock = DockStyle.Fill };
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4)
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 상단: 수신/문서번호/제목/가격기준/적용일자
        var meta = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++) meta.RowStyles.Add(new RowStyle(SizeType.Percent, 33));

        _adjRecipient = TB(); _adjDocNo = TB();
        _adjTitle = TB(); _adjTitle.Text = "제품 단가 조정에 관한 건";
        _adjPriceBasis = TB(); _adjPriceBasis.Text = "VAT 포함";
        _adjApplyDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Dock = DockStyle.Fill };

        meta.Controls.Add(Lbl("수신"), 0, 0);      meta.Controls.Add(_adjRecipient, 1, 0);
        meta.Controls.Add(Lbl("문서번호"), 2, 0);   meta.Controls.Add(_adjDocNo, 3, 0);
        meta.Controls.Add(Lbl("제목"), 0, 1);       meta.Controls.Add(_adjTitle, 1, 1);
        meta.Controls.Add(Lbl("가격기준"), 2, 1);   meta.Controls.Add(_adjPriceBasis, 3, 1);
        meta.Controls.Add(Lbl("적용일자"), 0, 2);   meta.Controls.Add(_adjApplyDatePicker, 1, 2);
        meta.SetColumnSpan(_adjApplyDatePicker, 3);

        // 하단: 상단 안내문(본문) + 즐겨찾기 버튼
        var bodyArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bodyArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        bodyArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var btnBar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var lblBody = new Label { Text = "상단 안내문:", AutoSize = true, Padding = new Padding(0, 3, 6, 0) };
        var btnPhrase = Btn("즐겨찾기 문구", 110, OnSelectPhraseClick);
        var btnSavePhrase = Btn("문구로 저장", 100, OnSavePhraseClick);
        btnBar.Controls.AddRange(new Control[] { lblBody, btnPhrase, btnSavePhrase });

        _adjBodyText = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "상단 안내문을 직접 입력하거나 즐겨찾기에서 선택하세요." };
        bodyArea.Controls.Add(btnBar, 0, 0);
        bodyArea.Controls.Add(_adjBodyText, 0, 1);

        outer.Controls.Add(meta, 0, 0);
        outer.Controls.Add(bodyArea, 0, 1);

        group.Controls.Add(outer);
        var wrapper = new Panel { Dock = DockStyle.Fill };
        group.Dock = DockStyle.Fill;
        wrapper.Controls.Add(group);
        return wrapper;
    }

    // ── 명세 그리드 ───────────────────────────────────────────────────
    private Control BuildGridPanel()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(6, 2, 6, 0)
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 그리드 툴바 (CSKU 불러오기)
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // 거래명세표 extras
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 그리드

        outer.Controls.Add(BuildGridToolbar(), 0, 0);
        outer.Controls.Add(BuildGridExtrasContainer(), 0, 1);
        outer.Controls.Add(BuildGrid(), 0, 2);
        return outer;
    }

    private Control BuildGridExtrasContainer()
    {
        _gridExtrasContainer = new Panel { Dock = DockStyle.Fill };
        var tradeExtras = BuildTradeStmtExtras();
        var ledgerExtras = BuildSalesLedgerExtras();
        _gridExtrasContainer.Controls.Add(tradeExtras);
        _gridExtrasContainer.Controls.Add(ledgerExtras);
        _salesLedgerExtrasPanel.Visible = false;
        return _gridExtrasContainer;
    }

    private Control BuildGridToolbar()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnCsku = Btn("CSKU 불러오기", 110, OnLoadCskuClick);
        var btnOutboundHistory = Btn("출고이력 불러오기", 130, OnLoadOutboundHistoryClick);
        var btnApplyPriceAdj = Btn("납품가 반영", 100, OnApplyPriceAdjustmentClick);
        flow.Controls.Add(btnCsku);
        flow.Controls.Add(btnOutboundHistory);
        flow.Controls.Add(btnApplyPriceAdj);
        return flow;
    }

    private Control BuildTradeStmtExtras()
    {
        _tradeStmtExtrasPanel = new Panel { Dock = DockStyle.Fill };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };

        _taxEmailBox = TB(); _taxEmailBox.Width = 220; _taxEmailBox.PlaceholderText = "전자세금계산서 발행 이메일";
        _unpaidBox = TB(); _unpaidBox.Width = 120; _unpaidBox.PlaceholderText = "미수금";
        _receiverBox = TB(); _receiverBox.Width = 100; _receiverBox.PlaceholderText = "인수자";

        flow.Controls.AddRange(new Control[]
        {
            Lbl("세금계산서 이메일"), _taxEmailBox, Gap(12),
            Lbl("미수금"), _unpaidBox, Gap(8),
            Lbl("인수자"), _receiverBox
        });
        _tradeStmtExtrasPanel.Controls.Add(flow);
        return _tradeStmtExtrasPanel;
    }

    private Control BuildSalesLedgerExtras()
    {
        _salesLedgerExtrasPanel = new Panel { Dock = DockStyle.Fill };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };

        _showCostCheck = new CheckBox { Text = "제조원가 열 표시", Checked = true, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        _showProfitCheck = new CheckBox { Text = "이익액 열 표시", Checked = true, AutoSize = true, Padding = new Padding(16, 6, 0, 0) };
        _showCostCheck.CheckedChanged += (s, e) => { if (_lineGrid.Columns.Contains("CostPrice")) _lineGrid.Columns["CostPrice"]!.Visible = _showCostCheck.Checked; };
        _showProfitCheck.CheckedChanged += (s, e) => { if (_lineGrid.Columns.Contains("ProfitAmount")) _lineGrid.Columns["ProfitAmount"]!.Visible = _showProfitCheck.Checked; };

        flow.Controls.AddRange(new Control[] { _showCostCheck, _showProfitCheck });
        _salesLedgerExtrasPanel.Controls.Add(flow);
        return _salesLedgerExtrasPanel;
    }

    private Control BuildGrid()
    {
        _lineGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        RebuildGridColumns(DocType.TradeStatementVatExcl);
        return _lineGrid;
    }

    // ── 하단 공통 필드 ────────────────────────────────────────────────
    private Control BuildCommonFooter()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            Padding = new Padding(6, 2, 6, 6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _issueDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Dock = DockStyle.Fill };
        _stampLabel = new Label { Text = "(도장 없음)", AutoSize = true, Padding = new Padding(4, 5, 0, 0) };
        _footerNoteBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "하단 비고" };

        var stampBtn = Btn("도장 업로드", 100, OnStampClick);
        var stampRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        stampRow.Controls.AddRange(new Control[] { stampBtn, _stampLabel });

        panel.Controls.Add(RowCtrl("발행일", _issueDatePicker), 0, 0);
        panel.Controls.Add(stampRow, 1, 0);

        var noteRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        noteRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        noteRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        noteRow.Controls.Add(Lbl("비고"), 0, 0);
        noteRow.Controls.Add(_footerNoteBox, 1, 0);
        panel.Controls.Add(noteRow, 0, 2);
        panel.SetColumnSpan(noteRow, 2);

        return panel;
    }

    // ══════════════════════════════════════════════════════════════════
    //  유형 전환
    // ══════════════════════════════════════════════════════════════════

    private void OnDocTypeChanged(object? sender, EventArgs e)
    {
        int idx = _docTypeCombo.SelectedIndex;
        if (idx < 0 || idx >= TypeMap.Length) return;

        _currentType = TypeMap[idx];
        bool isTrade  = _currentType is DocType.TradeStatementVatExcl or DocType.TradeStatementVatIncl;
        bool isQuote  = _currentType is DocType.QuoteBasic or DocType.QuoteWithQty;
        bool isAdj    = _currentType == DocType.PriceAdjustment;
        bool isLedger = _currentType == DocType.SalesLedger;

        _tradeStmtBuyerPanel.Visible = isTrade || isLedger;
        _quoteRightPanel.Visible = isQuote;
        _priceAdjRightPanel.Visible = isAdj;
        _tradeStmtExtrasPanel.Visible = isTrade;
        _salesLedgerExtrasPanel.Visible = isLedger;

        // 거래처 버튼 라벨은 유형 불문 표시하되 거래명세표에서만 실질적으로 의미
        _lineGrid.EndEdit();
        RebuildGridColumns(_currentType);
        _statusLabel.Text = "";

        // 견적서는 "비고" 칸이 줄마다 하나씩(비고1/비고2/...) "2. 기타" 항목으로 출력되므로 안내를 바꿔준다.
        _footerNoteBox.PlaceholderText = isQuote
            ? "기타 안내사항 — 한 줄에 하나씩 입력하세요(최대 10줄, 각 줄이 비고1/비고2/...로 출력됨)"
            : "하단 비고";
    }

    private void RebuildGridColumns(DocType type)
    {
        _lineGrid.Columns.Clear();

        switch (type)
        {
            case DocType.TradeStatementVatExcl:
            case DocType.TradeStatementVatIncl:
                _lineGrid.Columns.AddRange(
                    GCol("Year",         "연",      40),
                    GCol("Month",        "월",      35),
                    GCol("Day",          "일",      35),
                    GColFill("ItemName", "품목",   160),
                    GCol("Spec",         "규격",    90),
                    GCol("Qty",          "수량",    65),
                    GCol("UnitPrice",    "단가",   100),
                    GColRO("SupplyAmount", "공급가액", 100),
                    GCol("Note",         "비고",   120)
                );
                break;

            case DocType.QuoteBasic:
                _lineGrid.Columns.AddRange(
                    GColFill("ItemName",  "품목",   200),
                    GCol("Unit",          "단위",    70),
                    GCol("Packing",       "패킹",   100),
                    GCol("UnitPrice",     "단가",   110),
                    GCol("Note",          "비고",   160)
                );
                break;

            case DocType.QuoteWithQty:
                _lineGrid.Columns.AddRange(
                    GColFill("ItemName",  "품목",   180),
                    GCol("Unit",          "단위",    60),
                    GCol("Packing",       "패킹",    90),
                    GCol("UnitPrice",     "단가",   100),
                    GCol("Qty",           "수량",    65),
                    GColRO("Amount",      "금액",   100),
                    GCol("Note",          "비고",   120)
                );
                break;

            case DocType.PriceAdjustment:
                _lineGrid.Columns.AddRange(
                    GCol("No",            "번호",    50),
                    GColFill("ItemName",  "품명",   180),
                    GCol("Unit",          "단위",    65),
                    GCol("PriceBefore",   "조정 전", 100),
                    GCol("PriceAfter",    "조정 후", 100),
                    GCol("Note",          "비고",   130),
                    // "CSKU 불러오기"로 채운 줄만 값이 있다 — "납품가 반영" 버튼이 이 값으로 실제
                    // CSKU를 찾는다. 화면에는 안 보이지만 그리드에 함께 저장/이동되는 값이라 열로 둔다.
                    new DataGridViewTextBoxColumn { Name = "ChannelCode", Visible = false },
                    new DataGridViewTextBoxColumn { Name = "CskuCode", Visible = false }
                );
                break;

            case DocType.SalesLedger:
                _lineGrid.Columns.AddRange(
                    GCol("Year",         "연",      40),
                    GCol("Month",        "월",      35),
                    GCol("Day",          "일",      35),
                    GColFill("ItemName", "품목",   140),
                    GCol("Spec",         "규격",    80),
                    GCol("Qty",          "수량",    60),
                    GCol("UnitPrice",    "단가",    95),
                    GColRO("SupplyAmount", "공급가액", 95),
                    GCol("CostPrice",    "제조원가",  90),
                    GColRO("ProfitAmount", "이익액",  90),
                    GCol("Note",         "비고",   110)
                );
                _lineGrid.Columns["CostPrice"]!.Visible = _showCostCheck.Checked;
                _lineGrid.Columns["ProfitAmount"]!.Visible = _showProfitCheck.Checked;
                break;
        }
    }

    private static DataGridViewTextBoxColumn GCol(string name, string header, int width) =>
        new() { Name = name, HeaderText = header, Width = width };

    private static DataGridViewTextBoxColumn GColFill(string name, string header, int width) =>
        new() { Name = name, HeaderText = header, Width = width, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };

    private static DataGridViewTextBoxColumn GColRO(string name, string header, int width) =>
        new() { Name = name, HeaderText = header, Width = width, ReadOnly = true };

    // ══════════════════════════════════════════════════════════════════
    //  자동 계산 (그리드 셀 변경)
    // ══════════════════════════════════════════════════════════════════

    private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _lineGrid.Rows[e.RowIndex];

        if (_currentType is DocType.TradeStatementVatExcl or DocType.TradeStatementVatIncl)
        {
            if (decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out var q) &&
                decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(), out var p))
            {
                var v = Math.Round(q * p, 0, MidpointRounding.AwayFromZero);
                row.Cells["SupplyAmount"].Value = v == 0 ? "" : (object)v;
            }
            else row.Cells["SupplyAmount"].Value = "";
        }
        else if (_currentType == DocType.QuoteWithQty)
        {
            if (decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out var q) &&
                decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(), out var p))
            {
                var v = Math.Round(q * p, 0, MidpointRounding.AwayFromZero);
                row.Cells["Amount"].Value = v == 0 ? "" : (object)v;
            }
            else row.Cells["Amount"].Value = "";
        }
        else if (_currentType == DocType.SalesLedger)
        {
            decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out var q);
            decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(), out var p);
            decimal.TryParse(row.Cells["CostPrice"].Value?.ToString(), out var c);
            var supply = Math.Round(q * p, 0, MidpointRounding.AwayFromZero);
            row.Cells["SupplyAmount"].Value = supply == 0 ? "" : (object)supply;
            var profit = supply - Math.Round(q * c, 0, MidpointRounding.AwayFromZero);
            row.Cells["ProfitAmount"].Value = (q == 0 && p == 0 && c == 0) ? "" : (object)profit;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  엑셀 생성
    // ══════════════════════════════════════════════════════════════════

    private void OnGenerateClick(object? sender, EventArgs e)
    {
        _lineGrid.EndEdit();

        string defaultName = DefaultFileName();
        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = defaultName,
            InitialDirectory = _settingsService.GetLastFolder("DocsExport")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("DocsExport", Path.GetDirectoryName(sfd.FileName)!);

        try
        {
            switch (_currentType)
            {
                case DocType.TradeStatementVatExcl:
                case DocType.TradeStatementVatIncl:
                    var tsDoc = BuildTradeStatementDoc();
                    if (tsDoc == null) return;
                    DocumentExporter.ExportTradeStatement(tsDoc, sfd.FileName);
                    break;

                case DocType.QuoteBasic:
                case DocType.QuoteWithQty:
                    var qDoc = BuildQuoteDoc();
                    if (qDoc == null) return;
                    DocumentExporter.ExportQuote(qDoc, sfd.FileName);
                    break;

                case DocType.PriceAdjustment:
                    var paDoc = BuildPriceAdjDoc();
                    if (paDoc == null) return;
                    DocumentExporter.ExportPriceAdjustment(paDoc, sfd.FileName);
                    break;

                case DocType.SalesLedger:
                    var slDoc = BuildSalesLedgerDoc();
                    if (slDoc == null) return;
                    DocumentExporter.ExportSalesLedger(slDoc, sfd.FileName);
                    break;
            }

            SetStatus($"저장 완료: {Path.GetFileName(sfd.FileName)}", true);
            SaveHistory(sfd.FileName);
            ExportHelper.ShowPostExportDialog(this, sfd.FileName);
        }
        catch (Exception ex)
        {
            SetStatus($"저장 실패: {ExportHelper.DescribeSaveError(ex)}", false);
        }
    }

    // ── 거래명세표 문서 빌드 ──────────────────────────────────────────
    private TradeStatementDoc? BuildTradeStatementDoc()
    {
        var doc = new TradeStatementDoc
        {
            DocType    = _currentType,
            Supplier   = ReadSupplier(),
            Buyer      = ReadBuyer(),
            IssueDate  = _issueDatePicker.Value.Date,
            TaxInvoiceEmail = _taxEmailBox.Text.Trim(),
            Unpaid     = _unpaidBox.Text.Trim(),
            Receiver   = _receiverBox.Text.Trim(),
            FooterNote = _footerNoteBox.Text,
            StampImagePath = _stampImagePath,
        };
        doc.Lines.AddRange(ReadTradeLines());

        if (!doc.Lines.Any(l => !string.IsNullOrWhiteSpace(l.ItemName) || l.Qty != 0))
        {
            MessageBox.Show("명세 라인을 하나 이상 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        WarnIfOverflow(doc.Lines.Count(l => !string.IsNullOrWhiteSpace(l.ItemName)), doc.MaxLines);
        return doc;
    }

    // ── 매출장(내부용) 문서 빌드 ──────────────────────────────────────
    private SalesLedgerDoc? BuildSalesLedgerDoc()
    {
        var doc = new SalesLedgerDoc
        {
            Supplier   = ReadSupplier(),
            Buyer      = ReadBuyer(),
            IssueDate  = _issueDatePicker.Value.Date,
            FooterNote = _footerNoteBox.Text,
            StampImagePath = _stampImagePath,
            ShowCost   = _showCostCheck.Checked,
            ShowProfit = _showProfitCheck.Checked,
        };
        doc.Lines.AddRange(ReadSalesLedgerLines());

        if (!doc.Lines.Any(l => !string.IsNullOrWhiteSpace(l.ItemName) || l.Qty != 0))
        {
            MessageBox.Show("명세 라인을 하나 이상 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        WarnIfOverflow(doc.Lines.Count(l => !string.IsNullOrWhiteSpace(l.ItemName)), SalesLedgerDoc.MaxLines);
        return doc;
    }

    // ── 견적서 문서 빌드 ──────────────────────────────────────────────
    private QuoteDoc? BuildQuoteDoc()
    {
        var doc = new QuoteDoc
        {
            DocType       = _currentType,
            Supplier      = ReadSupplier(),
            RecipientName = _quoteRecipient.Text.Trim(),
            DocTitle      = _quoteTitle.Text.Trim().Length > 0 ? _quoteTitle.Text.Trim() : "견 적 서",
            HeaderText    = _quoteHeaderText.Text.Trim(),
            PriceBasis    = _quotePriceBasis.Text.Trim().Length > 0 ? _quotePriceBasis.Text.Trim() : "VAT 포함",
            FooterNote    = _footerNoteBox.Text,
            IssueDate     = _issueDatePicker.Value.Date,
            StampImagePath = _stampImagePath,
        };

        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var line = new QuoteLineItem
            {
                ItemName  = row.Cells["ItemName"].Value?.ToString() ?? "",
                Unit      = row.Cells["Unit"].Value?.ToString() ?? "",
                Packing   = row.Cells["Packing"].Value?.ToString() ?? "",
                Note      = row.Cells["Note"].Value?.ToString() ?? "",
            };
            decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(), out var up); line.UnitPrice = up;
            if (!doc.IsBasic)
            {
                decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out var q); line.Qty = q;
            }
            doc.Lines.Add(line);
        }

        if (!doc.Lines.Any(l => !string.IsNullOrWhiteSpace(l.ItemName)))
        {
            MessageBox.Show("명세 라인을 하나 이상 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return doc;
    }

    // ── 가격조정 공문 빌드 ────────────────────────────────────────────
    private PriceAdjDoc? BuildPriceAdjDoc()
    {
        var doc = new PriceAdjDoc
        {
            Supplier      = ReadSupplier(),
            RecipientName = _adjRecipient.Text.Trim(),
            DocNo         = _adjDocNo.Text.Trim(),
            DocTitle      = _adjTitle.Text.Trim().Length > 0 ? _adjTitle.Text.Trim() : "제품 단가 조정에 관한 건",
            PriceBasis    = _adjPriceBasis.Text.Trim().Length > 0 ? _adjPriceBasis.Text.Trim() : "VAT 포함",
            BodyText      = _adjBodyText.Text,
            ApplyDate     = _adjApplyDatePicker.Value.Date,
            IssueDate     = _issueDatePicker.Value.Date,
            FooterNote    = _footerNoteBox.Text,
            StampImagePath = _stampImagePath,
        };

        int seq = 1;
        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var line = new PriceAdjLineItem
            {
                ItemName    = row.Cells["ItemName"].Value?.ToString() ?? "",
                Unit        = row.Cells["Unit"].Value?.ToString() ?? "",
                Note        = row.Cells["Note"].Value?.ToString() ?? "",
                ChannelCode = row.Cells["ChannelCode"].Value?.ToString(),
                CskuCode    = row.Cells["CskuCode"].Value?.ToString(),
            };
            int.TryParse(row.Cells["No"].Value?.ToString(), out var no); line.No = no > 0 ? no : seq;
            decimal.TryParse(row.Cells["PriceBefore"].Value?.ToString(), out var pb); line.PriceBefore = pb;
            decimal.TryParse(row.Cells["PriceAfter"].Value?.ToString(), out var pa); line.PriceAfter = pa;
            doc.Lines.Add(line);
            seq++;
        }

        if (!doc.Lines.Any(l => !string.IsNullOrWhiteSpace(l.ItemName)))
        {
            MessageBox.Show("명세 라인을 하나 이상 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return doc;
    }

    private IEnumerable<DocLineItem> ReadTradeLines()
    {
        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var item = new DocLineItem
            {
                ItemName = row.Cells["ItemName"].Value?.ToString() ?? "",
                Spec     = row.Cells["Spec"].Value?.ToString() ?? "",
                Note     = row.Cells["Note"].Value?.ToString() ?? "",
            };
            int.TryParse(row.Cells["Year"].Value?.ToString(), out var y); item.Year = y;
            int.TryParse(row.Cells["Month"].Value?.ToString(), out var mo); item.Month = mo;
            int.TryParse(row.Cells["Day"].Value?.ToString(), out var d); item.Day = d;
            decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out var q); item.Qty = q;
            decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(), out var p); item.UnitPrice = p;
            yield return item;
        }
    }

    private IEnumerable<SalesLedgerLineItem> ReadSalesLedgerLines()
    {
        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var item = new SalesLedgerLineItem
            {
                ItemName = row.Cells["ItemName"].Value?.ToString() ?? "",
                Spec     = row.Cells["Spec"].Value?.ToString() ?? "",
                Note     = row.Cells["Note"].Value?.ToString() ?? "",
            };
            int.TryParse(row.Cells["Year"].Value?.ToString(), out var y); item.Year = y;
            int.TryParse(row.Cells["Month"].Value?.ToString(), out var mo); item.Month = mo;
            int.TryParse(row.Cells["Day"].Value?.ToString(), out var d); item.Day = d;
            decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out var q); item.Qty = q;
            decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(), out var p); item.UnitPrice = p;
            decimal.TryParse(row.Cells["CostPrice"].Value?.ToString(), out var c); item.CostPrice = c;
            yield return item;
        }
    }

    private string DefaultFileName() => _currentType switch
    {
        DocType.TradeStatementVatExcl or DocType.TradeStatementVatIncl =>
            $"거래명세표_{_issueDatePicker.Value:yyyyMMdd}.xlsx",
        DocType.QuoteBasic or DocType.QuoteWithQty =>
            $"견적서_{_issueDatePicker.Value:yyyyMMdd}.xlsx",
        DocType.PriceAdjustment =>
            $"가격조정공문_{_issueDatePicker.Value:yyyyMMdd}.xlsx",
        DocType.SalesLedger =>
            $"매출장_{_issueDatePicker.Value:yyyyMMdd}.xlsx",
        _ => $"문서_{_issueDatePicker.Value:yyyyMMdd}.xlsx"
    };

    private static void WarnIfOverflow(int count, int max)
    {
        if (count > max)
            MessageBox.Show(
                $"명세 라인이 {count}개로 1페이지 최대({max}행)를 초과합니다.\n" +
                "XLSX로 저장은 계속 진행됩니다.",
                "라인 수 초과", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ══════════════════════════════════════════════════════════════════
    //  거래처 DB
    // ══════════════════════════════════════════════════════════════════

    private void OnManagePartiesClick(object? sender, EventArgs e)
    {
        using var dlg = new PartyManagerForm();
        dlg.ShowDialog(this);
    }

    private void OnLoadPartyFromChannelClick(object? sender, EventArgs e)
    {
        var channelTypes = _channelConfigService.Load().ToDictionary(c => c.ChannelCode, c => c.ChannelType);
        var partyByChannel = _partyRepo.GetChannelLinkedAll().ToDictionary(p => p.ChannelCode);

        var options = _channelRepo.GetAll()
            .Where(c => !(channelTypes.TryGetValue(c.ChannelCode, out var type) ? type : ChannelType.General).IsMarketplace())
            .Select(c => new ChannelPartyOption(c, partyByChannel.TryGetValue(c.ChannelCode, out var p) ? p : null))
            .ToList();

        if (options.Count == 0)
        {
            MessageBox.Show("등록된 거래처(온라인 채널 제외) 목록이 없습니다.\n채널 설정 창에서 거래처 채널을 먼저 추가하세요.", "알림");
            return;
        }

        using var dlg = new ChannelPartySelectDialog(options);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.NeedsChannelSetup != null)
        {
            var configForm = Application.OpenForms.OfType<ChannelConfigForm>().FirstOrDefault() ?? new ChannelConfigForm();
            if (!configForm.Visible) configForm.Show();
            configForm.BringToFront();
            configForm.SelectChannelPartyTab(dlg.NeedsChannelSetup);
            return;
        }

        if (dlg.Selected == null) return;
        FillPartyFields(dlg.Selected, supplier: false);
        _currentBuyerChannelCode = dlg.Selected.ChannelCode;

        // B2B 거래처 견적서는 VAT별도가 기본이다(§D5) — 채널유형이 확인되는 시점(채널에서 불러오기)에만
        // 자동으로 바꿔주고, 수기로 입력한 값은 건드리지 않는다.
        var channelType = channelTypes.TryGetValue(_currentBuyerChannelCode, out var t) ? t : ChannelType.General;
        if (channelType == ChannelType.B2B && _currentType is DocType.QuoteBasic or DocType.QuoteWithQty)
        {
            _quotePriceBasis.Text = "VAT 별도";
        }
    }

    private void LoadPartyFromDb(bool supplier)
    {
        var parties = _partyRepo.GetAll();
        if (parties.Count == 0) { MessageBox.Show("저장된 거래처가 없습니다.", "알림"); return; }
        using var dlg = new PartySelectDialog(parties);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selected == null) return;
        FillPartyFields(dlg.Selected, supplier);
    }

    private void SavePartyToDb(bool supplier)
    {
        var party = supplier ? ReadSupplier() : ReadBuyer();
        if (string.IsNullOrWhiteSpace(party.CompanyName)) { MessageBox.Show("상호를 입력하세요.", "알림"); return; }

        string? name = Prompt("프로필 이름을 입력하세요:", party.CompanyName);
        if (name == null) return;
        party.ProfileName = name;
        _partyRepo.Save(party);

        if (supplier && MessageBox.Show("기본 공급자로 설정하시겠습니까?", "기본 공급자", MessageBoxButtons.YesNo) == DialogResult.Yes)
            _partyRepo.SetDefaultSupplier(party.Id);

        SetStatus($"'{party.CompanyName}' 저장 완료", true);
    }

    private void LoadDefaultSupplier()
    {
        var def = _partyRepo.GetDefaultSupplier();
        if (def != null) FillPartyFields(def, supplier: true);
    }

    private DocParty ReadSupplier() => new()
    {
        RegNo = _supRegNo.Text.Trim(), CompanyName = _supCompany.Text.Trim(),
        CeoName = _supCeo.Text.Trim(), Address = _supAddress.Text.Trim(),
        BizType = _supBizType.Text.Trim(), BizItem = _supBizItem.Text.Trim(),
        Tel = _supTel.Text.Trim(), Email = _supEmail.Text.Trim()
    };

    private DocParty ReadBuyer() => new()
    {
        RegNo = _buyRegNo.Text.Trim(), CompanyName = _buyCompany.Text.Trim(),
        CeoName = _buyCeo.Text.Trim(), Address = _buyAddress.Text.Trim(),
        BizType = _buyBizType.Text.Trim(), BizItem = _buyBizItem.Text.Trim(),
        Tel = _buyTel.Text.Trim(), Email = _buyEmail.Text.Trim()
    };

    private void FillPartyFields(DocParty p, bool supplier)
    {
        (supplier ? _supRegNo : _buyRegNo).Text = p.RegNo;
        (supplier ? _supCompany : _buyCompany).Text = p.CompanyName;
        (supplier ? _supCeo : _buyCeo).Text = p.CeoName;
        (supplier ? _supAddress : _buyAddress).Text = p.Address;
        (supplier ? _supBizType : _buyBizType).Text = p.BizType;
        (supplier ? _supBizItem : _buyBizItem).Text = p.BizItem;
        (supplier ? _supTel : _buyTel).Text = p.Tel;
        (supplier ? _supEmail : _buyEmail).Text = p.Email;

        // 공급자(발행자) 쪽만 도장이 의미가 있다 — 문서에 찍히는 도장은 항상 공급자의 것이다.
        // 거래처 관리 창에서 이 거래처에 지정해둔 기본 도장이 있으면 자동으로 불러오되, 이미
        // 하단 도장 업로드로 이번 문서에만 다른 도장을 지정해뒀다면 그걸 덮어쓰지 않는다.
        if (supplier && string.IsNullOrWhiteSpace(_stampImagePath) &&
            !string.IsNullOrWhiteSpace(p.StampImagePath) && File.Exists(p.StampImagePath))
        {
            _stampImagePath = p.StampImagePath;
            _stampLabel.Text = Path.GetFileName(_stampImagePath);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  즐겨찾기 문구
    // ══════════════════════════════════════════════════════════════════

    private void OnSelectPhraseClick(object? sender, EventArgs e)
    {
        var phrases = _phraseRepo.GetAll();
        if (phrases.Count == 0) { MessageBox.Show("저장된 즐겨찾기 문구가 없습니다.", "알림"); return; }
        using var dlg = new FavoritePhraseDialog(phrases);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selected == null) return;
        _adjBodyText.Text = dlg.Selected.Body;
    }

    private void OnSavePhraseClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_adjBodyText.Text)) { MessageBox.Show("저장할 문구를 먼저 입력하세요.", "알림"); return; }
        string? title = Prompt("문구 제목:", "");
        if (title == null || string.IsNullOrWhiteSpace(title)) return;

        string[] cats = { "일반", "인상", "인하" };
        using var catDlg = new Form { Text = "카테고리 선택", Size = new Size(280, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        var combo = new ComboBox { Left = 10, Top = 12, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(cats);
        combo.SelectedIndex = 0;
        var ok = new Button { Text = "확인", Left = 60, Top = 50, Width = 70, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", Left = 150, Top = 50, Width = 70, DialogResult = DialogResult.Cancel };
        catDlg.Controls.AddRange(new Control[] { combo, ok, cancel });
        catDlg.AcceptButton = ok; catDlg.CancelButton = cancel;
        if (catDlg.ShowDialog(this) != DialogResult.OK) return;

        _phraseRepo.Save(new DocFavoritePhrase
        {
            Title = title,
            Body = _adjBodyText.Text,
            Category = combo.SelectedItem?.ToString() ?? "일반"
        });
        SetStatus($"문구 '{title}' 저장 완료", true);
    }

    // ══════════════════════════════════════════════════════════════════
    //  기타 이벤트
    // ══════════════════════════════════════════════════════════════════

    private void OnLoadCskuClick(object? sender, EventArgs e)
    {
        using var dlg = new CskuPickerDialog(_currentBuyerChannelCode);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedItemName == null) return;

        _lineGrid.EndEdit();
        var row = TargetLineRow();
        row.Cells["ItemName"].Value = dlg.SelectedItemName;
        if (_lineGrid.Columns.Contains("Unit"))
            row.Cells["Unit"].Value = dlg.SelectedUnit;
        if (_lineGrid.Columns.Contains("Packing"))
            row.Cells["Packing"].Value = dlg.SelectedPacking;

        // 가격조정 공문은 "현재 납품가"를 조정 전 가격으로 채우고, CSKU 연결 정보를 함께 저장해둔다
        // — "납품가 반영" 버튼이 이 값으로 실제 CSKU를 찾아 조정 후 가격을 반영할 수 있게 하기 위함.
        if (_currentType == DocType.PriceAdjustment)
        {
            row.Cells["PriceBefore"].Value = dlg.SelectedUnitPrice;
            row.Cells["ChannelCode"].Value = dlg.SelectedChannelCode;
            row.Cells["CskuCode"].Value = dlg.SelectedCskuCode;
            return;
        }

        if (_lineGrid.Columns.Contains("UnitPrice"))
            row.Cells["UnitPrice"].Value = dlg.SelectedUnitPrice;
        if (_lineGrid.Columns.Contains("CostPrice"))
            row.Cells["CostPrice"].Value = dlg.SelectedCostPrice;
    }

    /// <summary>
    /// B2B 가격조정 공문(§M6) — "CSKU 불러오기"로 채운 줄(ChannelCode/CskuCode가 있는 줄)만
    /// PriceAfter를 실제 ChannelSkuTable.SupplyPrice에 반영하고, 그 변경을 ChannelSkuPriceHistory에
    /// 사유(문서 제목)와 함께 남긴다. 수기로 입력한 줄(연결된 CSKU 없음)은 안내 문서일 뿐이므로 건너뛴다.
    /// </summary>
    private void OnApplyPriceAdjustmentClick(object? sender, EventArgs e)
    {
        if (_currentType != DocType.PriceAdjustment)
        {
            MessageBox.Show("가격조정 공문에서만 사용할 수 있습니다.", "알림");
            return;
        }

        _lineGrid.EndEdit();
        var applicable = new List<(DataGridViewRow Row, string ChannelCode, string CskuCode, decimal PriceAfter)>();
        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var channelCode = row.Cells["ChannelCode"].Value?.ToString();
            var cskuCode = row.Cells["CskuCode"].Value?.ToString();
            if (string.IsNullOrEmpty(channelCode) || string.IsNullOrEmpty(cskuCode)) continue;
            decimal.TryParse(row.Cells["PriceAfter"].Value?.ToString(), out var priceAfter);
            applicable.Add((row, channelCode, cskuCode, priceAfter));
        }

        if (applicable.Count == 0)
        {
            MessageBox.Show(
                "반영할 줄이 없습니다.\n\"CSKU 불러오기\"로 채운 줄만 납품가에 반영할 수 있습니다(수기 입력 줄은 안내문 전용).",
                "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var skippedCount = _lineGrid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow) - applicable.Count;
        var reason = _adjTitle.Text.Trim().Length > 0 ? _adjTitle.Text.Trim() : "가격조정 공문";
        var confirmMsg = $"{applicable.Count}건의 납품가를 아래와 같이 실제 반영합니다(되돌리려면 CSKU 관리 화면에서 직접 수정해야 합니다).\n\n"
            + string.Join("\n", applicable.Take(5).Select(a => $"- {a.Row.Cells["ItemName"].Value}: {a.PriceAfter:N0}"))
            + (applicable.Count > 5 ? $"\n... 외 {applicable.Count - 5}건" : "")
            + (skippedCount > 0 ? $"\n\n(연결된 CSKU가 없는 {skippedCount}건은 제외됩니다.)" : "")
            + "\n\n계속하시겠습니까?";
        if (MessageBox.Show(confirmMsg, "납품가 반영 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        var appliedCount = 0;
        foreach (var (row, channelCode, cskuCode, priceAfter) in applicable)
        {
            var csku = _channelSkuRepository.GetByChannelAndCskuCode(channelCode, cskuCode);
            if (csku == null) continue;
            csku.SupplyPrice = priceAfter;
            _channelSkuRepository.Upsert(csku, reason);
            appliedCount++;
        }

        _statusLabel.Text = $"납품가 {appliedCount}건을 반영했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>
    /// 과거 출고이력(OutboundDetailTable, OFS 발주확정/출고확정 이력)을 채널·기간으로 조회해
    /// 체크한 여러 건을 한 번에 그리드에 새 줄로 추가한다. 거래명세표/매출장은 실제 출고건이라
    /// 발주일(연/월/일)까지 채우고, 견적서는 날짜 열이 없으므로 품목/단가만 채운다.
    /// </summary>
    private void OnLoadOutboundHistoryClick(object? sender, EventArgs e)
    {
        if (_currentType == DocType.PriceAdjustment)
        {
            MessageBox.Show("가격조정 공문에서는 출고이력 불러오기를 사용할 수 없습니다.", "알림");
            return;
        }

        using var dlg = new OutboundHistoryPickerDialog(_currentBuyerChannelCode);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedPicks.Count == 0) return;

        _lineGrid.EndEdit();
        foreach (var pick in dlg.SelectedPicks)
        {
            int idx = _lineGrid.Rows.Add();
            var row = _lineGrid.Rows[idx];
            var date = pick.Detail.CreatedAt;

            if (_lineGrid.Columns.Contains("Year")) row.Cells["Year"].Value = date.Year;
            if (_lineGrid.Columns.Contains("Month")) row.Cells["Month"].Value = date.Month;
            if (_lineGrid.Columns.Contains("Day")) row.Cells["Day"].Value = date.Day;
            row.Cells["ItemName"].Value = pick.ItemName;
            if (_lineGrid.Columns.Contains("Qty")) row.Cells["Qty"].Value = pick.Detail.Qty;
            if (_lineGrid.Columns.Contains("UnitPrice")) row.Cells["UnitPrice"].Value = pick.Detail.SupplyPrice;
            if (_lineGrid.Columns.Contains("CostPrice")) row.Cells["CostPrice"].Value = pick.CostPrice;
        }
    }

    private DataGridViewRow TargetLineRow()
    {
        var current = _lineGrid.CurrentRow;
        if (current != null && !current.IsNewRow) return current;
        int idx = _lineGrid.Rows.Add();
        return _lineGrid.Rows[idx];
    }

    private void OnStampClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "이미지 (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png", Title = "도장 이미지 선택" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _stampImagePath = ofd.FileName;
        _stampLabel.Text = Path.GetFileName(_stampImagePath);
    }

    private void SetStatus(string msg, bool success)
    {
        _statusLabel.ForeColor = success ? System.Drawing.Color.DarkGreen : System.Drawing.Color.Red;
        _statusLabel.Text = msg;
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI 헬퍼
    // ══════════════════════════════════════════════════════════════════

    private static TableLayoutPanel MakePartyTable()
    {
        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Padding = new Padding(4)
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        for (int i = 0; i < 4; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        return tbl;
    }

    private static void PartyRows(TableLayoutPanel tbl,
        TextBox regNo, TextBox company, TextBox ceo, TextBox address,
        TextBox bizType, TextBox bizItem, TextBox tel, TextBox email)
    {
        tbl.Controls.Add(Lbl("등록번호"), 0, 0); tbl.Controls.Add(regNo, 1, 0);
        tbl.Controls.Add(Lbl("상호"), 2, 0);     tbl.Controls.Add(company, 3, 0);
        tbl.Controls.Add(Lbl("대표자"), 0, 1);   tbl.Controls.Add(ceo, 1, 1);
        tbl.Controls.Add(Lbl("주소"), 2, 1);     tbl.Controls.Add(address, 3, 1);
        tbl.Controls.Add(Lbl("업태"), 0, 2);     tbl.Controls.Add(bizType, 1, 2);
        tbl.Controls.Add(Lbl("종목"), 2, 2);     tbl.Controls.Add(bizItem, 3, 2);
        tbl.Controls.Add(Lbl("전화"), 0, 3);     tbl.Controls.Add(tel, 1, 3);
        tbl.Controls.Add(Lbl("이메일"), 2, 3);   tbl.Controls.Add(email, 3, 3);
    }

    private static TextBox TB() => new() { Dock = DockStyle.Fill };

    private static Label Lbl(string text) =>
        new() { Text = text, AutoSize = true, Padding = new Padding(0, 4, 4, 0) };

    private static Button Btn(string text, int width, EventHandler handler)
    {
        var b = new Button { Text = text, Size = new Size(width, 28) };
        b.Click += handler;
        return b;
    }

    private static Label Gap(int width) => new() { Width = width, AutoSize = false };

    private static Control RowCtrl(string label, Control ctrl)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(Lbl(label), 0, 0);
        row.Controls.Add(ctrl, 1, 0);
        return row;
    }

    private static string? Prompt(string message, string defaultValue)
    {
        using var form = new Form
        {
            Text = "입력", Size = new Size(340, 130),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var lbl = new Label { Text = message, Left = 10, Top = 12, Width = 300 };
        var tb  = new TextBox { Left = 10, Top = 32, Width = 300, Text = defaultValue };
        var ok  = new Button { Text = "확인", Left = 150, Top = 60, Width = 70, DialogResult = DialogResult.OK };
        var no  = new Button { Text = "취소", Left = 230, Top = 60, Width = 70, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { lbl, tb, ok, no });
        form.AcceptButton = ok; form.CancelButton = no;
        return form.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  발행 이력 저장
    // ══════════════════════════════════════════════════════════════════

    private string DocTypeLabel() => _currentType switch
    {
        DocType.TradeStatementVatExcl => "거래명세표(VAT별도)",
        DocType.TradeStatementVatIncl => "거래명세표(VAT포함)",
        DocType.QuoteBasic            => "견적서(기본)",
        DocType.QuoteWithQty          => "견적서(수량형)",
        DocType.PriceAdjustment       => "가격조정명세서",
        DocType.SalesLedger           => "매출장",
        _                             => "문서",
    };

    private void SaveHistory(string filePath)
    {
        try
        {
            decimal total = 0;
            string buyerName = "";
            switch (_currentType)
            {
                case DocType.TradeStatementVatExcl:
                case DocType.TradeStatementVatIncl:
                    total      = decimal.TryParse(_unpaidBox.Text.Replace(",", ""), out var u) ? u : 0;
                    buyerName  = _buyCompany.Text.Trim();
                    if (_currentType == DocType.TradeStatementVatExcl)
                        total = decimal.TryParse(_unpaidBox.Text.Replace(",", ""), out _) ? total : 0;
                    break;
                case DocType.QuoteBasic:
                case DocType.QuoteWithQty:
                    buyerName = _quoteRecipient.Text.Trim();
                    break;
                case DocType.PriceAdjustment:
                    buyerName = _adjRecipient.Text.Trim();
                    break;
            }

            // 생성 직후의 엑셀 바이트를 DB에도 백업해둔다 — FilePath만 들고 있으면 사용자가 나중에
            // 파일을 옮기거나 지웠을 때 이력에서 "파일 열기"가 불가능해지는 문제(사용자 신고)를
            // 막기 위함. 읽기 실패해도(권한 등) 이력 저장 자체는 계속 진행한다.
            byte[]? fileBytes = null;
            try { fileBytes = File.ReadAllBytes(filePath); } catch { /* 백업 실패해도 이력 저장은 계속 */ }

            _historyRepo.Add(new DocHistoryRecord
            {
                DocType     = DocTypeLabel(),
                IssueDate   = _issueDatePicker.Value.Date,
                BuyerName   = buyerName,
                TotalAmount = total,
                FilePath    = filePath,
                CreatedAt   = DateTime.Now,
                FileBytes   = fileBytes,
            });
        }
        catch
        {
            // 이력 저장 실패는 주요 기능에 영향 없음
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  이력 조회
    // ══════════════════════════════════════════════════════════════════

    private void OnHistoryClick(object? sender, EventArgs e)
    {
        using var dlg = new DocHistoryForm(_historyRepo);
        dlg.ShowDialog(this);
    }

    // ══════════════════════════════════════════════════════════════════
    //  PDF 출력 (Excel COM 레이트 바인딩 — Microsoft Excel 설치 필요)
    // ══════════════════════════════════════════════════════════════════

    private void OnPrintPdfClick(object? sender, EventArgs e)
    {
        _lineGrid.EndEdit();

        // PDF 저장 경로 먼저 확인
        string defaultPdf = DefaultFileName().Replace(".xlsx", ".pdf");
        using var sfd = new SaveFileDialog
        {
            Filter = "PDF 파일 (*.pdf)|*.pdf",
            FileName = defaultPdf,
            InitialDirectory = _settingsService.GetLastFolder("DocsPdf")
                ?? _settingsService.GetLastFolder("DocsExport")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("DocsPdf", Path.GetDirectoryName(sfd.FileName)!);

        // 임시 XLSX 생성
        string tempXlsx = Path.Combine(Path.GetTempPath(), $"_doc_pdf_{Guid.NewGuid():N}.xlsx");
        try
        {
            switch (_currentType)
            {
                case DocType.TradeStatementVatExcl:
                case DocType.TradeStatementVatIncl:
                    var tsDoc = BuildTradeStatementDoc();
                    if (tsDoc == null) return;
                    DocumentExporter.ExportTradeStatement(tsDoc, tempXlsx);
                    break;
                case DocType.QuoteBasic:
                case DocType.QuoteWithQty:
                    var qDoc = BuildQuoteDoc();
                    if (qDoc == null) return;
                    DocumentExporter.ExportQuote(qDoc, tempXlsx);
                    break;
                case DocType.PriceAdjustment:
                    var paDoc = BuildPriceAdjDoc();
                    if (paDoc == null) return;
                    DocumentExporter.ExportPriceAdjustment(paDoc, tempXlsx);
                    break;
                case DocType.SalesLedger:
                    var slDoc = BuildSalesLedgerDoc();
                    if (slDoc == null) return;
                    DocumentExporter.ExportSalesLedger(slDoc, tempXlsx);
                    break;
                default: return;
            }

            ExportXlsxToPdf(tempXlsx, sfd.FileName);
            SetStatus($"PDF 저장 완료: {Path.GetFileName(sfd.FileName)}", true);

            var res = MessageBox.Show("PDF를 열겠습니까?", "PDF 저장 완료", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (COMException cex)
        {
            MessageBox.Show(
                "Excel이 설치되어 있지 않아 PDF를 직접 생성할 수 없습니다.\n" +
                "엑셀로 저장 후 '파일 > 다른 이름으로 저장 > PDF(.pdf)'를 사용하세요.\n\n" +
                $"오류: {cex.Message}",
                "PDF 변환 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF 생성 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { if (File.Exists(tempXlsx)) File.Delete(tempXlsx); } catch { }
        }
    }

    private static void ExportXlsxToPdf(string xlsxPath, string pdfPath)
    {
        // Excel COM 레이트 바인딩 — Excel 설치 필수, Interop 어셈블리 참조 불필요
        dynamic? excel = null;
        dynamic? wb = null;
        try
        {
            var progId = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new COMException("Excel.Application ProgID를 찾을 수 없습니다. Microsoft Excel이 설치되어 있는지 확인하세요.");
            excel = Activator.CreateInstance(progId);
            excel!.Visible = false;
            excel.DisplayAlerts = false;

            wb = excel.Workbooks.Open(xlsxPath);
            wb!.ExportAsFixedFormat(
                0 /*xlTypePDF*/,
                pdfPath,
                0 /*xlQualityStandard*/,
                true  /*IncludeDocProperties*/,
                false /*IgnorePrintAreas*/);
        }
        finally
        {
            try { wb?.Close(false); } catch { }
            try { excel?.Quit(); } catch { }
            if (excel != null) Marshal.ReleaseComObject(excel);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════
//  보조 다이얼로그
// ══════════════════════════════════════════════════════════════════════

internal class PartySelectDialog : Form
{
    public DocParty? Selected { get; private set; }

    public PartySelectDialog(List<DocParty> parties)
    {
        Text = "거래처 선택";
        Size = new Size(400, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var p in parties) list.Items.Add(p);

        var ok = new Button { Text = "선택", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.OK };
        ok.Click += (s, e) =>
        {
            if (list.SelectedItem is DocParty p) { Selected = p; DialogResult = DialogResult.OK; }
        };
        list.DoubleClick += (s, e) =>
        {
            if (list.SelectedItem is DocParty p) { Selected = p; DialogResult = DialogResult.OK; Close(); }
        };

        Controls.Add(list);
        Controls.Add(ok);
        AcceptButton = ok;
    }
}

/// <summary>채널 1건과 해당 채널에 저장된 공급받는자 정보(없으면 null)를 짝지은 선택 후보.</summary>
internal sealed record ChannelPartyOption(SalesChannel Channel, DocParty? Party);

internal class ChannelPartySelectDialog : Form
{
    public DocParty? Selected { get; private set; }

    /// <summary>거래처 정보가 없는 채널을 선택했을 때, 채널 설정 창에서 등록해야 할 채널 코드.</summary>
    public string? NeedsChannelSetup { get; private set; }

    public ChannelPartySelectDialog(List<ChannelPartyOption> options)
    {
        Text = "채널 선택";
        Size = new Size(420, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var opt in options)
        {
            string label = opt.Party != null
                ? $"{opt.Channel.ChannelName} — {opt.Party.CompanyName}"
                : $"{opt.Channel.ChannelName} — (거래처 정보 없음, 선택 시 등록)";
            list.Items.Add(new ChannelPartyItem(label, opt));
        }

        var ok = new Button { Text = "선택", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.OK };
        ok.Click += (s, e) => Confirm(list);
        list.DoubleClick += (s, e) => Confirm(list);

        Controls.Add(list);
        Controls.Add(ok);
        AcceptButton = ok;
    }

    private void Confirm(ListBox list)
    {
        if (list.SelectedItem is not ChannelPartyItem item) return;
        if (item.Option.Party != null) Selected = item.Option.Party;
        else NeedsChannelSetup = item.Option.Channel.ChannelCode;
        DialogResult = DialogResult.OK;
    }

    private sealed record ChannelPartyItem(string Label, ChannelPartyOption Option)
    {
        public override string ToString() => Label;
    }
}

internal class FavoritePhraseDialog : Form
{
    public DocFavoritePhrase? Selected { get; private set; }

    public FavoritePhraseDialog(List<DocFavoritePhrase> phrases)
    {
        Text = "즐겨찾기 문구 선택";
        Size = new Size(500, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));

        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (var p in phrases) list.Items.Add(p);

        var preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = System.Drawing.SystemColors.Window };
        list.SelectedIndexChanged += (s, e) =>
        {
            preview.Text = list.SelectedItem is DocFavoritePhrase p ? p.Body : "";
        };

        var ok = new Button { Text = "선택", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.OK };
        ok.Click += (s, e) =>
        {
            if (list.SelectedItem is DocFavoritePhrase p) { Selected = p; DialogResult = DialogResult.OK; }
        };
        list.DoubleClick += (s, e) =>
        {
            if (list.SelectedItem is DocFavoritePhrase p) { Selected = p; DialogResult = DialogResult.OK; Close(); }
        };

        layout.Controls.Add(list, 0, 0);
        layout.Controls.Add(preview, 0, 1);
        Controls.Add(layout);
        Controls.Add(ok);
        AcceptButton = ok;
    }
}
