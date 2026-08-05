using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 아마존 FBA 발주 처리 화면. CSKU를 먼저 "미배정 품목" 풀(BoxSeq=0)에 담고, 그 다음 박스를
/// 추가해 담긴 품목을 원하는 박스로 옮기는 "품목 우선" 흐름이다 — 발송할 품목을 그때그때 확정해
/// 담아두다가 박스 수량이 찰 때 한꺼번에 박스로 정리하기 위함이다. 미배정 품목은 장바구니로
/// 임시저장(1회, 전체교체)해뒀다가 다음에 이 화면을 열 때 불러올 수 있다(FbaCartRepository).
/// 수취지가 FbaConfig 단일 행으로 고정이라 FBO의 채널 콤보 대신 요약 라벨만 보여준다. 그리드는
/// 박스 첫 행에만 박스규격/치수/무게를 표시해 시각적으로 박스 단위 그룹처럼 보이게 한다(미배정
/// 풀은 박스 속성이 없으므로 항상 비워 보여준다).
/// </summary>
public class FbaOrderForm : Form
{
    private readonly FbaCskuRepository _cskuRepository = new();
    private readonly FbaBoxSpecRepository _boxSpecRepository = new();
    private readonly FbaConfigRepository _configRepository = new();
    private readonly FbaOrderRepository _orderRepository = new();
    private readonly FbaCartRepository _cartRepository = new();
    private readonly SettingsService _settingsService = new();

    private string _fbaNo = string.Empty;
    private bool _isSaved;
    private FbaConfigModel _config = new();
    private List<FbaBoxSpec> _boxSpecs = [];
    private List<FbaCskuModel> _cskus = [];
    private readonly BindingList<FbaGridRow> _rows = [];
    // 사용자가 박스무게 칸을 직접 편집한 박스는 여기 기록해, Qty가 바뀌어도 자동 재계산으로
    // 덮어쓰지 않는다(기획서 §3.4 — 자동계산값이지만 그리드에서 수동 덮어쓰기 가능).
    private readonly HashSet<int> _manualWeightBoxes = [];

    private Label _fbaNoLabel = new();
    private DateTimePicker _orderDatePicker = new();
    private TextBox _shipmentIdInput = new();
    private Label _receiverInfoLabel = new();
    private ComboBox _boxSpecCombo = new();
    private ComboBox _cskuCombo = new();
    private ExcelLikeDataGridView _grid = new();
    private Label _summaryLabel = new();
    private Button _btnSave = new();

    private static readonly FbaBoxSpec CustomSizeSentinel = new() { BoxName = "(직접입력)" };

    private static readonly HashSet<string> BoxLevelColumns =
    [
        nameof(FbaGridRow.BoxSeq), nameof(FbaGridRow.BoxSpecName), nameof(FbaGridRow.SizeDisplay), nameof(FbaGridRow.WeightG), nameof(FbaGridRow.TrackingNo),
    ];

    public FbaOrderForm()
    {
        InitializeComponent();
        LoadMasterData();
        GenerateNewFbaNo();
    }

    /// <summary>과거 발주 이력에서 "복사하여 신규 발주"로 진입할 때 쓰는 생성자(FboOrderForm과 동일
    /// 목적). ShipmentId는 1회 발주=1 Shipment 원칙상 새로 입력해야 하므로 복사하지 않는다.</summary>
    public FbaOrderForm(FbaOrder templateOrder, List<FbaBox> templateBoxes, List<FbaBoxItem> templateItems) : this()
    {
        ApplyTemplate(templateOrder, templateBoxes, templateItems);
    }

    private class FbaGridRow
    {
        /// <summary>0이면 아직 박스에 배정되지 않은 "미배정 품목" 풀, 1 이상이면 실제 박스번호.</summary>
        public int BoxSeq { get; set; }
        public string BoxSpecName { get; set; } = string.Empty;
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double HeightMm { get; set; }
        public bool IsCustomSize { get; set; }
        public double WeightG { get; set; }
        public string MatchKey { get; set; } = string.Empty;
        public string? TrackingNo { get; set; }
        public string BoxStatus { get; set; } = "대기";
        /// <summary>실제 박스(BoxSeq&gt;=1)는 추가됐지만 아직 품목이 하나도 배정되지 않은 상태를
        /// 그리드에 붙잡아 두기 위한 자리표시 행. 저장 전에 반드시 품목이 배정되거나 박스째
        /// 삭제되어야 한다. 미배정 풀(BoxSeq=0)에는 쓰이지 않는다.</summary>
        public bool IsPlaceholder { get; set; }
        public string Csku { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? InvoiceDisplayName { get; set; }
        public string Asin { get; set; } = string.Empty;
        public string CommodityDescription { get; set; } = string.Empty;
        public string HsCode { get; set; } = string.Empty;
        public decimal ItemPrice { get; set; }
        public double UnitWeightG { get; set; }
        public int QtyPerLayer { get; set; }
        public int Qty { get; set; }
        public string? ExpiryDate { get; set; }

        public string SizeDisplay => $"{WidthMm:0}×{DepthMm:0}×{HeightMm:0}";
    }

