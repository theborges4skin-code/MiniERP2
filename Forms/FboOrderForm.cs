using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 네이버 풀필먼트(FBO) 발주 등록 화면. 발주 1건 = 박스(FboBox) 여러 개, 박스 1개 = 반품부명이
/// 같은 품목(FboBoxItem) 여러 줄(기획서 §2). CSKU + 박스수를 고르고 [박스 산정]을 누르면 박스가
/// 자동 생성되고, 이후 그리드에서 수량/합포장(행 추가)을 직접 편집한다. 핵심 2단계는 OFS 발주
/// 화면과 동일하게 "저장(발주확정)" → "엑셀파일 내보내기"(하배출고이서, 택배시스템 업로드용)이고,
/// 그 다음 같은 화면에서 (외부 CJ 처리) → 운송장 결과 불러오기 → 입고양식 파일 변환(풀필먼트
/// 시스템 입고등록 업로드용 별도 파일)까지 이어서 진행한다(기획서 §5.3).
/// </summary>
public class FboOrderForm : Form
{
    private readonly FboCskuRepository _cskuRepository = new();
    private readonly FboChannelConfigRepository _channelConfigRepository = new();
    private readonly FboOrderRepository _orderRepository = new();
    private readonly SettingsService _settingsService = new();

    private string _fboNo = string.Empty;
    private bool _isSaved;
    private List<FboChannelConfigModel> _channels = [];
    private List<FboCskuModel> _cskus = [];
    private readonly BindingList<FboBoxItemRow> _rows = [];

    private Label _fboNoLabel = new();
    private DateTimePicker _orderDatePicker = new();
    private ComboBox _channelCombo = new();
    private Label _receiverInfoLabel = new();
    private ComboBox _cskuCombo = new();
    private NumericUpDown _boxCountInput = new();
    private CheckBox _expiryCheckBox = new();
    private DateTimePicker _expiryPicker = new();
    private Button _btnCalcBoxes = new();
    private DataGridView _grid = new();
    private Label _summaryLabel = new();
    private Button _btnSave = new();
    private Button _btnExportOrder = new();
    private Button _btnImportTracking = new();
    private Button _btnExportInbound = new();

    public FboOrderForm()
    {
        InitializeComponent();
        LoadMasterData();
        GenerateNewFboNo();
    }

    /// <summary>
    /// 과거 발주 이력(FboHistoryForm)에서 "복사하여 신규 발주"로 진입할 때 쓰는 생성자. 매번 CSKU를
    /// 새로 골라야 하는 불편을 없애기 위해, 과거 발주의 박스/품목 구성을 그대로 복사해 새 발주번호로
    /// 시작한다. 운송장번호·상태 등 이전 발주의 진행 이력은 복사하지 않는다(완전히 새로운 발주이므로).
    /// </summary>
    public FboOrderForm(FboOrder templateOrder, List<FboBox> templateBoxes, List<FboBoxItem> templateItems) : this()
    {
        ApplyTemplate(templateOrder, templateBoxes, templateItems);
    }

    private class FboBoxItemRow
    {
        public int BoxSeq { get; set; }
        public string ReceiverDisplayName { get; set; } = string.Empty;
        public string MatchKey { get; set; } = string.Empty;
        public string Csku { get; set; } = string.Empty;
        public string FboItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? InvoiceDisplayName { get; set; }
        public int QtyPerBox { get; set; }
        public int Qty { get; set; }
        public string? ExpiryDate { get; set; }
        public string BoxType { get; set; } = "소";
        public string? TrackingNo { get; set; }
    }

    private void InitializeComponent()
    {
        Text = "풀필먼트 발주 처리";
        Size = new Size(980, 680);
        MinimumSize = new Size(780, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        // 1행: 발주번호/발주일/채널/발주지
        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 0, 0) };
        _fboNoLabel = new Label { Text = "(자동)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 3, 12, 0) };
        _orderDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _orderDatePicker.ValueChanged += OnOrderDateChanged;
        _channelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        _channelCombo.SelectedIndexChanged += OnChannelComboChanged;
        _receiverInfoLabel = new Label { Text = "채널을 먼저 선택하세요", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 3, 0, 0) };
        row1.Controls.AddRange(
        [
            new Label { Text = "발주번호:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) }, _fboNoLabel,
            new Label { Text = "발주일:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) }, _orderDatePicker,
            new Label { Text = "채널:", AutoSize = true, Padding = new Padding(12, 3, 4, 0) }, _channelCombo,
            _receiverInfoLabel,
        ]);

        // 2행: CSKU 선택 + 박스 산정
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0) };
        // 타이핑할 때마다 후보를 직접 필터링해 드롭다운에 보여준다(네이티브 AutoComplete 팝업은
        // 높이 제한/스크롤을 우리가 제어할 수 없어서 대신 콤보의 표준 드롭다운을 쓴다).
        // MaxDropDownItems=5로 5행까지는 그대로, 6행 이상이면 자동으로 스크롤바가 생긴다.
        _cskuCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, MaxDropDownItems = 5, Width = 260 };
        _cskuCombo.TextUpdate += OnCskuSearchTextUpdate;
        var btnCskuPicker = new Button { Text = "CSKU 검색하여 추가", Size = new Size(130, 26), Margin = new Padding(6, 0, 0, 0) };
        btnCskuPicker.Click += OnCskuPickerClick;
        _boxCountInput = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 1, Width = 60 };
        _expiryCheckBox = new CheckBox { Text = "유통기한", AutoSize = true, Padding = new Padding(12, 3, 0, 0) };
        _expiryPicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100, Enabled = false };
        _expiryCheckBox.CheckedChanged += (s, e) => _expiryPicker.Enabled = _expiryCheckBox.Checked;
        _btnCalcBoxes = new Button { Text = "박스 산정", Size = new Size(90, 26), Margin = new Padding(12, 0, 0, 0) };
        _btnCalcBoxes.Click += OnCalcBoxesClick;
        var btnManageItems = new Button { Text = "품목/설정 관리", Size = new Size(100, 26), Margin = new Padding(12, 0, 0, 0) };
        btnManageItems.Click += OnManageItemsClick;
        row2.Controls.AddRange(
        [
            new Label { Text = "CSKU:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) }, _cskuCombo,
            btnCskuPicker,
            new Label { Text = "박스수:", AutoSize = true, Padding = new Padding(12, 3, 4, 0) }, _boxCountInput,
            _expiryCheckBox, _expiryPicker,
            _btnCalcBoxes,
            btnManageItems,
        ]);

        // 3행: 행 추가/삭제 툴바
        var row3 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 2, 0, 0) };
        var btnAddRow = new Button { Text = "행 추가(합포장)", Size = new Size(110, 26) };
        var btnRemoveRow = new Button { Text = "행 삭제", Size = new Size(80, 26) };
        var btnLoadPastOrder = new Button { Text = "지난 발주 불러오기", Size = new Size(130, 26), Margin = new Padding(12, 0, 0, 0) };
        var btnLoadRecentCsku = new Button { Text = "지난 CSKU 불러오기", Size = new Size(140, 26), Margin = new Padding(6, 0, 0, 0) };
        btnAddRow.Click += OnAddRowClick;
        btnRemoveRow.Click += OnRemoveRowClick;
        btnLoadPastOrder.Click += OnLoadPastOrderClick;
        btnLoadRecentCsku.Click += OnLoadRecentCskuClick;
        row3.Controls.AddRange([btnAddRow, btnRemoveRow, btnLoadPastOrder, btnLoadRecentCsku]);

        // 4행: 그리드
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스", DataPropertyName = "BoxSeq", ReadOnly = true, Width = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "반품부명", DataPropertyName = "ReceiverDisplayName", ReadOnly = true, Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CSKU", DataPropertyName = "Csku", ReadOnly = true, Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "품목명", DataPropertyName = "ItemName", ReadOnly = true, Width = 220, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "수량", DataPropertyName = "Qty", Width = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "유통기한(YYYYMMDD)", DataPropertyName = "ExpiryDate", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스타입", DataPropertyName = "BoxType", Width = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "이송장번호", DataPropertyName = "TrackingNo", ReadOnly = true, Width = 110 });
        _grid.DataSource = _rows;
        _grid.CellEndEdit += (s, e) => UpdateSummary();

        // 5행: 후속 처리(발주확정 이후에만 의미가 있는 단계 — CJ 처리 결과 반영/재고 보고)
        var row5 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 2, 6, 0) };
        var postProcessLabel = new Label { Text = "후속 처리:", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 8, 0) };
        _btnExportInbound = new Button { Text = "입고양식 파일 변환", Size = new Size(130, 28) };
        _btnImportTracking = new Button { Text = "운송장 불러오기", Size = new Size(110, 28) };
        _btnExportInbound.Click += OnExportInboundClick;
        _btnImportTracking.Click += OnImportTrackingClick;
        row5.Controls.AddRange([_btnExportInbound, _btnImportTracking, postProcessLabel]);

        // 6행: 요약 라벨 — 버튼과 한 TableLayoutPanel 행에 같이 넣으면(AutoSize 열 + Dock=Fill 자식
        // 조합) 열 너비 계산이 꼬여 버튼이 안 보이는 문제가 있었다(재현 후 확정). 이 코드베이스의
        // 다른 조회창들(OutboundHistoryForm 등)도 전부 라벨 행과 버튼 행을 분리해두므로 그 패턴을
        // 그대로 따른다 — 절대 한 행에 같이 넣지 않는다.
        _summaryLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };

        // 7행: 핵심 버튼(OFS 발주 화면과 동일한 2단계 — 저장(발주확정) → 엑셀파일 내보내기)
        var row7 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 6, 4) };
        _btnExportOrder = new Button { Text = "엑셀파일 내보내기", Size = new Size(120, 32) };
        _btnSave = new Button { Text = "저장 (발주확정)", Size = new Size(120, 32), Font = new Font(Font, FontStyle.Bold) };
        _btnExportOrder.Click += OnExportOrderClick;
        _btnSave.Click += OnSaveClick;
        row7.Controls.AddRange([_btnExportOrder, _btnSave]);

        mainLayout.Controls.Add(row1, 0, 0);
        mainLayout.Controls.Add(row2, 0, 1);
        mainLayout.Controls.Add(row3, 0, 2);
        mainLayout.Controls.Add(_grid, 0, 3);
        mainLayout.Controls.Add(row5, 0, 4);
        mainLayout.Controls.Add(_summaryLabel, 0, 5);
        mainLayout.Controls.Add(row7, 0, 6);
        Controls.Add(mainLayout);

        SetupGridContextMenu();

        UpdateSummary();
    }

    /// <summary>그리드를 우클릭해도 저장/내보내기/행삭제를 바로 쓸 수 있게 한다 — 하단 버튼 줄이
    /// 화면 크기·DPI 설정에 따라 안 보이는 환경이 있어도 이 메뉴로는 항상 접근 가능하다.</summary>
    private void SetupGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        var saveItem = new ToolStripMenuItem("저장 (발주확정)", null, OnSaveClick);
        var exportItem = new ToolStripMenuItem("엑셀파일 내보내기", null, OnExportOrderClick);
        var removeRowItem = new ToolStripMenuItem("선택 행 삭제", null, OnRemoveRowClick);
        menu.Items.AddRange([saveItem, exportItem, new ToolStripSeparator(), removeRowItem]);

        menu.Opening += (s, e) =>
        {
            removeRowItem.Enabled = _grid.SelectedRows.Count > 0;
        };
        _grid.ContextMenuStrip = menu;
    }

    private void LoadMasterData()
    {
        _channels = _channelConfigRepository.GetAll();
        _channelCombo.DataSource = _channels;
        _channelCombo.DisplayMember = nameof(FboChannelConfigModel.ChannelName);
        _channelCombo.ValueMember = nameof(FboChannelConfigModel.ChannelId);
        if (_channels.Count == 0)
        {
            _receiverInfoLabel.Text = "FBO 채널설정이 없습니다. 데이터 관리 화면에서 먼저 등록하세요.";
        }

        _cskus = _cskuRepository.GetAll().Where(c => c.IsActive).OrderBy(c => c.Csku).ToList();
        _cskuCombo.Items.Clear();
        foreach (var csku in _cskus) _cskuCombo.Items.Add(FormatCskuOption(csku));
    }

    private void GenerateNewFboNo()
    {
        _fboNo = _orderRepository.GenerateNextFboNo(_orderDatePicker.Value.Date);
        _fboNoLabel.Text = _fboNo;
    }

    private FboChannelConfigModel? SelectedChannel() => _channelCombo.SelectedItem as FboChannelConfigModel;

    private FboCskuModel? SelectedCsku()
    {
        var text = _cskuCombo.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;
        var csku = text.Split(" - ", 2)[0].Trim();
        return _cskus.FirstOrDefault(c => c.Csku.Equals(csku, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatCskuOption(FboCskuModel csku) => $"{csku.Csku} - {csku.ItemName} ({csku.QtyPerBox}개입)";

    /// <summary>
    /// TextChanged 대신 TextUpdate를 쓴다 — TextUpdate는 사용자가 실제로 타이핑할 때만 발생하고
    /// 드롭다운에서 항목을 선택해 Text가 채워질 때는 발생하지 않아서, 항목 선택 직후 드롭다운이
    /// 다시 열리는 것을 막을 수 있다. 입력한 검색어로 CSKU/품목명/FBO상품코드를 필터링해 콤보의
    /// 드롭다운 목록 자체를 갈아끼우고(MaxDropDownItems=5라 5행까지는 그대로, 그 이상은 콤보가
    /// 자동으로 스크롤바를 붙인다), 매칭 결과가 있으면 드롭다운을 강제로 펼친다.
    /// </summary>
    private void OnCskuSearchTextUpdate(object? sender, EventArgs e)
    {
        var search = _cskuCombo.Text.Trim();
        var matches = string.IsNullOrEmpty(search)
            ? _cskus
            : _cskus.Where(c =>
                c.Csku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.FboItemCode.Contains(search, StringComparison.OrdinalIgnoreCase))
              .ToList();

        var text = _cskuCombo.Text;
        var selStart = _cskuCombo.SelectionStart;
        var selLength = _cskuCombo.SelectionLength;

        _cskuCombo.BeginUpdate();
        _cskuCombo.Items.Clear();
        foreach (var c in matches) _cskuCombo.Items.Add(FormatCskuOption(c));
        _cskuCombo.EndUpdate();

        // Items를 갈아끼우면 콤보가 Text를 건드릴 수 있어 사용자가 입력 중이던 텍스트/커서 위치를
        // 그대로 복원한다.
        _cskuCombo.Text = text;
        _cskuCombo.SelectionStart = selStart;
        _cskuCombo.SelectionLength = selLength;

        _cskuCombo.DroppedDown = !string.IsNullOrEmpty(search) && matches.Count > 0;
    }

    /// <summary>과거 발주의 채널/박스/품목 구성을 그대로 이 폼에 채운다. 박스번호는
    /// RecomputeBoxIdentifiers()로 새로 채번되므로 원본 박스번호는 그대로 쓰지 않는다.</summary>
    private void ApplyTemplate(FboOrder templateOrder, List<FboBox> templateBoxes, List<FboBoxItem> templateItems)
    {
        var channel = _channels.FirstOrDefault(c => c.ChannelId == templateOrder.ChannelId);
        if (channel != null) _channelCombo.SelectedItem = channel;

        var boxTypeBySeq = templateBoxes.ToDictionary(b => b.BoxSeq, b => b.BoxType);

        _rows.Clear();
        foreach (var item in templateItems.OrderBy(i => i.BoxSeq).ThenBy(i => i.ItemSeq))
        {
            _rows.Add(new FboBoxItemRow
            {
                BoxSeq = item.BoxSeq,
                Csku = item.Csku,
                FboItemCode = item.FboItemCode,
                ItemName = item.ItemName,
                InvoiceDisplayName = item.InvoiceDisplayName,
                QtyPerBox = item.QtyPerBox,
                Qty = item.Qty,
                ExpiryDate = item.ExpiryDate,
                BoxType = boxTypeBySeq.GetValueOrDefault(item.BoxSeq, "소"),
            });
        }
        RecomputeBoxIdentifiers();
        _summaryLabel.Text = $"과거 발주 {templateOrder.FboNo}를(을) 복사했습니다 — 내용을 확인하고 저장하세요.  " + _summaryLabel.Text;
    }

    /// <summary>
    /// 발주 작성 화면에서 바로 과거 발주를 골라 그 구성을 불러온다. FboHistoryForm(발주 이력
    /// 조회)의 "복사하여 신규 발주"와 같은 기능이지만, 다른 창을 거치지 않고 바로 쓸 수 있게
    /// 여기에도 둔다 — 매번 CSKU를 새로 고르지 않고 과거에 나갔던 구성 그대로 새 발주를 만드는
    /// 것이 핵심 목적이었으므로 발주 작성 화면 자체에 있는 편이 자연스럽다.
    /// </summary>
    private void OnLoadPastOrderClick(object? sender, EventArgs e)
    {
        if (_isSaved)
        {
            MessageBox.Show("이미 저장된 발주입니다. 새 발주 작성 화면(풀필먼트 발주 처리)을 새로 열어서 불러오세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_rows.Count > 0)
        {
            var confirm = MessageBox.Show("현재 입력된 내용을 지우고 과거 발주를 불러오시겠습니까?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
        }

        using var dialog = new FboOrderPickerDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedFboNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(dialog.SelectedFboNo);
        if (order == null)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ApplyTemplate(order, boxes, items);
    }

    /// <summary>
    /// 발주 품목을 매번 새로 고르지 않고, CSKU별로 최근에 실제 나갔던 박스/품목 구성을 그대로
    /// 가져와 담는다. "지난 발주 불러오기"가 발주 1건 전체를 통째로 복사하는 것과 달리, 이건
    /// 필요한 CSKU만 골라 현재 작성 중인 발주에 추가하는 용도라 기존 행을 지우지 않고 이어붙인다
    /// (CSKU 검색하여 추가와 같은 방식). CSKU당 최근 2개 발주일, 최근에 나간 CSKU 30종까지만
    /// 후보로 보여준다(FboOrderRepository.GetRecentCskuGroups).
    /// </summary>
    private void OnLoadRecentCskuClick(object? sender, EventArgs e)
    {
        if (!EnsureChannelSelected(out _)) return;

        var recentGroups = _orderRepository.GetRecentCskuGroups();
        if (recentGroups.Count == 0)
        {
            MessageBox.Show("과거에 나간 발주 이력이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FboRecentCskuPickerDialog(recentGroups);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        foreach (var group in dialog.SelectedGroups) AddRecentCskuGroup(group);
        RecomputeBoxIdentifiers();
    }

    /// <summary>선택한 지난 CSKU 스냅샷의 박스/품목 구성을 그대로 현재 발주에 새 박스로 추가한다
    /// (원본 박스번호는 그대로 쓰지 않고 뒤에 새로 이어붙인다).</summary>
    private void AddRecentCskuGroup(FboRecentCskuGroup group)
    {
        var nextBoxSeq = _rows.Count == 0 ? 1 : _rows.Max(r => r.BoxSeq) + 1;
        var boxSeqRemap = group.Items.Select(i => i.BoxSeq).Distinct().OrderBy(x => x)
            .Select((origSeq, idx) => (origSeq, newSeq: nextBoxSeq + idx))
            .ToDictionary(x => x.origSeq, x => x.newSeq);

        foreach (var item in group.Items.OrderBy(i => i.BoxSeq).ThenBy(i => i.ItemSeq))
        {
            _rows.Add(new FboBoxItemRow
            {
                BoxSeq = boxSeqRemap[item.BoxSeq],
                Csku = item.Csku,
                FboItemCode = item.FboItemCode,
                ItemName = item.ItemName,
                InvoiceDisplayName = item.InvoiceDisplayName,
                QtyPerBox = item.QtyPerBox,
                Qty = item.Qty,
                ExpiryDate = item.ExpiryDate,
                BoxType = group.BoxTypeBySeq.GetValueOrDefault(item.BoxSeq, "소"),
            });
        }
    }

    private void OnOrderDateChanged(object? sender, EventArgs e)
    {
        if (_isSaved) return;
        GenerateNewFboNo();
        RecomputeBoxIdentifiers();
    }

    private void OnChannelComboChanged(object? sender, EventArgs e)
    {
        var channel = SelectedChannel();
        _receiverInfoLabel.Text = channel == null
            ? "채널을 먼저 선택하세요"
            : $"{channel.ReceiverName} / {channel.Phone} / {channel.Address}";
        if (!_isSaved) RecomputeBoxIdentifiers();
    }

    /// <summary>박스 번호가 추가/삭제로 바뀔 때마다 1..N으로 다시 채번하고, 반품부명·매칭키를
    /// 현재 채널 설정 기준으로 다시 계산한다(기획서 §5.2 — 박스 추가/삭제 시 자동 재채번).</summary>
    private void RecomputeBoxIdentifiers()
    {
        var channel = SelectedChannel();
        if (channel == null || _rows.Count == 0) return;

        var dailySeq = FboKeyGenerator.ExtractDailySeq(_fboNo);
        var orderedBoxSeqs = _rows.Select(r => r.BoxSeq).Distinct().OrderBy(x => x).ToList();
        var remap = new Dictionary<int, int>();
        for (int i = 0; i < orderedBoxSeqs.Count; i++) remap[orderedBoxSeqs[i]] = i + 1;

        foreach (var row in _rows)
        {
            row.BoxSeq = remap[row.BoxSeq];
            row.ReceiverDisplayName = FboKeyGenerator.FormatReceiverName(channel.ReceiverSeqFormat, channel.ReceiverName, row.BoxSeq);
            row.MatchKey = FboKeyGenerator.BuildMatchKey(channel.OrderNoPrefix, _orderDatePicker.Value.Date, dailySeq, row.BoxSeq);
        }
        _rows.ResetBindings();
        UpdateSummary();
    }

    /// <summary>
    /// FBO 품목마스터(박스당수량/박스유형 등)·채널설정은 데이터관리창에서 관리한다. 이 화면에서
    /// 바로 그 탭으로 이동할 수 있게 안내하고, 닫고 돌아오면 CSKU 콤보(박스당수량 표기 포함)를
    /// 새로고침한다(관리창에서 값을 바꿨을 수 있으므로).
    /// </summary>
    private void OnManageItemsClick(object? sender, EventArgs e)
    {
        FormManager.Show<DataManagementForm>();
        Application.OpenForms.OfType<DataManagementForm>().FirstOrDefault()?.SelectTabByDisplayName("FBO 품목마스터");
        LoadMasterData();
    }

    private void OnCalcBoxesClick(object? sender, EventArgs e)
    {
        if (!EnsureChannelSelected(out var channel)) return;
        var csku = SelectedCsku();
        if (csku == null)
        {
            MessageBox.Show("CSKU를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AddBoxesForCsku(csku, (int)_boxCountInput.Value);
        RecomputeBoxIdentifiers();
    }

    /// <summary>
    /// CSKU 콤보 검색이 한 번에 하나씩만 골라야 해서 여러 품목을 담는 발주서 작성 시 반복이
    /// 불편하다는 지적에 따라 신설. 등록된 CSKU 전부를 보여주는 선택창에서 여러 건을 체크하거나
    /// 행을 더블클릭해 골라, 선택한 각 CSKU마다 (현재 "박스수" 입력값만큼) 박스를 한 번에 추가한다.
    /// </summary>
    private void OnCskuPickerClick(object? sender, EventArgs e)
    {
        if (!EnsureChannelSelected(out _)) return;

        using var dialog = new FboCskuPickerDialog(_cskus);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var boxCount = (int)_boxCountInput.Value;
        foreach (var csku in dialog.SelectedCskus) AddBoxesForCsku(csku, boxCount);
        RecomputeBoxIdentifiers();
    }

    private bool EnsureChannelSelected(out FboChannelConfigModel? channel)
    {
        channel = SelectedChannel();
        if (channel != null) return true;
        MessageBox.Show("채널을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    /// <summary>지정한 CSKU로 boxCount개의 박스(각 박스=이 CSKU 1종, 박스당수량만큼)를 새로 추가한다.
    /// 박스번호/반품부명/매칭키는 호출 측에서 RecomputeBoxIdentifiers()로 다시 채번해야 한다.</summary>
    private void AddBoxesForCsku(FboCskuModel csku, int boxCount)
    {
        var expiry = _expiryCheckBox.Checked ? _expiryPicker.Value.ToString("yyyyMMdd") : null;
        var nextBoxSeq = _rows.Count == 0 ? 1 : _rows.Max(r => r.BoxSeq) + 1;

        for (int i = 0; i < boxCount; i++)
        {
            _rows.Add(new FboBoxItemRow
            {
                BoxSeq = nextBoxSeq + i,
                Csku = csku.Csku,
                FboItemCode = csku.FboItemCode,
                ItemName = csku.ItemName,
                InvoiceDisplayName = csku.InvoiceDisplayName,
                QtyPerBox = csku.QtyPerBox,
                Qty = csku.QtyPerBox,
                ExpiryDate = expiry,
                BoxType = csku.BoxType,
            });
        }
    }

    private void OnAddRowClick(object? sender, EventArgs e)
    {
        var csku = SelectedCsku();
        if (csku == null)
        {
            MessageBox.Show("추가할 CSKU를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_rows.Count == 0)
        {
            MessageBox.Show("박스를 먼저 산정하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targetRow = _grid.CurrentRow?.DataBoundItem as FboBoxItemRow;
        var targetBoxSeq = targetRow?.BoxSeq ?? _rows.Max(r => r.BoxSeq);
        var boxSample = _rows.First(r => r.BoxSeq == targetBoxSeq);

        _rows.Add(new FboBoxItemRow
        {
            BoxSeq = targetBoxSeq,
            ReceiverDisplayName = boxSample.ReceiverDisplayName,
            MatchKey = boxSample.MatchKey,
            Csku = csku.Csku,
            FboItemCode = csku.FboItemCode,
            ItemName = csku.ItemName,
            InvoiceDisplayName = csku.InvoiceDisplayName,
            QtyPerBox = csku.QtyPerBox,
            Qty = csku.QtyPerBox,
            ExpiryDate = boxSample.ExpiryDate,
            BoxType = boxSample.BoxType,
        });
        UpdateSummary();
    }

    private void OnRemoveRowClick(object? sender, EventArgs e)
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as FboBoxItemRow)
            .Where(r => r != null)
            .Cast<FboBoxItemRow>()
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("삭제할 행을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (var row in selected) _rows.Remove(row);
        RecomputeBoxIdentifiers();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var boxCount = _rows.Select(r => r.BoxSeq).Distinct().Count();
        var totalQty = _rows.Sum(r => r.Qty);
        _summaryLabel.Text = $"박스 {boxCount}개 / 품목줄 {_rows.Count} / 총수량 {totalQty}개";
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var channel = SelectedChannel();
        if (channel == null)
        {
            MessageBox.Show("채널을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_rows.Count == 0)
        {
            MessageBox.Show("박스를 하나 이상 추가하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_rows.Any(r => r.Qty < 1))
        {
            MessageBox.Show("수량이 1 미만인 행이 있습니다.", "저장 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var warnBoxes = _rows.GroupBy(r => r.BoxSeq)
            .Where(g => g.Select(r => r.Csku).Distinct().Count() >= 3)
            .Select(g => g.Key)
            .ToList();
        if (warnBoxes.Count > 0)
        {
            MessageBox.Show(
                $"박스 {string.Join(", ", warnBoxes)}에 서로 다른 CSKU가 3종 이상 있습니다. 저장은 계속 진행됩니다.",
                "합포장 경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        FboOrder? existingOrder = null;
        List<FboBox> existingBoxes = [];
        if (_isSaved)
        {
            (existingOrder, existingBoxes, _) = _orderRepository.GetOrder(_fboNo);
        }
        var existingTrackingByBox = existingBoxes.ToDictionary(b => b.BoxSeq, b => b);

        var order = new FboOrder
        {
            FboNo = _fboNo,
            OrderDate = _orderDatePicker.Value.Date,
            ChannelId = channel.ChannelId,
            ReceiverName = channel.ReceiverName,
            Phone = channel.Phone,
            Address = channel.Address,
            Status = existingOrder?.Status ?? "작성중",
            CreatedAt = existingOrder?.CreatedAt ?? DateTime.Now,
        };

        var boxes = _rows.GroupBy(r => r.BoxSeq).Select(g =>
        {
            var first = g.First();
            existingTrackingByBox.TryGetValue(g.Key, out var existingBox);
            return new FboBox
            {
                FboNo = _fboNo,
                BoxSeq = g.Key,
                ReceiverDisplayName = first.ReceiverDisplayName,
                MatchKey = first.MatchKey,
                BoxType = first.BoxType,
                TrackingNo = existingBox?.TrackingNo,
                TrackingLoadedAt = existingBox?.TrackingLoadedAt,
                Status = existingBox?.Status ?? "대기",
            };
        }).ToList();

        var items = _rows.GroupBy(r => r.BoxSeq).SelectMany(g => g.Select((row, index) => new FboBoxItem
        {
            FboNo = _fboNo,
            BoxSeq = g.Key,
            ItemSeq = index + 1,
            Csku = row.Csku,
            FboItemCode = row.FboItemCode,
            ItemName = row.ItemName,
            InvoiceDisplayName = row.InvoiceDisplayName,
            QtyPerBox = row.QtyPerBox,
            Qty = row.Qty,
            ExpiryDate = string.IsNullOrWhiteSpace(row.ExpiryDate) ? null : row.ExpiryDate,
        })).ToList();

        _orderRepository.SaveOrder(order, boxes, items);
        _isSaved = true;
        _orderDatePicker.Enabled = false;
        _channelCombo.Enabled = false;
        UpdateSummary();
        _summaryLabel.Text += $"  —  저장 완료 ({DateTime.Now:HH:mm:ss})";
    }

    private void OnExportOrderClick(object? sender, EventArgs e)
    {
        if (!_isSaved)
        {
            MessageBox.Show("먼저 저장하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var channel = SelectedChannel()!;
        var (order, boxes, items) = _orderRepository.GetOrder(_fboNo);
        if (order == null || boxes.Count == 0) return;

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"풀필먼트출고_{_fboNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("FboOrderExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("FboOrderExport", Path.GetDirectoryName(sfd.FileName)!);

        try
        {
            FboOrderExporter.Export(order, channel, boxes, items, sfd.FileName);
            order.Status = "발주서출력완료";
            _orderRepository.SaveOrder(order, boxes, items);
            ExportHelper.ShowPostExportDialog(this, sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportTrackingClick(object? sender, EventArgs e)
    {
        if (!_isSaved)
        {
            MessageBox.Show("먼저 저장하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "이송장 결과 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("FboTrackingImport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("FboTrackingImport", Path.GetDirectoryName(ofd.FileName)!);

        try
        {
            var result = new FboTrackingImporter(_orderRepository).Import(ofd.FileName);
            if (!result.Success)
            {
                var msg = "미매칭/불일치가 있어 아무것도 반영되지 않았습니다.\n\n";
                if (result.UnmatchedRows.Count > 0) msg += $"[미매칭]\n{string.Join("\n", result.UnmatchedRows)}\n\n";
                if (result.InconsistentBoxes.Count > 0) msg += $"[이송장번호 불일치]\n{string.Join("\n", result.InconsistentBoxes)}";
                MessageBox.Show(msg, "운송장 불러오기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RefreshTrackingFromDb();
            _summaryLabel.Text = $"운송장번호 {result.AppliedCount}건을 적용했습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshTrackingFromDb()
    {
        var (_, boxes, _) = _orderRepository.GetOrder(_fboNo);
        var trackingByBox = boxes.ToDictionary(b => b.BoxSeq, b => b.TrackingNo);
        foreach (var row in _rows)
        {
            if (trackingByBox.TryGetValue(row.BoxSeq, out var trackingNo)) row.TrackingNo = trackingNo;
        }
        _rows.ResetBindings();
    }

    private void OnExportInboundClick(object? sender, EventArgs e)
    {
        if (!_isSaved)
        {
            MessageBox.Show("먼저 저장하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var channel = SelectedChannel()!;
        var (order, boxes, items) = _orderRepository.GetOrder(_fboNo);
        if (order == null) return;

        if (boxes.Any(b => string.IsNullOrEmpty(b.TrackingNo)))
        {
            MessageBox.Show("이송장번호가 없는 박스가 있습니다. 먼저 운송장을 불러오세요.", "내보내기 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"풀필먼트입고_{_fboNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("FboInboundExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("FboInboundExport", Path.GetDirectoryName(sfd.FileName)!);

        try
        {
            FboInboundExporter.Export(channel, boxes, items, sfd.FileName);
            order.Status = "입고재고출력완료";
            _orderRepository.SaveOrder(order, boxes, items);
            ExportHelper.ShowPostExportDialog(this, sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