    private void InitializeComponent()
    {
        Text = "FBA 발주 처리";
        Size = new Size(1040, 700);
        MinimumSize = new Size(820, 540);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        // 1행: 발주번호/발주일/Shipment ID/수취지 요약
        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 0, 0) };
        _fbaNoLabel = new Label { Text = "(자동)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 3, 12, 0) };
        _orderDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _orderDatePicker.ValueChanged += OnOrderDateChanged;
        _shipmentIdInput = new TextBox { Width = 140, PlaceholderText = "미입력 허용" };
        _shipmentIdInput.TextChanged += (s, e) => RecalculateMatchKeys();
        _receiverInfoLabel = new Label { Text = "발주지 설정을 불러오는 중...", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 3, 0, 0) };
        row1.Controls.AddRange(
        [
            new Label { Text = "발주번호:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) }, _fbaNoLabel,
            new Label { Text = "발주일:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) }, _orderDatePicker,
            new Label { Text = "Shipment ID:", AutoSize = true, Padding = new Padding(12, 3, 4, 0) }, _shipmentIdInput,
            _receiverInfoLabel,
        ]);

        // 2행: 품목 조작줄 — 품목을 먼저 미배정 풀에 담는다(품목 우선 흐름의 1단계).
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0) };
        _cskuCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, MaxDropDownItems = 5, Width = 260 };
        _cskuCombo.TextUpdate += OnCskuSearchTextUpdate;
        var btnAddCsku = new Button { Text = "CSKU 추가", Size = new Size(90, 26), Margin = new Padding(6, 0, 0, 0) };
        var btnCskuPicker = new Button { Text = "CSKU 검색하여 추가", Size = new Size(130, 26), Margin = new Padding(6, 0, 0, 0) };
        var btnRemoveItem = new Button { Text = "선택 품목 삭제", Size = new Size(100, 26), Margin = new Padding(12, 0, 0, 0) };
        var btnManageItems = new Button { Text = "품목/설정 관리", Size = new Size(100, 26), Margin = new Padding(12, 0, 0, 0) };
        var btnSaveCart = new Button { Text = "장바구니 임시저장", Size = new Size(110, 26), Margin = new Padding(12, 0, 0, 0) };
        var btnLoadCart = new Button { Text = "장바구니 불러오기", Size = new Size(110, 26), Margin = new Padding(6, 0, 0, 0) };
        btnAddCsku.Click += OnAddCskuClick;
        btnCskuPicker.Click += OnCskuPickerClick;
        btnRemoveItem.Click += OnRemoveItemClick;
        btnManageItems.Click += OnManageItemsClick;
        btnSaveCart.Click += OnSaveCartClick;
        btnLoadCart.Click += OnLoadCartClick;
        row2.Controls.AddRange(
        [
            new Label { Text = "CSKU:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) }, _cskuCombo,
            btnAddCsku, btnCskuPicker, btnRemoveItem, btnManageItems, btnSaveCart, btnLoadCart,
        ]);

        // 3행: 박스 조작줄 — 미배정 풀에 담긴 품목을 골라 박스로 옮긴다(품목 우선 흐름의 2단계).
        var row3 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0) };
        _boxSpecCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, FormattingEnabled = true, DisplayMember = nameof(FbaBoxSpec.BoxName) };
        _boxSpecCombo.Format += (s, e) =>
        {
            if (e.ListItem is FbaBoxSpec spec)
            {
                e.Value = spec.BoxName == CustomSizeSentinel.BoxName ? spec.BoxName : $"{spec.BoxName} ({spec.WidthMm:0}×{spec.DepthMm:0}×{spec.HeightMm:0})";
            }
        };
        var btnAddBox = new Button { Text = "박스 추가", Size = new Size(90, 26), Margin = new Padding(6, 0, 0, 0) };
        var btnRemoveBox = new Button { Text = "박스 삭제", Size = new Size(90, 26), Margin = new Padding(6, 0, 0, 0) };
        var btnAssignToBox = new Button { Text = "선택 품목 박스에 담기", Size = new Size(140, 26), Margin = new Padding(12, 0, 0, 0) };
        btnAddBox.Click += OnAddBoxClick;
        btnRemoveBox.Click += OnRemoveBoxClick;
        btnAssignToBox.Click += OnAssignToBoxClick;
        row3.Controls.AddRange(
        [
            new Label { Text = "박스규격:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) }, _boxSpecCombo,
            btnAddBox, btnRemoveBox, btnAssignToBox,
        ]);

        // 4행: 그리드 — ExcelLikeDataGridView를 써야 셀 하나만 선택한 상태에서 복사할 때 그
        // 행 전체가 아니라 클릭한 셀 하나만 복사된다(FullRowSelect 모드의 기본 동작 문제, 다른
        // 그리드들도 겪은 문제라 이 컨트롤에서 이미 고쳐져 있다).
        _grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스", DataPropertyName = nameof(FbaGridRow.BoxSeq), ReadOnly = true, Width = 45, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스규격", DataPropertyName = nameof(FbaGridRow.BoxSpecName), ReadOnly = true, Width = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "치수(mm)", DataPropertyName = nameof(FbaGridRow.SizeDisplay), ReadOnly = true, Width = 110, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CSKU", DataPropertyName = nameof(FbaGridRow.Csku), ReadOnly = true, Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "품목명", DataPropertyName = nameof(FbaGridRow.ItemName), ReadOnly = true, Width = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "1단수량", DataPropertyName = nameof(FbaGridRow.QtyPerLayer), ReadOnly = true, Width = 60, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "수량", DataPropertyName = nameof(FbaGridRow.Qty), Width = 60, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "유통기한(YYYYMMDD)", DataPropertyName = nameof(FbaGridRow.ExpiryDate), Width = 130, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스무게(g)", DataPropertyName = nameof(FbaGridRow.WeightG), Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "운송장번호", DataPropertyName = nameof(FbaGridRow.TrackingNo), Width = 110, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.DataSource = _rows;
        _grid.CellFormatting += OnGridCellFormatting;
        _grid.CellBeginEdit += OnGridCellBeginEdit;
        _grid.CellEndEdit += OnGridCellEndEdit;

        // 5행: 요약 라벨 — 버튼과 같은 TableLayoutPanel 행에 두면 열 너비 계산이 꼬이는 문제가
        // 있어(FboOrderForm과 동일 이유) 라벨 행과 버튼 행을 분리한다.
        _summaryLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };

        // 6행: 핵심 버튼 4개(§4). 하배출고이서/운송장 불러오기/선적명세는 각각 M3/M4/M5에서 결선한다.
        var row6 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 6, 4) };
        var btnExportShipment = new Button { Text = "아마존 선적명세 내보내기", Size = new Size(150, 32) };
        var btnImportTracking = new Button { Text = "운송장 불러오기", Size = new Size(110, 32) };
        var btnExportCourier = new Button { Text = "하배출고이서 내보내기", Size = new Size(130, 32) };
        _btnSave = new Button { Text = "저장 (발주확정)", Size = new Size(120, 32), Font = new Font(Font, FontStyle.Bold) };
        btnExportShipment.Click += OnExportShipmentClick;
        btnImportTracking.Click += OnImportTrackingClick;
        btnExportCourier.Click += OnExportCourierClick;
        _btnSave.Click += OnSaveClick;
        row6.Controls.AddRange([btnExportShipment, btnImportTracking, btnExportCourier, _btnSave]);

        mainLayout.Controls.Add(row1, 0, 0);
        mainLayout.Controls.Add(row2, 0, 1);
        mainLayout.Controls.Add(row3, 0, 2);
        mainLayout.Controls.Add(_grid, 0, 3);
        mainLayout.Controls.Add(_summaryLabel, 0, 4);
        mainLayout.Controls.Add(row6, 0, 5);
        Controls.Add(mainLayout);

        UpdateSummary();
    }

    private void LoadMasterData()
    {
        LoadBoxSpecs();

        _config = _configRepository.GetDefault();
        _receiverInfoLabel.Text = string.IsNullOrWhiteSpace(_config.ReceiverName)
            ? "FBA 발주지설정이 없습니다. 데이터 관리 화면에서 먼저 등록하세요."
            : $"{_config.ReceiverName} / {_config.Phone} / {_config.Address}";

        _cskus = _cskuRepository.GetAll().Where(c => c.IsActive).OrderBy(c => c.Csku).ToList();
        _cskuCombo.Items.Clear();
        foreach (var csku in _cskus) _cskuCombo.Items.Add(FormatCskuOption(csku));
    }

    private void LoadBoxSpecs()
    {
        _boxSpecs = _boxSpecRepository.GetAll().Where(s => s.IsActive).ToList();
        var comboSource = new List<FbaBoxSpec>(_boxSpecs) { CustomSizeSentinel };
        _boxSpecCombo.DataSource = null;
        _boxSpecCombo.DataSource = comboSource;
    }

    private void GenerateNewFbaNo()
    {
        _fbaNo = _orderRepository.GenerateNextFbaNo(_orderDatePicker.Value.Date);
        _fbaNoLabel.Text = _fbaNo;
    }

    private static string FormatCskuOption(FbaCskuModel csku) => $"{csku.Csku} - {csku.ItemName} ({csku.QtyPerLayer}개/1단)";

    private FbaCskuModel? SelectedCsku()
    {
        var text = _cskuCombo.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;
        var csku = text.Split(" - ", 2)[0].Trim();
        return _cskus.FirstOrDefault(c => c.Csku.Equals(csku, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>FboOrderForm의 CSKU 검색 콤보와 동일한 방식 — TextUpdate로 입력할 때마다 후보를
    /// 직접 필터링해 드롭다운 목록을 갈아끼운다.</summary>
    private void OnCskuSearchTextUpdate(object? sender, EventArgs e)
    {
        var search = _cskuCombo.Text.Trim();
        var matches = string.IsNullOrEmpty(search)
            ? _cskus
            : _cskus.Where(c =>
                c.Csku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.Asin.Contains(search, StringComparison.OrdinalIgnoreCase))
              .ToList();

        var text = _cskuCombo.Text;
        var selStart = _cskuCombo.SelectionStart;
        var selLength = _cskuCombo.SelectionLength;

        _cskuCombo.BeginUpdate();
        _cskuCombo.Items.Clear();
        foreach (var c in matches) _cskuCombo.Items.Add(FormatCskuOption(c));
        _cskuCombo.EndUpdate();

        _cskuCombo.Text = text;
        _cskuCombo.SelectionStart = selStart;
        _cskuCombo.SelectionLength = selLength;
        _cskuCombo.DroppedDown = !string.IsNullOrEmpty(search) && matches.Count > 0;
    }

    private void OnOrderDateChanged(object? sender, EventArgs e)
    {
        if (_isSaved) return;
        GenerateNewFbaNo();
    }

    private void OnManageItemsClick(object? sender, EventArgs e)
    {
        FormManager.Show<DataManagementForm>();
        Application.OpenForms.OfType<DataManagementForm>().FirstOrDefault()?.SelectTabByDisplayName("FBA 품목마스터");
        LoadMasterData();
    }

    private void OnAddBoxClick(object? sender, EventArgs e)
    {
        var selected = _boxSpecCombo.SelectedItem as FbaBoxSpec;
        if (selected == null)
        {
            MessageBox.Show("박스규격을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string boxSpecName;
        double width, depth, height;
        bool isCustomSize;

        if (selected.BoxName == CustomSizeSentinel.BoxName)
        {
            using var dialog = new FbaBoxSpecInputDialog();
            if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;
            boxSpecName = dialog.BoxSpecName;
            width = dialog.WidthMm;
            depth = dialog.DepthMm;
            height = dialog.HeightMm;
            isCustomSize = true;
            LoadBoxSpecs();
        }
        else
        {
            boxSpecName = selected.BoxName;
            width = selected.WidthMm;
            depth = selected.DepthMm;
            height = selected.HeightMm;
            isCustomSize = false;
        }

        var nextBoxSeq = _rows.Count == 0 ? 1 : _rows.Max(r => r.BoxSeq) + 1;
        _rows.Add(new FbaGridRow
        {
            BoxSeq = nextBoxSeq,
            BoxSpecName = boxSpecName,
            WidthMm = width,
            DepthMm = depth,
            HeightMm = height,
            IsCustomSize = isCustomSize,
            IsPlaceholder = true,
            ItemName = "(CSKU를 추가하세요)",
        });

        RecomputeBoxIdentifiers();
        SelectBoxInGrid(nextBoxSeq);
    }

    /// <summary>박스를 삭제한다. 담겨있던 품목은 버리지 않고 미배정 풀(BoxSeq=0)로 되돌린다 —
    /// 품목 우선 흐름에서는 품목이 주된 자원이고 박스는 그저 담는 그릇이므로, 박스를 잘못
    /// 만들었다고 애써 고른 품목까지 잃을 필요는 없다.</summary>
    private void OnRemoveBoxClick(object? sender, EventArgs e)
    {
        var currentRow = _grid.CurrentRow?.DataBoundItem as FbaGridRow;
        if (currentRow == null || currentRow.BoxSeq == 0)
        {
            MessageBox.Show("삭제할 박스를 그리드에서 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var boxSeq = currentRow.BoxSeq;
        var hasItems = _rows.Any(r => r.BoxSeq == boxSeq && !r.IsPlaceholder);
        var confirmMessage = hasItems
            ? $"박스 {boxSeq}번을 삭제하시겠습니까? 담긴 품목은 미배정 목록으로 돌아갑니다."
            : $"박스 {boxSeq}번을 삭제하시겠습니까?";
        var confirm = MessageBox.Show(confirmMessage, "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        foreach (var row in _rows.Where(r => r.BoxSeq == boxSeq).ToList())
        {
            if (row.IsPlaceholder)
            {
                _rows.Remove(row);
                continue;
            }
            row.BoxSeq = 0;
            row.BoxSpecName = string.Empty;
            row.WidthMm = 0;
            row.DepthMm = 0;
            row.HeightMm = 0;
            row.IsCustomSize = false;
            row.WeightG = 0;
            row.MatchKey = string.Empty;
            row.TrackingNo = null;
            row.BoxStatus = "대기";
        }
        _manualWeightBoxes.Remove(boxSeq);
        RecomputeBoxIdentifiers();
        RecalculateWeights();
    }

    /// <summary>미배정 품목을 담을 대상 박스 — 그리드에서 선택 중인 행이 실제 박스(BoxSeq&gt;=1)면
    /// 그 박스, 아니면 마지막(가장 큰 번호) 박스를 기본으로 쓴다. 실제 박스가 하나도 없으면 null.</summary>
    private int? GetTargetBoxSeq()
    {
        var currentRow = _grid.CurrentRow?.DataBoundItem as FbaGridRow;
        if (currentRow is { BoxSeq: > 0 }) return currentRow.BoxSeq;

        var maxBoxSeq = _rows.Where(r => r.BoxSeq > 0).Select(r => r.BoxSeq).DefaultIfEmpty(0).Max();
        return maxBoxSeq > 0 ? maxBoxSeq : null;
    }

    private void OnAddCskuClick(object? sender, EventArgs e)
    {
        var csku = SelectedCsku();
        if (csku == null)
        {
            MessageBox.Show("추가할 CSKU를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AddCskuToPool(csku);
        SortRows();
        RecalculateWeights();
        UpdateSummary();
    }

    private void OnCskuPickerClick(object? sender, EventArgs e)
    {
        using var dialog = new FbaCskuPickerDialog(_cskus);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        foreach (var csku in dialog.SelectedCskus) AddCskuToPool(csku);
        SortRows();
        RecalculateWeights();
        UpdateSummary();
    }

    /// <summary>CSKU를 미배정 풀(BoxSeq=0)에 담는다. 박스는 나중에 이 풀에서 골라 옮긴다
    /// (품목 우선 흐름의 1단계). 수량은 1단수량으로 자동 채운다(수정 가능).</summary>
    private void AddCskuToPool(FbaCskuModel csku) => _rows.Add(CreatePoolRow(csku));

    private static FbaGridRow CreatePoolRow(FbaCskuModel csku, int? qty = null, string? expiryDate = null) => new()
    {
        BoxSeq = 0,
        IsPlaceholder = false,
        Csku = csku.Csku,
        ItemName = csku.ItemName,
        InvoiceDisplayName = csku.InvoiceDisplayName,
        Asin = csku.Asin,
        CommodityDescription = csku.CommodityDescription,
        HsCode = csku.HsCode,
        ItemPrice = csku.ItemPrice,
        UnitWeightG = csku.UnitWeightG,
        QtyPerLayer = csku.QtyPerLayer,
        Qty = qty is > 0 ? qty.Value : csku.QtyPerLayer,
        ExpiryDate = expiryDate,
    };

    /// <summary>그리드에서 선택한 미배정 품목들을 <see cref="GetTargetBoxSeq"/>가 고른 박스로
    /// 옮긴다(품목 우선 흐름의 2단계). 대상 박스가 비어있던 자리표시 행이었으면 걷어낸다.</summary>
    private void OnAssignToBoxClick(object? sender, EventArgs e)
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as FbaGridRow)
            .Where(r => r != null && r.BoxSeq == 0)
            .Cast<FbaGridRow>()
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("박스에 담을 미배정 품목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targetBoxSeq = GetTargetBoxSeq();
        if (targetBoxSeq == null)
        {
            MessageBox.Show("담을 박스를 먼저 추가하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var boxTemplate = _rows.First(r => r.BoxSeq == targetBoxSeq.Value);
        var placeholder = _rows.FirstOrDefault(r => r.BoxSeq == targetBoxSeq.Value && r.IsPlaceholder);
        if (placeholder != null) _rows.Remove(placeholder);

        foreach (var row in selected)
        {
            row.BoxSeq = targetBoxSeq.Value;
            row.BoxSpecName = boxTemplate.BoxSpecName;
            row.WidthMm = boxTemplate.WidthMm;
            row.DepthMm = boxTemplate.DepthMm;
            row.HeightMm = boxTemplate.HeightMm;
            row.IsCustomSize = boxTemplate.IsCustomSize;
            row.TrackingNo = boxTemplate.TrackingNo;
            row.BoxStatus = boxTemplate.BoxStatus;
        }

        RecalculateMatchKeys();
        SortRows();
        RecalculateWeights();
        UpdateSummary();
    }

    private void OnRemoveItemClick(object? sender, EventArgs e)
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as FbaGridRow)
            .Where(r => r != null && !r.IsPlaceholder)
            .Cast<FbaGridRow>()
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("삭제할 품목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 박스가 통째로 비게 되는 경우를 대비해, 삭제 전에 박스 속성을 박스별로 하나씩 미리 챙겨둔다.
        // 미배정 풀(BoxSeq=0)은 자리표시 행이 필요 없으므로 대상에서 제외한다.
        var templatesByBox = selected.Where(r => r.BoxSeq > 0).GroupBy(r => r.BoxSeq).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in selected) _rows.Remove(row);

        foreach (var (boxSeq, template) in templatesByBox)
        {
            if (_rows.Any(r => r.BoxSeq == boxSeq)) continue;
            _rows.Add(new FbaGridRow
            {
                BoxSeq = boxSeq,
                BoxSpecName = template.BoxSpecName,
                WidthMm = template.WidthMm,
                DepthMm = template.DepthMm,
                HeightMm = template.HeightMm,
                IsCustomSize = template.IsCustomSize,
                MatchKey = template.MatchKey,
                TrackingNo = template.TrackingNo,
                BoxStatus = template.BoxStatus,
                IsPlaceholder = true,
                ItemName = "(CSKU를 추가하세요)",
            });
            _manualWeightBoxes.Remove(boxSeq);
        }

        SortRows();
        RecalculateWeights();
        UpdateSummary();
    }

    private void OnSaveCartClick(object? sender, EventArgs e)
    {
        var poolItems = _rows.Where(r => r.BoxSeq == 0 && !r.IsPlaceholder)
            .Select(r => new FbaCartItemModel { Csku = r.Csku, Qty = r.Qty, ExpiryDate = r.ExpiryDate })
            .ToList();
        _cartRepository.SaveAll(poolItems);
        MessageBox.Show($"미배정 품목 {poolItems.Count}건을 장바구니에 임시저장했습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>임시저장된 장바구니를 미배정 풀에 이어붙인다(기존 풀 내용은 유지 — 세션 도중 담아둔
    /// 품목을 덮어쓰지 않도록 명시적으로 불러올 때만 합친다). CSKU 마스터에서 이미 사라진 항목은
    /// 건너뛰고 목록으로 알려준다.</summary>
    private void OnLoadCartClick(object? sender, EventArgs e)
    {
        var cartItems = _cartRepository.GetAll();
        if (cartItems.Count == 0)
        {
            MessageBox.Show("임시저장된 장바구니가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var missing = new List<string>();
        foreach (var item in cartItems)
        {
            var csku = _cskus.FirstOrDefault(c => c.Csku.Equals(item.Csku, StringComparison.OrdinalIgnoreCase));
            if (csku == null)
            {
                missing.Add(item.Csku);
                continue;
            }
            _rows.Add(CreatePoolRow(csku, item.Qty, item.ExpiryDate));
        }

        SortRows();
        RecalculateWeights();
        UpdateSummary();

        if (missing.Count > 0)
        {
            MessageBox.Show($"다음 CSKU는 마스터에서 찾을 수 없어 불러오지 못했습니다:\n{string.Join(", ", missing)}", "일부 항목 누락", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SelectBoxInGrid(int boxSeq)
    {
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].DataBoundItem is FbaGridRow row && row.BoxSeq == boxSeq)
            {
                _grid.CurrentCell = _grid.Rows[i].Cells[0];
                return;
            }
        }
    }

    /// <summary>박스 목록을 화면에 그룹으로 보여주기 위해 항상 박스번호 순으로 재정렬한다(§4 —
    /// 박스번호 기준 그룹 정렬, 박스 첫 행에만 박스 속성 표시).</summary>
    private void SortRows()
    {
        var sorted = _rows.OrderBy(r => r.BoxSeq).ThenBy(r => r.IsPlaceholder ? 0 : 1).ToList();
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var row in sorted) _rows.Add(row);
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();
    }

    /// <summary>박스 추가·삭제로 박스번호가 바뀔 때마다 1..N으로 다시 채번하고, 고객주문번호(매칭키)를
    /// 전 박스에 대해 다시 계산한다(총박스수가 바뀌므로, §7.1). 미배정 풀(BoxSeq=0)은 실제 박스가
    /// 아니므로 채번 대상에서 제외한다.</summary>
    private void RecomputeBoxIdentifiers()
    {
        if (_rows.Count == 0)
        {
            UpdateSummary();
            return;
        }

        var remap = FbaKeyGenerator.BuildBoxSeqRenumberMap(_rows.Where(r => r.BoxSeq > 0).Select(r => r.BoxSeq));

        var remappedManual = _manualWeightBoxes.Select(seq => remap.GetValueOrDefault(seq, seq)).ToList();
        _manualWeightBoxes.Clear();
        foreach (var seq in remappedManual) _manualWeightBoxes.Add(seq);

        foreach (var row in _rows.Where(r => r.BoxSeq > 0)) row.BoxSeq = remap[row.BoxSeq];

        RecalculateMatchKeys();
        SortRows();
        UpdateSummary();
    }

    private void RecalculateMatchKeys()
    {
        var totalBoxes = _rows.Where(r => r.BoxSeq > 0).Select(r => r.BoxSeq).Distinct().Count();
        var shipmentId = _shipmentIdInput.Text.Trim();
        var shipmentIdOrNull = string.IsNullOrEmpty(shipmentId) ? null : shipmentId;
        foreach (var row in _rows)
        {
            row.MatchKey = row.BoxSeq > 0 ? FbaKeyGenerator.BuildMatchKey(shipmentIdOrNull, totalBoxes, row.BoxSeq) : string.Empty;
        }
        _rows.ResetBindings();
    }

    /// <summary>박스무게를 다시 계산한다. 사용자가 직접 편집해 _manualWeightBoxes에 기록된 박스는
    /// 건드리지 않는다(§3.4 — 자동계산값이지만 수동 덮어쓰기 가능).</summary>
    private void RecalculateWeights()
    {
        foreach (var group in _rows.GroupBy(r => r.BoxSeq))
        {
            var boxSeq = group.Key;
            var weight = _manualWeightBoxes.Contains(boxSeq)
                ? group.First().WeightG
                : group.Where(r => !r.IsPlaceholder).Sum(r => r.UnitWeightG * r.Qty);
            foreach (var row in group) row.WeightG = weight;
        }
        _rows.ResetBindings();
    }

    private bool IsFirstOfBoxGroup(int rowIndex)
    {
        if (rowIndex <= 0 || rowIndex >= _rows.Count) return true;
        return _rows[rowIndex].BoxSeq != _rows[rowIndex - 1].BoxSeq;
    }

    /// <summary>박스규격/치수/박스무게 칸은 박스 첫 행에만 값을 보여주고 나머지 행은 빈 셀로
    /// 남긴다(박스 첫 행에만 박스 속성 표시). 데이터 자체는 모든 행이 그대로 갖고 있다. 미배정
    /// 풀(BoxSeq=0)은 박스 속성 자체가 없으므로 항상 비워 보여주고, 박스번호 칸에는 그룹 첫 행에만
    /// "미배정"이라고 표시한다.</summary>
    private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var column = _grid.Columns[e.ColumnIndex];
        var row = _rows[e.RowIndex];

        if (column.DataPropertyName == nameof(FbaGridRow.BoxSeq))
        {
            if (row.BoxSeq != 0) return;
            e.Value = IsFirstOfBoxGroup(e.RowIndex) ? "미배정" : string.Empty;
            e.FormattingApplied = true;
            return;
        }

        if (!BoxLevelColumns.Contains(column.DataPropertyName)) return;
        if (row.BoxSeq != 0 && IsFirstOfBoxGroup(e.RowIndex)) return;

        e.Value = string.Empty;
        e.FormattingApplied = true;
    }

    private void OnGridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var row = _rows[e.RowIndex];
        var column = _grid.Columns[e.ColumnIndex];

        // 자리표시 행(아직 품목 없음)은 수량/유통기한을 편집할 대상이 없다.
        if (row.IsPlaceholder && (column.DataPropertyName == nameof(FbaGridRow.Qty) || column.DataPropertyName == nameof(FbaGridRow.ExpiryDate)))
        {
            e.Cancel = true;
            return;
        }
        // 박스무게/운송장번호는 실제 박스 첫 행에서만 편집 가능(미배정 풀은 박스 속성이 없고,
        // 다른 행은 화면에 빈 칸으로만 보인다).
        if ((column.DataPropertyName == nameof(FbaGridRow.WeightG) || column.DataPropertyName == nameof(FbaGridRow.TrackingNo))
            && (row.BoxSeq == 0 || !IsFirstOfBoxGroup(e.RowIndex)))
        {
            e.Cancel = true;
        }
    }

    private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var column = _grid.Columns[e.ColumnIndex];
        var row = _rows[e.RowIndex];

        if (column.DataPropertyName == nameof(FbaGridRow.WeightG))
        {
            _manualWeightBoxes.Add(row.BoxSeq);
            foreach (var r in _rows.Where(x => x.BoxSeq == row.BoxSeq)) r.WeightG = row.WeightG;
            _rows.ResetBindings();
        }
        else if (column.DataPropertyName == nameof(FbaGridRow.TrackingNo))
        {
            // 박스 1개당 운송장은 하나이므로 같은 박스의 다른 품목 줄에도 동일한 값을 맞춘다
            // (§7.2 수동 입력 병행 — 파일 매칭 실패 시 우회 수단).
            SyncTrackingNoAcrossBox(row.BoxSeq, row.TrackingNo);
        }
        else if (column.DataPropertyName == nameof(FbaGridRow.Qty))
        {
            RecalculateWeights();
        }
        UpdateSummary();
    }

    private void SyncTrackingNoAcrossBox(int boxSeq, string? trackingNo)
    {
        foreach (var row in _rows.Where(r => r.BoxSeq == boxSeq)) row.TrackingNo = trackingNo;
        _rows.ResetBindings();
    }

    private void UpdateSummary()
    {
        var boxCount = _rows.Where(r => r.BoxSeq > 0).Select(r => r.BoxSeq).Distinct().Count();
        var poolCount = _rows.Count(r => r.BoxSeq == 0);
        var itemLineCount = _rows.Count(r => !r.IsPlaceholder);
        var totalQty = _rows.Where(r => !r.IsPlaceholder).Sum(r => r.Qty);
        var poolSuffix = poolCount > 0 ? $" / 미배정 {poolCount}줄" : string.Empty;
        _summaryLabel.Text = $"박스 {boxCount}개 / 품목줄 {itemLineCount} / 총수량 {totalQty}개{poolSuffix}";
    }

    /// <summary>과거 발주의 박스/품목 구성을 그대로 이 폼에 채운다. 박스번호는
    /// RecomputeBoxIdentifiers()로 새로 채번되므로 원본 박스번호는 그대로 쓰지 않는다.</summary>
    private void ApplyTemplate(FbaOrder templateOrder, List<FbaBox> templateBoxes, List<FbaBoxItem> templateItems)
    {
        var boxBySeq = templateBoxes.ToDictionary(b => b.BoxSeq, b => b);

        _rows.Clear();
        foreach (var item in templateItems.OrderBy(i => i.BoxSeq).ThenBy(i => i.ItemSeq))
        {
            var box = boxBySeq.GetValueOrDefault(item.BoxSeq);
            _rows.Add(new FbaGridRow
            {
                BoxSeq = item.BoxSeq,
                BoxSpecName = box?.BoxSpecName ?? string.Empty,
                WidthMm = box?.WidthMm ?? 0,
                DepthMm = box?.DepthMm ?? 0,
                HeightMm = box?.HeightMm ?? 0,
                IsCustomSize = box?.IsCustomSize ?? false,
                Csku = item.Csku,
                ItemName = item.ItemName,
                InvoiceDisplayName = item.InvoiceDisplayName,
                Asin = item.Asin,
                CommodityDescription = item.CommodityDescription,
                HsCode = item.HsCode,
                ItemPrice = item.ItemPrice,
                UnitWeightG = item.UnitWeightG,
                QtyPerLayer = item.QtyPerLayer,
                Qty = item.Qty,
                ExpiryDate = item.ExpiryDate,
            });
        }

        RecomputeBoxIdentifiers();
        RecalculateWeights();
        _summaryLabel.Text = $"과거 발주 {templateOrder.FbaNo}를(을) 복사했습니다 — 내용을 확인하고 저장하세요.  " + _summaryLabel.Text;
    }

    private void OnExportCourierClick(object? sender, EventArgs e)
    {
        if (!_isSaved)
        {
            MessageBox.Show("먼저 저장하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (order, boxes, items) = _orderRepository.GetOrder(_fbaNo);
        if (order == null || boxes.Count == 0) return;

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"FBA출고_{_fbaNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("FbaOrderExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("FbaOrderExport", Path.GetDirectoryName(filePath)!);

        try
        {
            // §3.4 상태 전이(작성중/발주확정/출고완료)에는 "출력완료" 단계가 없다 — 하배출고이서
            // 내보내기는 상태를 바꾸지 않는다(발주확정 상태 그대로 유지, 출고완료는 선적명세 출력 시점).
            FbaCourierExporter.Export(order, _config, boxes, items, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
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
            Title = "운송장 결과 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("FbaTrackingImport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("FbaTrackingImport", Path.GetDirectoryName(ofd.FileName)!);

        try
        {
            var result = new FbaTrackingImporter(_orderRepository).Import(ofd.FileName);
            if (!result.Success)
            {
                var msg = "미매칭/불일치가 있어 아무것도 반영되지 않았습니다.\n\n";
                if (result.UnmatchedRows.Count > 0) msg += $"[미매칭]\n{string.Join("\n", result.UnmatchedRows)}\n\n";
                if (result.InconsistentBoxes.Count > 0) msg += $"[운송장번호 불일치]\n{string.Join("\n", result.InconsistentBoxes)}";
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
        var (_, boxes, _) = _orderRepository.GetOrder(_fbaNo);
        var trackingByBox = boxes.ToDictionary(b => b.BoxSeq, b => b.TrackingNo);
        foreach (var row in _rows)
        {
            if (trackingByBox.TryGetValue(row.BoxSeq, out var trackingNo)) row.TrackingNo = trackingNo;
        }
        _rows.ResetBindings();
    }

    private void OnExportShipmentClick(object? sender, EventArgs e)
    {
        if (!_isSaved)
        {
            MessageBox.Show("먼저 저장하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (order, boxes, items) = _orderRepository.GetOrder(_fbaNo);
        if (order == null || boxes.Count == 0) return;

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"FBA선적명세_{_fbaNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("FbaShipmentExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("FbaShipmentExport", Path.GetDirectoryName(filePath)!);

        try
        {
            // 저장 시점 이후 CSKU 마스터에 MOCRA Listing No.가 새로 채워졌을 수 있으므로, 발주에
            // 스냅샷된 값이 아니라 최신 마스터 전체(비활성 포함)를 다시 조회해 넘긴다.
            FbaShipmentExporter.Export(boxes, items, _cskuRepository.GetAll(), filePath);
            order.Status = "출고완료";
            _orderRepository.SaveOrder(order, boxes, items);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var validation = FbaOrderSaveValidator.Validate(
            _rows.Select(r => new FbaOrderSaveValidator.Row(r.BoxSeq, r.IsPlaceholder, r.Qty)).ToList());
        if (!validation.IsValid)
        {
            MessageBox.Show(validation.ErrorMessage, "저장 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RecalculateMatchKeys();

        FbaOrder? existingOrder = null;
        List<FbaBox> existingBoxes = [];
        if (_isSaved)
        {
            (existingOrder, existingBoxes, _) = _orderRepository.GetOrder(_fbaNo);
        }
        var existingByBox = existingBoxes.ToDictionary(b => b.BoxSeq, b => b);

        var order = new FbaOrder
        {
            FbaNo = _fbaNo,
            OrderDate = _orderDatePicker.Value.Date,
            ShipmentId = string.IsNullOrWhiteSpace(_shipmentIdInput.Text) ? null : _shipmentIdInput.Text.Trim(),
            ReceiverName = _config.ReceiverName,
            Phone = _config.Phone,
            Address = _config.Address,
            // 저장 버튼 자체가 "저장 (발주확정)"이므로 저장 시점에 바로 발주확정 상태가 된다(§3.4
            // 상태 전이: 작성중→발주확정→출고완료 — 작성중은 저장 전 화면상의 상태일 뿐 DB에는
            // 남지 않는다). 이미 출고완료(선적명세 출력 완료)된 발주를 다시 저장해도 되돌리지 않는다.
            Status = existingOrder?.Status == "출고완료" ? "출고완료" : "발주확정",
            CreatedAt = existingOrder?.CreatedAt ?? DateTime.Now,
        };

        var boxes = _rows.GroupBy(r => r.BoxSeq).Select(g =>
        {
            var first = g.First();
            existingByBox.TryGetValue(g.Key, out var existingBox);
            return new FbaBox
            {
                FbaNo = _fbaNo,
                BoxSeq = g.Key,
                BoxSpecName = first.BoxSpecName,
                WidthMm = first.WidthMm,
                DepthMm = first.DepthMm,
                HeightMm = first.HeightMm,
                IsCustomSize = first.IsCustomSize,
                WeightG = first.WeightG,
                MatchKey = first.MatchKey,
                TrackingNo = !string.IsNullOrWhiteSpace(first.TrackingNo) ? first.TrackingNo.Trim() : existingBox?.TrackingNo,
                TrackingLoadedAt = existingBox?.TrackingLoadedAt,
                Status = existingBox?.Status ?? "대기",
            };
        }).ToList();

        var items = _rows.GroupBy(r => r.BoxSeq).SelectMany(g => g.Select((row, index) => new FbaBoxItem
        {
            FbaNo = _fbaNo,
            BoxSeq = g.Key,
            ItemSeq = index + 1,
            Csku = row.Csku,
            ItemName = row.ItemName,
            InvoiceDisplayName = row.InvoiceDisplayName,
            Asin = row.Asin,
            CommodityDescription = row.CommodityDescription,
            HsCode = row.HsCode,
            ItemPrice = row.ItemPrice,
            UnitWeightG = row.UnitWeightG,
            QtyPerLayer = row.QtyPerLayer,
            Qty = row.Qty,
            ExpiryDate = string.IsNullOrWhiteSpace(row.ExpiryDate) ? null : row.ExpiryDate,
        })).ToList();

        _orderRepository.SaveOrder(order, boxes, items);
        _isSaved = true;
        _orderDatePicker.Enabled = false;
        UpdateSummary();
        _summaryLabel.Text += $"  —  저장 완료 ({DateTime.Now:HH:mm:ss})";
    }
}
