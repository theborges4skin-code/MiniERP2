using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 발주/출고 이력 관리창. 발주확정/출고확정 이력을 조회하고, 택배사 프로그램에서 받은 운송장 결과
/// 파일을 불러와 수령인 기준으로 매칭해 운송장번호를 채워 출고확정으로 처리한다. 직접 셀을 편집해
/// 수정할 수도 있고, 여러 건을 선택해 삭제할 수도 있다. 발주확정만 해두고 OFS에서 택배사 양식
/// 출력을 빠뜨린 건도 여기서 임의 선택해 다시 출력할 수 있다.
/// </summary>
public class OutboundHistoryForm : Form
{
    private readonly OutboundRepository _outboundRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly CourierExporter _courierExporter = new();
    private readonly SettingsService _settingsService = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly PurchaseSkuRepository _purchaseSkuRepository = new();
    private readonly OutboundShipmentRepository _outboundShipmentRepository = new();

    private ComboBox _channelComboBox = new();
    private ComboBox _lineKindFilterComboBox = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ExcelLikeDataGridView _historyGrid = new();
    private Label _statusLabel = new();
    private bool _suppressCellEndEdit;

    // 셀 직접 편집은 실수 방지를 위해 즉시 DB에 쓰지 않고, "변경사항 저장"을 눌러야 반영된다.
    // 같은 BindingList 인스턴스가 그대로 변경되므로 참조 동일성으로 추적하면 충분하다.
    private readonly HashSet<OutboundDetail> _dirtyDetails = [];

    // B2B 견적관리(§M5) — 발송헤더(운임)는 라인과 별도 저장소(OutboundShipmentTable)라 여기서
    // ShipmentGroupKey 기준으로 캐시해두고, "운임" 열 편집 시 이 캐시만 갱신했다가 저장 시점에
    // 한꺼번에 반영한다(그리드 한 줄=라인 1개이지만 운임은 발송헤더 1건에 귀속되는 값이라 같은
    // ShipmentGroupKey를 가진 여러 줄이 항상 같은 운임을 보여줘야 함).
    private Dictionary<string, decimal> _freightByShipmentKey = new();
    private readonly HashSet<string> _dirtyShipmentKeys = [];

    private static readonly (string Key, string Label, bool DefaultOn)[] TrackingExportFieldDefs =
    [
        ("Recipient",   "수령인",       true),
        ("TrackingNo",  "운송장번호",   true),
        ("OrderNo",     "주문번호",     false),
        ("ChannelCode", "채널",         false),
        ("ProductName", "품목명",       false),
        ("MskuCode",    "SKU",          false),
        ("Qty",         "수량",         false),
        ("SupplyPrice", "납품가",       false),
        ("Address",     "주소",         false),
        ("Remark",      "비고",         false),
        ("LineKind",    "구분",         false),
        ("Status",      "상태",         false),
        ("CreatedAt",   "발주확정 시점", false),
        ("ConfirmedAt", "출고확정 시점", false),
    ];

    /// <summary>
    /// 구분 필터 콤보 항목(샘플발송이력관리_개발기획서.md §4.2). 기본값(전체)이 첫 항목이라, 이
    /// 창에서 비매출 건이 조용히 숨어 "왜 안 보이지" 문제가 생기지 않는다 — 제외는 마감·정산·
    /// 명세표 쪽에서 수행한다.
    /// </summary>
    private static readonly (string Label, LineKindScope Scope, string? Kind)[] LineKindFilterDefs =
    [
        ("전체(기본)", LineKindScope.All, null),
        ("정상거래만", LineKindScope.SaleOnly, null),
        ("비매출 전체", LineKindScope.NonSaleOnly, null),
        (LineKinds.Sample, LineKindScope.NonSaleOnly, LineKinds.Sample),
        (LineKinds.Cs, LineKindScope.NonSaleOnly, LineKinds.Cs),
        (LineKinds.Other, LineKindScope.NonSaleOnly, LineKinds.Other),
    ];

    private readonly long? _focusDetailId;

    public OutboundHistoryForm() : this(null) { }

    /// <summary>
    /// 거래처 마감보드(거래처마감보드_개발기획서.md §2)에서 "역으로 출고이력 추적"할 때 쓰는 생성자.
    /// 지정한 채널·날짜 근방으로 조회 조건을 미리 채우고 자동으로 조회한 뒤, 그 라인을 선택해
    /// 스크롤한다 — 마감보드의 라인 상세에는 없는 필드(운송장번호/수령인/상태 등)까지 여기서
    /// 바로 이어서 확인·수정할 수 있게 하기 위함이다. focusDetailId 없이 채널만 지정하면(마감보드
    /// 거래처 목록에서 우클릭) 특정 라인 선택 없이 그 채널·기간으로만 필터링해서 연다.
    /// </summary>
    public OutboundHistoryForm(long? focusDetailId, string? focusChannelCode = null, DateTime? focusDate = null, DateTime? rangeFrom = null, DateTime? rangeTo = null)
    {
        _focusDetailId = focusDetailId;
        InitializeComponent();
        FormClosing += OnFormClosing;

        if (focusDetailId == null && string.IsNullOrEmpty(focusChannelCode)) return;

        if (!string.IsNullOrEmpty(focusChannelCode))
            _channelComboBox.SelectedValue = focusChannelCode;
        if (focusDate != null)
        {
            _fromDatePicker.Value = focusDate.Value.AddDays(-14);
            _toDatePicker.Value = focusDate.Value.AddDays(14);
        }
        else if (rangeFrom != null && rangeTo != null)
        {
            _fromDatePicker.Value = rangeFrom.Value;
            _toDatePicker.Value = rangeTo.Value;
        }
        Load += (s, e) =>
        {
            OnLoadClick(this, EventArgs.Empty);
            if (focusDetailId != null) SelectAndScrollToDetail(focusDetailId.Value);
        };
    }

    private void SelectAndScrollToDetail(long id)
    {
        foreach (DataGridViewRow row in _historyGrid.Rows)
        {
            if (row.DataBoundItem is not OutboundDetail d || d.Id != id) continue;
            _historyGrid.ClearSelection();
            row.Selected = true;
            _historyGrid.CurrentCell = row.Cells[0];
            _historyGrid.FirstDisplayedScrollingRowIndex = row.Index;
            _statusLabel.Text = $"거래처 마감보드에서 지정한 라인(Id={id})을 찾아 선택했습니다.";
            break;
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_dirtyDetails.Count == 0 && _dirtyShipmentKeys.Count == 0) return;

        var result = MessageBox.Show(
            $"저장하지 않은 변경사항이 {_dirtyDetails.Count + _dirtyShipmentKeys.Count}건 있습니다. 저장하지 않고 닫으시겠습니까?",
            "저장되지 않은 변경사항", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) e.Cancel = true;
    }

    private void InitializeComponent()
    {
        Text = "발주/출고 이력 관리";
        Size = new Size(1150, 650);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var channels = new List<SalesChannel> { new() { ChannelCode = "", ChannelName = "(전체)" } };
        channels.AddRange(_salesChannelRepository.GetAll());
        _channelComboBox = new ComboBox { Size = new Size(160, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _channelComboBox.DataSource = channels;
        _channelComboBox.DisplayMember = "ChannelName";
        _channelComboBox.ValueMember = "ChannelCode";

        // 샘플발송이력관리_개발기획서.md §4.2: 구분 필터. 기본값 "전체(기본)"라 이 창은 지금까지처럼
        // 비매출 건도 그대로 보여준다.
        _lineKindFilterComboBox = new ComboBox { Size = new Size(110, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _lineKindFilterComboBox.Items.AddRange(LineKindFilterDefs.Select(d => d.Label).ToArray());
        _lineKindFilterComboBox.SelectedIndex = 0;

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        var btnQuickDate = DateRangeQuickSelect.CreateButton(_fromDatePicker, _toDatePicker);

        var btnLoad = new Button { Text = "조회", Size = new Size(80, 30) };
        var btnImportTracking = new Button { Text = "운송장번호 불러오기", Size = new Size(150, 30) };
        var btnCheckMissing = new Button { Text = "운송장 파일 누락건 점검", Size = new Size(160, 30) };
        var btnExport = new Button { Text = "선택 건 택배사 양식 출력", Size = new Size(170, 30) };
        var btnDelete = new Button { Text = "선택 삭제", Size = new Size(90, 30) };
        var btnSaveChanges = new Button { Text = "변경사항 저장", Size = new Size(110, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnExportTracking = new Button { Text = "송장번호 출력", Size = new Size(110, 30) };

        btnLoad.Click += OnLoadClick;
        btnImportTracking.Click += OnImportTrackingClick;
        btnCheckMissing.Click += OnCheckMissingTrackingClick;
        btnExport.Click += OnExportClick;
        btnDelete.Click += OnDeleteClick;
        btnSaveChanges.Click += OnSaveChangesClick;
        btnExportTracking.Click += OnExportTrackingClick;

        toolStrip.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolStrip.Controls.Add(_channelComboBox);
        toolStrip.Controls.Add(new Label { Text = "구분:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_lineKindFilterComboBox);
        toolStrip.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_fromDatePicker);
        toolStrip.Controls.Add(new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        toolStrip.Controls.Add(_toDatePicker);
        toolStrip.Controls.Add(btnQuickDate);
        toolStrip.Controls.Add(btnLoad);
        toolStrip.Controls.Add(btnImportTracking);
        toolStrip.Controls.Add(btnCheckMissing);
        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnDelete);
        toolStrip.Controls.Add(btnSaveChanges);
        toolStrip.Controls.Add(btnExportTracking);

        // 행 머리글(왼쪽 끝)을 클릭해야 행 전체가 선택된다(선택 삭제/택배사 양식 출력용) — 셀을
        // 클릭하면 그 셀만 선택되어, 오른클릭 복사 시 행 전체가 아니라 클릭한 셀만 복사된다.
        _historyGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "OutboundHistoryForm.HistoryGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
        };

        _historyGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelCode", DataPropertyName = "ChannelCode", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", Name = "OrderNo", DataPropertyName = "OrderNo", Width = 120, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "수령인", Name = "Recipient", DataPropertyName = "Recipient", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "주소", Name = "Address", DataPropertyName = "Address", Width = 220, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "품목명", Name = "ProductName", DataPropertyName = "ProductName", Width = 130, ReadOnly = true },
            // 비고(내부관리용 메모) 전체 내용을 열에 노출하지 않고 유무만 표시한다(OFS 그리드와 같은
            // 패턴 — DataPropertyName 없이 OnRemarkFlagCellFormatting에서 채움). 전체 내용은 우클릭 "메모 보기".
            new DataGridViewTextBoxColumn { HeaderText = "메모", Name = "RemarkFlag", Width = 55, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "SKU", Name = "MskuCode", DataPropertyName = "MskuCode", Width = 110, ReadOnly = true },
            // 샘플발송이력관리_개발기획서.md §4.2: 구분 열(편집 가능 — 기존 _dirtyDetails 흐름 사용).
            new DataGridViewComboBoxColumn { HeaderText = "구분", Name = "LineKind", DataPropertyName = "LineKind", Width = 70, Items = { "", LineKinds.Sample, LineKinds.Cs, LineKinds.Other }, FlatStyle = FlatStyle.Flat },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 55, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품가", Name = "SupplyPrice", DataPropertyName = "SupplyPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", DataPropertyName = "TrackingNo", Width = 120 },
            // 운송장번호 불러오기에서 이름은 같지만 운송장번호가 여럿이라 자동 적용을 못 하고 사용자가
            // 건너뛴 건을 표시한다(DB 컬럼 아님 — 이번 조회 세션에 한해서만 유지되는 안내용 열).
            new DataGridViewTextBoxColumn { HeaderText = "확인", Name = "ReviewFlag", Width = 90, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.OrangeRed, Font = new Font("맑은 고딕", 9, FontStyle.Bold) } },
            new DataGridViewComboBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "Status", Width = 90, Items = { "발주확정", "출고확정" }, FlatStyle = FlatStyle.Flat },
            new DataGridViewTextBoxColumn { HeaderText = "발주확정 시점", Name = "CreatedAt", DataPropertyName = "CreatedAt", Width = 130, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "출고확정 시점", Name = "ConfirmedAt", DataPropertyName = "ConfirmedAt", Width = 130, ReadOnly = true }
        );
        AddB2BMarginColumns();
        _historyGrid.CellFormatting += OnRemarkFlagCellFormatting;
        SetupRemarkContextMenu();
        _historyGrid.CellEndEdit += OnHistoryGridCellEndEdit;
        // ExcelLikeDataGridView의 붙여넣기(Ctrl+V)는 셀 값을 코드로 직접 대입해서(cell.Value = ...)
        // CellValueChanged만 발생시키고 CellEndEdit는 발생시키지 않는다 — 운송장번호를 붙여넣기로
        // 채운 뒤 "변경사항 저장"을 눌러도 아무 것도 저장되지 않던 버그의 원인이라 함께 구독한다.
        _historyGrid.CellValueChanged += OnHistoryGridCellEndEdit;
        // 옛 용어("발송대기"/"발송완료")로 저장된 데이터가 DB 정규화 전에 이미 메모리에 올라온 경우 등
        // 상태 콤보(Items)에 없는 값이 들어와도 창이 죽지 않도록 방어한다(DataGridViewComboBoxCell 오류).
        _historyGrid.DataError += (s, e) => { e.ThrowException = false; };

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "조회 버튼을 눌러 발주/출고 이력을 불러오세요.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_historyGrid, 0, 1);
        mainLayout.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(mainLayout);
    }

    /// <summary>
    /// B2B 견적관리(§M5) — 매입처/원가/중량/운임/물류비/실질원가/마진 열을 추가한다. 기존
    /// 마켓플레이스 채널 이력은 이 값들을 전혀 쓰지 않으므로(WeightKg 등이 null) 전부 빈 칸으로
    /// 보이고 기존 사용에 영향이 없다.
    /// </summary>
    private void AddB2BMarginColumns()
    {
        var purchaseChannels = new List<SalesChannel> { new() { ChannelCode = "", ChannelName = "" } };
        purchaseChannels.AddRange(_salesChannelRepository.GetAll().Where(c => c.IsPurchase));

        _historyGrid.Columns.AddRange(
            new DataGridViewComboBoxColumn
            {
                HeaderText = "매입처", Name = "PurchaseChannelCode", DataPropertyName = "PurchaseChannelCode", Width = 100,
                DataSource = purchaseChannels, DisplayMember = "ChannelName", ValueMember = "ChannelCode", FlatStyle = FlatStyle.Flat,
            },
            new DataGridViewTextBoxColumn { HeaderText = "원가/kg", Name = "PurchasePrice", DataPropertyName = "PurchasePrice", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "중량(kg)", Name = "WeightKg", DataPropertyName = "WeightKg", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "발송운임", Name = "FreightCost", DataPropertyName = string.Empty, Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "물류비/kg", Name = "FreightPerKg", DataPropertyName = string.Empty, Width = 80, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = Color.DimGray } },
            new DataGridViewTextBoxColumn { HeaderText = "실질원가/kg", Name = "EffectiveCost", DataPropertyName = string.Empty, Width = 90, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = Color.DimGray } },
            new DataGridViewTextBoxColumn { HeaderText = "마진", Name = "Margin", DataPropertyName = string.Empty, Width = 90, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = Color.DimGray } }
        );
        _historyGrid.CellFormatting += OnB2BColumnCellFormatting;
    }

    /// <summary>
    /// "발송운임"은 발송헤더 캐시(_freightByShipmentKey)에서, 나머지 3개(물류비/kg·실질원가/kg·마진)는
    /// 그 값과 라인 데이터로부터 매번 다시 계산해서 보여준다(§3 산출 규칙). WeightKg이 없는 줄(=
    /// B2B 대상이 아닌 마켓플레이스 라인)은 빈 칸으로 남긴다 — 이 열들을 안 쓰면 기존 화면과 동일.
    /// </summary>
    // FormattingApplied=true는 e.Value가 "이미 완성된 표시용 문자열"일 때만 써야 한다 — 아래 4개
    // 열은 전부 DefaultCellStyle.Format="N0"로 그리드가 숫자 서식을 입혀야 하므로, 원시 decimal만
    // 넣고 FormattingApplied는 켜지 않는다(켜면 FormatException — 2026-07-30 ChannelCskuForm의
    // 같은 패턴에서 실제로 재현됨. 여기서는 DataError 억제(아래 생성자)로 조용히 삼켜지고 있었을
    // 뿐 같은 버그였다).
    private void OnB2BColumnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _historyGrid.Rows.Count) return;
        var columnName = _historyGrid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("FreightCost" or "FreightPerKg" or "EffectiveCost" or "Margin")) return;
        if (_historyGrid.Rows[e.RowIndex].DataBoundItem is not OutboundDetail detail) return;

        if (columnName == "FreightCost")
        {
            e.Value = _freightByShipmentKey.TryGetValue(detail.ShipmentGroupKey, out var freight) ? freight : 0m;
            return;
        }

        if (detail.WeightKg is not { } weightKg || weightKg <= 0)
        {
            e.Value = null;
            return;
        }

        var totalWeight = _historyGrid.Rows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as OutboundDetail)
            .Where(d => d != null && d.ShipmentGroupKey == detail.ShipmentGroupKey)
            .Sum(d => d!.WeightKg ?? 0m);
        var hasFreight = _freightByShipmentKey.TryGetValue(detail.ShipmentGroupKey, out var freightCost) && freightCost > 0;
        var freightPerKg = hasFreight && totalWeight > 0 ? freightCost / totalWeight : 0m;

        if (columnName == "FreightPerKg")
        {
            e.Value = freightPerKg;
            return;
        }

        var costPerKg = detail.PurchasePrice ?? 0m;
        var effectiveCost = costPerKg + freightPerKg;

        if (columnName == "EffectiveCost")
        {
            e.Value = effectiveCost;
            return;
        }

        // Margin
        e.Value = (detail.SupplyPrice - effectiveCost) * weightKg;
    }

    /// <summary>"메모" 열에는 전체 내용 대신 유무만 표시한다(OfsForm의 RemarkFlag 열과 같은 패턴).</summary>
    private void OnRemarkFlagCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _historyGrid.Rows.Count) return;
        if (_historyGrid.Columns[e.ColumnIndex].Name != "RemarkFlag") return;
        if (_historyGrid.Rows[e.RowIndex].DataBoundItem is not OutboundDetail detail) return;

        e.Value = string.IsNullOrWhiteSpace(detail.Remark) ? string.Empty : "메모있음";
        e.FormattingApplied = true;
    }

    /// <summary>선택 행의 비고(내부관리용 메모) 전체 내용을 보여준다.</summary>
    private void SetupRemarkContextMenu()
    {
        var menu = _historyGrid.ContextMenuStrip!;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("메모 보기", null, OnViewRemarkClick);
        menu.Items.Add("메모 편집", null, OnEditRemarkClick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("선택 이력 채널 이관", null, OnTransferChannelClick);
    }

    /// <summary>
    /// 샘플등 채널 등에 쌓인 비매출 이력을 정기 거래처로 옮긴다(샘플발송이력관리_개발기획서.md
    /// §5). 정상 거래(LineKind='') 라인은 대상이 아니다 — 이미 정산 대사가 끝난 정상 매출 이력의
    /// 채널이 실수로 바뀌는 사고를 막기 위한 가드다.
    /// </summary>
    private void OnTransferChannelClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedDetails();
        if (selected.Count == 0)
        {
            MessageBox.Show("이관할 이력을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (selected.Any(d => string.IsNullOrEmpty(d.LineKind)))
        {
            MessageBox.Show(
                "정상 거래(구분 없음) 라인은 이관할 수 없습니다.\n샘플·CS·기타로 지정된 비매출 라인만 선택하세요.",
                "이관 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new ChannelTransferDialog(selected.Count);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        var request = new Services.ChannelTransferRequest
        {
            TargetChannelCode = dialog.TargetChannelCode!,
            TargetChannelName = dialog.TargetChannelName!,
            UpdateSupplyPriceFromTarget = dialog.UpdateSupplyPriceFromTarget,
            ManualSupplyPrice = dialog.ManualSupplyPrice,
            ConvertToSaleTransaction = dialog.ConvertToSaleTransaction,
            ForcedClosingPeriod = dialog.ForcedClosingPeriod,
        };
        var result = new Services.ChannelTransferService().TransferChannel(selected, request);

        var message = $"이관 {result.TransferredCount}건 완료.";
        if (result.AlreadyClosedSkipped > 0)
            message += $"\n마감확정 제외 {result.AlreadyClosedSkipped}건(메모 추가됨 — 원래 채널 이력에 그대로 남음)";
        if (result.ConflictSkipped.Count > 0)
        {
            var preview = string.Join("\n", result.ConflictSkipped.Take(5).Select(c => $"- {c.Reason}"));
            var more = result.ConflictSkipped.Count > 5 ? $"\n... 외 {result.ConflictSkipped.Count - 5}건" : "";
            message += $"\n충돌로 스킵 {result.ConflictSkipped.Count}건\n{preview}{more}";
        }
        MessageBox.Show(message, "이관 결과", MessageBoxButtons.OK, MessageBoxIcon.Information);

        OnLoadClick(this, EventArgs.Empty);
    }

    private void OnViewRemarkClick(object? sender, EventArgs e)
    {
        var detail = GetSelectedDetails().FirstOrDefault();
        if (detail == null) return;

        MessageBox.Show(
            string.IsNullOrWhiteSpace(detail.Remark) ? "메모가 없습니다." : detail.Remark,
            "메모", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// 선택 행의 비고(내부관리용 메모)를 편집한다(샘플발송이력관리_개발기획서.md §4.3(c)). 저장은
    /// UpdateDetail이 Remark를 UPDATE하도록 이미 고쳐두었으므로, 기존 편집 저장 흐름(_dirtyDetails
    /// → [변경사항 저장])에 그대로 태운다.
    /// </summary>
    private void OnEditRemarkClick(object? sender, EventArgs e)
    {
        var detail = GetSelectedDetails().FirstOrDefault();
        if (detail == null) return;

        using var dialog = new TextPromptDialog("메모 편집", "메모:", detail.Remark, multiline: true);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        detail.Remark = dialog.Value;
        _dirtyDetails.Add(detail);
        _historyGrid.Invalidate();
        _statusLabel.Text = $"{_dirtyDetails.Count}건의 변경사항이 저장되지 않았습니다. '변경사항 저장'을 눌러주세요.";
    }

    private void OnLoadClick(object? sender, EventArgs e)
    {
        if (_dirtyDetails.Count > 0 || _dirtyShipmentKeys.Count > 0)
        {
            var result = MessageBox.Show(
                $"저장하지 않은 변경사항이 {_dirtyDetails.Count + _dirtyShipmentKeys.Count}건 있습니다. 저장하지 않고 다시 조회하시겠습니까?",
                "저장되지 않은 변경사항", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }

        var channelCode = _channelComboBox.SelectedValue as string;
        var from = _fromDatePicker.Value.Date;
        var to = _toDatePicker.Value.Date.AddDays(1).AddTicks(-1);
        var (_, lineKindScope, lineKind) = LineKindFilterDefs[Math.Max(_lineKindFilterComboBox.SelectedIndex, 0)];

        var details = _outboundRepository.GetHistory(string.IsNullOrEmpty(channelCode) ? null : channelCode, from, to, lineKindScope, lineKind);
        EnsureStatusItemsInclude(details.Select(d => d.Status));
        _dirtyDetails.Clear();
        _dirtyShipmentKeys.Clear();
        _freightByShipmentKey = _outboundShipmentRepository.GetByKeys(details.Select(d => d.ShipmentGroupKey))
            .ToDictionary(s => s.ShipmentGroupKey, s => s.FreightCost);
        _suppressCellEndEdit = true;
        _historyGrid.DataSource = new BindingList<OutboundDetail>(details);
        _suppressCellEndEdit = false;
        _statusLabel.Text = $"발주/출고 이력 {details.Count}건 조회됨.";
    }

    /// <summary>
    /// 선택 삭제/택배사 양식 출력의 "선택된 줄"을 모은다. 행 머리글로 선택한 줄(SelectedRows)뿐
    /// 아니라, 셀만 클릭/드래그로 선택한 경우(SelectedCells)도 그 셀이 속한 줄을 포함한다 —
    /// 복사는 클릭한 셀만 복사되어야 하지만(SelectionMode=RowHeaderSelect), 삭제/출력 대상을
    /// 고를 때는 어느 셀이든 클릭해 그 줄을 고를 수 있는 기존 사용 흐름을 유지하기 위함이다.
    /// </summary>
    private List<OutboundDetail> GetSelectedDetails()
    {
        var rowIndices = _historyGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index)
            .Union(_historyGrid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex))
            .Distinct();

        return rowIndices
            .Where(i => i >= 0 && i < _historyGrid.Rows.Count && !_historyGrid.Rows[i].IsNewRow)
            .Select(i => _historyGrid.Rows[i].DataBoundItem)
            .OfType<OutboundDetail>()
            .ToList();
    }

    /// <summary>
    /// 옛 용어로 저장된 값 등 콤보의 두 표준값("발주확정"/"출고확정")에 없는 상태값이 데이터에 있어도
    /// 표시 시 DataGridViewComboBoxCell 오류가 나지 않도록, 실제 로드된 값을 Items에 보강해둔다.
    /// </summary>
    private void EnsureStatusItemsInclude(IEnumerable<string> values)
    {
        if (_historyGrid.Columns["Status"] is not DataGridViewComboBoxColumn column) return;

        var existing = new HashSet<string>(column.Items.Cast<string>(), StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value) || !existing.Add(value)) continue;
            column.Items.Add(value);
        }
    }

    /// <summary>
    /// 그리드 셀을 직접 수정(수량/납품가/운송장번호/상태)해도 실수 방지를 위해 바로 DB에 쓰지
    /// 않고, 변경된 항목만 표시해두었다가 "변경사항 저장"을 눌러야 한꺼번에 반영된다. 타이핑 편집
    /// (CellEndEdit)뿐 아니라 붙여넣기(CellValueChanged로만 감지됨)도 감지해야 하므로 두 이벤트
    /// 모두 이 핸들러를 구독한다.
    /// </summary>
    private void OnHistoryGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressCellEndEdit) return;
        if (e.RowIndex < 0 || e.RowIndex >= _historyGrid.Rows.Count) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _historyGrid.Columns.Count) return;
        var columnName = _historyGrid.Columns[e.ColumnIndex].Name;
        // "확인" 열은 운송장번호 불러오기가 안내용으로 채우는 표시일 뿐 실제 편집 대상이 아니므로 제외.
        if (columnName == "ReviewFlag") return;
        if (_historyGrid.Rows[e.RowIndex].DataBoundItem is not OutboundDetail detail) return;

        // "발송운임"은 라인이 아니라 발송헤더(ShipmentGroupKey) 소속 값이라 OutboundDetail의
        // 필드가 아니다 — 별도 캐시에 반영하고, 같은 발송(합포장)의 다른 줄도 화면에 즉시 반영한다.
        if (columnName == "FreightCost")
        {
            var raw = _historyGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            var freight = decimal.TryParse(raw, out var parsed) ? parsed : 0m;
            _freightByShipmentKey[detail.ShipmentGroupKey] = freight;
            _dirtyShipmentKeys.Add(detail.ShipmentGroupKey);
            InvalidateRowsForShipment(detail.ShipmentGroupKey);
            _statusLabel.Text = $"운임 변경 {_dirtyShipmentKeys.Count}건 + 이력 변경 {_dirtyDetails.Count}건이 저장되지 않았습니다. '변경사항 저장'을 눌러주세요.";
            return;
        }

        // 매입처를 고르면(또는 지우면) 원가/kg을 자동으로 채워준다 — 매입처 지정 시 그 매입가,
        // 아니면 마스터DB 대표원가(CostPrice)로 스냅샷(§3 원가 스냅샷 규칙). 사용자가 원가 칸을
        // 직접 고친 뒤에는 매입처를 다시 바꾸기 전까지 그 값을 건드리지 않는다.
        if (columnName == "PurchaseChannelCode")
        {
            detail.PurchasePrice = ResolveCostSnapshot(detail);
            _historyGrid.Rows[e.RowIndex].Cells["PurchasePrice"].Value = detail.PurchasePrice;
        }

        if (detail.Status == "출고확정" && detail.ConfirmedAt is null)
        {
            detail.ConfirmedAt = DateTime.Now;
        }
        else if (detail.Status == "발주확정")
        {
            detail.ConfirmedAt = null;
        }

        _dirtyDetails.Add(detail);
        _historyGrid.InvalidateRow(e.RowIndex);
        _statusLabel.Text = $"{_dirtyDetails.Count}건의 변경사항이 저장되지 않았습니다. '변경사항 저장'을 눌러주세요.";
    }

    /// <summary>매입처 지정 시 그 매입가, 아니면 CSKU 개별원가(오버라이드), 그 외엔 마스터DB
    /// 대표원가(CostPrice) 순으로 원가/kg 스냅샷을 반환한다(CostResolver 참고).</summary>
    private decimal ResolveCostSnapshot(OutboundDetail detail)
    {
        var csku = _channelSkuRepository.GetByChannelAndCskuCode(detail.ChannelCode, detail.MskuCode);
        var masterSku = csku?.Msku ?? detail.MskuCode;

        decimal? purchasePrice = null;
        if (!string.IsNullOrEmpty(detail.PurchaseChannelCode))
        {
            purchasePrice = _purchaseSkuRepository.GetByChannelAndMsku(detail.PurchaseChannelCode, masterSku)?.PurchasePrice;
        }

        var masterCostPrice = _itemRepository.GetBySku(masterSku)?.CostPrice ?? 0m;
        return CostResolver.Resolve(purchasePrice, csku?.CostPriceOverride, masterCostPrice);
    }

    private void InvalidateRowsForShipment(string shipmentGroupKey)
    {
        foreach (DataGridViewRow row in _historyGrid.Rows)
        {
            if (row.DataBoundItem is OutboundDetail d && d.ShipmentGroupKey == shipmentGroupKey)
                _historyGrid.InvalidateRow(row.Index);
        }
    }

    private void OnSaveChangesClick(object? sender, EventArgs e)
    {
        if (_dirtyDetails.Count == 0 && _dirtyShipmentKeys.Count == 0)
        {
            MessageBox.Show("저장할 변경사항이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lockedPriceCount = 0;
        foreach (var detail in _dirtyDetails)
        {
            if (_outboundRepository.UpdateDetail(detail)) lockedPriceCount++;
        }

        foreach (var shipmentKey in _dirtyShipmentKeys)
        {
            _outboundShipmentRepository.Upsert(new OutboundShipmentModel
            {
                ShipmentGroupKey = shipmentKey,
                FreightCost = _freightByShipmentKey.TryGetValue(shipmentKey, out var freight) ? freight : 0m,
            });
        }

        var savedCount = _dirtyDetails.Count;
        var savedShipmentCount = _dirtyShipmentKeys.Count;
        _dirtyDetails.Clear();
        _dirtyShipmentKeys.Clear();
        OnLoadClick(sender, e);
        // 출고확정 건은 납품가가 잠겨있어(P3 — 견적기록관리_개발기획서_확정본.md §7.3) 요청한 값이
        // 조용히 무시될 수 있다. 그리드를 다시 로드해 실제 저장된 값을 보여주는 것과 별개로, 사용자가
        // "입력했는데 왜 안 바뀌었지"로 헷갈리지 않도록 몇 건이 잠겼는지 명시적으로 안내한다.
        var lockedNote = lockedPriceCount > 0
            ? $" (출고확정 건 {lockedPriceCount}건은 납품가 변경이 적용되지 않았습니다 — 확정 후 단가는 잠깁니다)"
            : "";
        _statusLabel.Text = (savedShipmentCount > 0
            ? $"이력 {savedCount}건, 운임 {savedShipmentCount}건의 변경사항을 저장했습니다."
            : $"{savedCount}건의 변경사항을 저장했습니다.") + lockedNote;
    }

    /// <summary>
    /// 발주확정만 해두고 OFS에서 택배사 양식 출력을 빠뜨린 건(또는 재출력이 필요한 건)을 여기서
    /// 임의로 선택해 택배사 양식으로 다시 출력한다. OutboundDetail에는 OFS 그리드의 연락처/배송메세지/
    /// 송장표시명 같은 일부 정보가 없으므로(저장 안 됨), 그 항목들은 비워진 채로 출력된다.
    /// </summary>
    private void OnExportClick(object? sender, EventArgs e)
    {
        if (_historyGrid.DataSource is not BindingList<OutboundDetail> details)
        {
            MessageBox.Show("먼저 이력을 조회하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = GetSelectedDetails();

        if (selected.Count == 0)
        {
            MessageBox.Show("택배사 양식으로 출력할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var courierDialog = new SelectCourierDialog();
        if (FormManager.ShowDialogSafe(courierDialog, this) != DialogResult.OK || courierDialog.SelectedCourier is not { } courier)
        {
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"{courier.CourierName}_출고_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("OutboundHistoryExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        _settingsService.SetLastFolder("OutboundHistoryExport", Path.GetDirectoryName(filePath)!);

        var orderItems = selected.Select(d => new OfsOrderItem
        {
            ChannelCode = d.ChannelCode,
            OrderNo = d.OrderNo,
            ProductName = d.ProductName,
            Quantity = d.Qty,
            Recipient = d.Recipient,
            Address = d.Address,
            MappedSku = d.MskuCode,
            TrackingNo = d.TrackingNo,
        }).ToList();

        ExportOrders(orderItems, courier, filePath, selected.Count);
    }

    private async void ExportOrders(List<OfsOrderItem> orderItems, CourierMaster courier, string filePath, int selectedCount)
    {
        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = $"'{courier.CourierName}' 양식으로 내보내는 중...";

        try
        {
            var channelConfigsByCode = _channelConfigService.Load().ToDictionary(c => c.ChannelCode);
            var overflowGroups = await _courierExporter.ExportAsync(orderItems, courier, filePath, channelConfigsByCode, appendCourierNameColumn: true);
            _statusLabel.Text = $"선택한 {selectedCount}건을 '{courier.CourierName}' 양식으로 내보냈습니다.";

            if (overflowGroups.Count > 0)
            {
                MessageBox.Show(
                    $"다음 묶음은 품목이 4줄을 초과해 송장에 다 표시되지 못할 수 있습니다:\n{string.Join(", ", overflowGroups)}",
                    "품목 줄 수 초과 안내", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "내보내기 오류 발생";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        if (_historyGrid.DataSource is not BindingList<OutboundDetail> details)
        {
            MessageBox.Show("먼저 이력을 조회하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = GetSelectedDetails();

        if (selected.Count == 0)
        {
            MessageBox.Show("삭제할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show($"선택한 {selected.Count}건을 삭제하시겠습니까?\n삭제하면 되돌릴 수 없습니다.", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _outboundRepository.DeleteByIds(selected.Select(d => d.Id));
        foreach (var item in selected)
        {
            details.Remove(item);
            _dirtyDetails.Remove(item);
        }
        _statusLabel.Text = $"{selected.Count}건을 삭제했습니다.";
    }

    /// <summary>
    /// 운송장 결과 파일(택배사 프로그램에서 받은 엑셀)을 불러와 이름+주소가 일치하는 발주확정 건에
    /// 운송장번호를 채운다. 이름+주소가 같은 건이 여럿이어도 그 이름에 대해 파일에서 찾은 운송장번호가
    /// 1개뿐이면 무조건 합포장된 것으로 보고 전부 같은 운송장번호를 적용하고(1번 규칙), 운송장번호가
    /// 2개 이상 발견되면(같은 사람이 여러 번 주문했거나 이름만 같은 동명이인일 수 있어 시스템이 판단할
    /// 수 없으므로) 사용자가 직접 어느 건에 어느 운송장번호를 적용할지 골라야 한다(2/3번 규칙).
    /// </summary>
    private void OnImportTrackingClick(object? sender, EventArgs e)
    {
        if (_historyGrid.DataSource is not BindingList<OutboundDetail> details || details.Count == 0)
        {
            MessageBox.Show("먼저 조회 버튼으로 발주확정 대상 이력을 불러오세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var courierDialog = new SelectCourierDialog();
        if (FormManager.ShowDialogSafe(courierDialog, this) != DialogResult.OK || courierDialog.SelectedCourier is not { } courier)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(courier.TrackingImportRecipientHeader) || string.IsNullOrWhiteSpace(courier.TrackingImportTrackingNoHeader))
        {
            MessageBox.Show(
                $"'{courier.CourierName}'의 운송장 결과 가져오기 양식이 설정되지 않았습니다.\n택배사 양식 관리 창에서 수령인/운송장번호 헤더를 먼저 지정하세요.",
                "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "운송장 결과 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("TrackingImport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _settingsService.SetLastFolder("TrackingImport", Path.GetDirectoryName(ofd.FileName)!);
            ImportTrackingFile(ofd.FileName, courier, details);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportTrackingFile(string filePath, CourierMaster courier, BindingList<OutboundDetail> details)
    {
        using var package = Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? CsvWorkbookReader.LoadAsPackage(filePath)
            : ExcelFileOpener.OpenWithPasswordPrompt(filePath, this);
        if (package == null) return;

        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        var headerRow = courier.TrackingImportHeaderRow;
        if (worksheet?.Dimension == null || headerRow > worksheet.Dimension.End.Row)
        {
            MessageBox.Show("엑셀 파일에서 데이터를 찾을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 수령인 헤더는 '|'로 여러 개 지정 가능 (예: "받는분|받는분명") — 먼저 일치하는 컬럼을 사용한다.
        var recipientHeaderCandidates = courier.TrackingImportRecipientHeader
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int? recipientCol = null, trackingCol = null, orderNoCol = null, addressCol = null, productNameCol = null;
        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var header = worksheet.Cells[headerRow, col].Value?.ToString()?.Trim();
            if (header is null) continue;
            if (recipientHeaderCandidates.Any(h => string.Equals(header, h, StringComparison.OrdinalIgnoreCase))) recipientCol = col;
            if (string.Equals(header, courier.TrackingImportTrackingNoHeader, StringComparison.OrdinalIgnoreCase)) trackingCol = col;
            // 아래 3개는 선택 항목 — 설정돼 있고 파일에 실제로 있을 때만 찾는다(없어도 오류 아님).
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportOrderNoHeader) && string.Equals(header, courier.TrackingImportOrderNoHeader, StringComparison.OrdinalIgnoreCase)) orderNoCol = col;
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportAddressHeader) && string.Equals(header, courier.TrackingImportAddressHeader, StringComparison.OrdinalIgnoreCase)) addressCol = col;
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportProductNameHeader) && string.Equals(header, courier.TrackingImportProductNameHeader, StringComparison.OrdinalIgnoreCase)) productNameCol = col;
        }

        if (recipientCol is null || trackingCol is null)
        {
            MessageBox.Show(
                $"설정된 헤더(\"{courier.TrackingImportRecipientHeader}\"/\"{courier.TrackingImportTrackingNoHeader}\")를 {headerRow}행에서 찾지 못했습니다.\n택배사 양식 설정을 확인하세요.",
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 운송장 파일에서 (수령인 이름 → 그 이름에 대해 발견된 고유 운송장번호 목록)을 먼저 전부
        // 모은다. 같은 수령인이 여러 줄로 나와도(예: 합포장 시 품목별로 줄이 나뉘는 택배사 양식)
        // 여기서 중복 없이 합쳐지므로, 파일 행 하나하나가 아니라 이름당 한 번씩만 판단하면 된다.
        // 고객주문번호/상세주소/품목(선택 항목)은 운송장번호가 여러 개 발견됐을 때 선택창에서
        // 참고용으로만 보여준다 — 매칭 로직 자체는 그대로 수령인 이름 기준이다.
        var trackingRowsByRecipientName = new Dictionary<string, List<TrackingFileRow>>();
        for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
        {
            var recipient = worksheet.Cells[row, recipientCol.Value].Value?.ToString()?.Trim();
            var trackingNo = worksheet.Cells[row, trackingCol.Value].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(trackingNo)) continue;

            var orderNo = orderNoCol is { } oc ? worksheet.Cells[row, oc].Value?.ToString()?.Trim() : null;
            var address = addressCol is { } ac ? worksheet.Cells[row, ac].Value?.ToString()?.Trim() : null;
            var productName = productNameCol is { } pc ? worksheet.Cells[row, pc].Value?.ToString()?.Trim() : null;

            var key = NormalizeForMatch(recipient);
            if (!trackingRowsByRecipientName.TryGetValue(key, out var list))
                trackingRowsByRecipientName[key] = list = [];

            var existing = list.FirstOrDefault(r => string.Equals(r.TrackingNo, trackingNo, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                list.Add(new TrackingFileRow(trackingNo, orderNo, address, productName));
            }
            else if (!string.IsNullOrWhiteSpace(productName) && existing.ProductName?.Contains(productName, StringComparison.OrdinalIgnoreCase) != true)
            {
                // 같은 운송장번호가 여러 줄(합포장)로 나오면 품목명은 이어붙여서 보여준다.
                existing.ProductName = string.IsNullOrWhiteSpace(existing.ProductName) ? productName : $"{existing.ProductName}, {productName}";
            }
        }

        // 매칭 대상은 운송장번호가 아직 없는(=발주확정 상태) 건. 이름+주소가 완전히 같은 건들만 한
        // 그룹으로 묶는다 — 이름만 같은 동명이인(주소 다름)은 서로 다른 배송으로 취급해야 하므로
        // 별도 그룹이 된다(3번 규칙).
        var candidateGroups = details
            .Where(d => string.IsNullOrWhiteSpace(d.TrackingNo))
            .GroupBy(d => (Name: NormalizeForMatch(d.Recipient), Addr: NormalizeForMatch(d.Address)));

        var appliedCount = 0;
        var autoBundledGroups = 0;
        var userResolvedCount = 0;
        var needsReviewIds = new HashSet<long>();
        var noFileDataCount = 0;

        foreach (var group in candidateGroups)
        {
            var candidates = group.ToList();
            if (!trackingRowsByRecipientName.TryGetValue(group.Key.Name, out var trackingRows) || trackingRows.Count == 0)
            {
                noFileDataCount += candidates.Count;
                continue; // 운송장 파일에 이 이름 자체가 없음 — 아직 처리할 게 없으므로 그냥 둔다.
            }

            if (trackingRows.Count == 1)
            {
                // 1번 규칙: 이 이름+주소 조합에 대해 파일에 운송장번호가 딱 1개뿐 — 여러 주문행이
                // 있어도 무조건 하나의 운송장으로 합포장된 것이므로 그룹 전체에 같은 운송장번호를
                // 적용한다(사용자 확인 없이 자동).
                var trackingNo = trackingRows[0].TrackingNo;
                foreach (var d in candidates)
                {
                    _outboundRepository.ApplyTrackingNo(d.Id, trackingNo);
                    d.TrackingNo = trackingNo;
                    d.Status = "출고확정";
                    d.ConfirmedAt = DateTime.Now;
                    appliedCount++;
                }
                autoBundledGroups++;
            }
            else
            {
                // 2/3번 규칙: 이 이름에 대해 운송장번호가 여러 개 발견됨 — 어느 주문행이 어느
                // 운송장번호로 나갔는지 시스템이 판단할 수 없으므로(같은 사람이 여러 번 주문했거나,
                // 이름만 같은 동명이인일 수 있음) 사용자가 직접 골라야 한다.
                using var picker = new TrackingAssignDialog(group.Key.Name, group.Key.Addr, candidates, trackingRows);
                var resolvedIds = new HashSet<long>();
                if (FormManager.ShowDialogSafe(picker, this) == DialogResult.OK)
                {
                    foreach (var (detail, trackingNo) in picker.Assignments)
                    {
                        _outboundRepository.ApplyTrackingNo(detail.Id, trackingNo);
                        detail.TrackingNo = trackingNo;
                        detail.Status = "출고확정";
                        detail.ConfirmedAt = DateTime.Now;
                        resolvedIds.Add(detail.Id);
                        appliedCount++;
                        userResolvedCount++;
                    }
                }
                foreach (var d in candidates.Where(d => !resolvedIds.Contains(d.Id)))
                    needsReviewIds.Add(d.Id);
            }
        }

        MarkReviewFlags(needsReviewIds);
        _historyGrid.Refresh();

        // 2026-06-28 점검: 그리드 갱신 직후 모달을 띄우는 패턴이 다른 화면들에서 반복 재현됐던
        // 경쟁 상태와 같은 위험군이라(특히 선택창(TrackingAssignDialog)이 막 닫혔을 수 있는 상황이라
        // 더 위험) 이미 있던 상태표시줄에 요약을 그대로 담아 대체한다.
        var summary = $"운송장번호 {appliedCount}건을 적용해 출고확정으로 처리했습니다";
        summary += autoBundledGroups > 0 ? $"(합포장 자동적용 {autoBundledGroups}건 포함)." : ".";
        if (userResolvedCount > 0) summary += $" 직접 선택해 적용: {userResolvedCount}건.";
        if (needsReviewIds.Count > 0) summary += $" ▶ 확인요망(운송장번호가 여러 개라 직접 확인 필요): {needsReviewIds.Count}건.";
        if (noFileDataCount > 0) summary += $" 운송장 파일에 이름이 없어 건너뜀: {noFileDataCount}건.";
        _statusLabel.Text = summary;
    }

    // ─── 운송장 파일 누락건 점검 ────────────────────────────────────────────

    /// <summary>
    /// 운송장 결과 파일 전체를 읽어 DB(OutboundDetailTable) 전체 TrackingNo와 대조하고, 아직 등록되지
    /// 않은 건을 TrackingBackfillViewer로 보여준다(운송장파일_미매칭건_OFS역등록_검토_rev2.md §3.1).
    /// 위의 운송장번호 불러오기(OnImportTrackingClick)는 "지금 조회된 이력"에 운송장번호를 채우는
    /// 반대 방향 작업이라 서로 대체하지 않는다 — 이쪽은 이력 자체가 아예 없는(발주확정도 안 된)
    /// 진짜 누락건을 찾는 게 목적이라 조회 여부와 무관하게 파일과 DB 전체를 비교한다.
    /// </summary>
    private void OnCheckMissingTrackingClick(object? sender, EventArgs e)
    {
        using var courierDialog = new SelectCourierDialog();
        if (FormManager.ShowDialogSafe(courierDialog, this) != DialogResult.OK || courierDialog.SelectedCourier is not { } courier)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(courier.TrackingImportRecipientHeader) || string.IsNullOrWhiteSpace(courier.TrackingImportTrackingNoHeader))
        {
            MessageBox.Show(
                $"'{courier.CourierName}'의 운송장 결과 가져오기 양식이 설정되지 않았습니다.\n택배사 양식 관리 창에서 수령인/운송장번호 헤더를 먼저 지정하세요.",
                "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "운송장 결과 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("TrackingBackfillCheck") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _settingsService.SetLastFolder("TrackingBackfillCheck", Path.GetDirectoryName(ofd.FileName)!);

            using var package = Path.GetExtension(ofd.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? CsvWorkbookReader.LoadAsPackage(ofd.FileName)
                : ExcelFileOpener.OpenWithPasswordPrompt(ofd.FileName, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
            {
                MessageBox.Show("엑셀 파일에서 시트를 찾을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var parseResult = TrackingBackfillFileParser.Parse(worksheet, courier, Path.GetFileName(ofd.FileName));
            if (parseResult.Error != null)
            {
                MessageBox.Show(parseResult.Error, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (parseResult.Rows.Count == 0)
            {
                MessageBox.Show("파일에서 운송장번호가 있는 행을 찾지 못했습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var existing = _outboundRepository.GetExistingTrackingNos(parseResult.Rows.Select(r => r.TrackingNo));
            foreach (var row in parseResult.Rows) row.IsRegistered = existing.Contains(row.TrackingNo);

            var missingCount = parseResult.Rows.Count(r => !r.IsRegistered);
            _statusLabel.Text = $"운송장 파일 {parseResult.Rows.Count}건 중 미등록(누락 후보) {missingCount}건 — 뷰어 창에서 확인하세요.";

            var viewer = Application.OpenForms.OfType<TrackingBackfillViewer>().FirstOrDefault();
            if (viewer == null)
            {
                viewer = new TrackingBackfillViewer();
                viewer.Show();
            }
            else if (viewer.WindowState == FormWindowState.Minimized)
            {
                viewer.WindowState = FormWindowState.Normal;
            }
            viewer.LoadRows(parseResult.Rows);
            viewer.BringToFront();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>공백/대소문자 차이로 같은 이름·주소가 다른 그룹으로 갈리지 않도록 비교용으로만 정규화한다.</summary>
    private static string NormalizeForMatch(string? s) =>
        string.Join(" ", (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>운송장번호가 여럿이라 자동 적용을 못 하고 사용자도 건너뛴 건을 "확인" 열에 표시한다.</summary>
    private void MarkReviewFlags(HashSet<long> needsReviewIds)
    {
        foreach (DataGridViewRow row in _historyGrid.Rows)
        {
            if (row.DataBoundItem is not OutboundDetail d) continue;
            row.Cells["ReviewFlag"].Value = needsReviewIds.Contains(d.Id) ? "▶ 확인요망" : "";
        }
    }

    // ─── 송장번호 출력 ──────────────────────────────────────────────────────

    private void OnExportTrackingClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedDetails();
        if (selected.Count == 0)
        {
            MessageBox.Show("출력할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var saved = _settingsService.GetLastFolder("TrackingExportFields");
        var enabledKeys = saved != null
            ? saved.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
            : TrackingExportFieldDefs.Where(f => f.DefaultOn).Select(f => f.Key).ToHashSet();

        using var dialog = new TrackingFieldDialog(enabledKeys);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        var fieldKeys = dialog.SelectedKeys;
        _settingsService.SetLastFolder("TrackingExportFields", string.Join(",", fieldKeys));

        if (fieldKeys.Count == 0)
        {
            MessageBox.Show("출력할 필드를 하나 이상 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"송장번호_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("TrackingExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        _settingsService.SetLastFolder("TrackingExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExportTrackingToExcel(selected, fieldKeys, filePath);
            _statusLabel.Text = $"송장번호 {selected.Count}건을 출력했습니다.";
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ExportTrackingToExcel(List<OutboundDetail> rows, HashSet<string> fieldKeys, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("송장번호");

        var fields = TrackingExportFieldDefs.Where(f => fieldKeys.Contains(f.Key)).ToList();

        for (int c = 0; c < fields.Count; c++)
        {
            var cell = ws.Cells[1, c + 1];
            cell.Value = fields[c].Label;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var d = rows[r];
            for (int c = 0; c < fields.Count; c++)
                ws.Cells[r + 2, c + 1].Value = GetTrackingFieldValue(d, fields[c].Key);
        }

        if (ws.Dimension != null)
            ws.Cells[ws.Dimension.Address].AutoFitColumns();

        ExportHelper.SaveExcel(package, filePath);
    }

    private static object? GetTrackingFieldValue(OutboundDetail d, string key) => key switch
    {
        "ChannelCode"  => d.ChannelCode,
        "OrderNo"      => d.OrderNo,
        "Recipient"    => d.Recipient,
        "Address"      => d.Address,
        "ProductName"  => d.ProductName,
        "Remark"       => d.Remark,
        "MskuCode"     => d.MskuCode,
        "Qty"          => (object)d.Qty,
        "SupplyPrice"  => d.SupplyPrice,
        "TrackingNo"   => d.TrackingNo,
        "Status"       => d.Status,
        "CreatedAt"    => d.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
        "ConfirmedAt"  => d.ConfirmedAt?.ToString("yyyy-MM-dd HH:mm"),
        _              => null,
    };

    private sealed class TrackingFieldDialog : Form
    {
        private readonly Dictionary<string, CheckBox> _checks = new();

        public HashSet<string> SelectedKeys =>
            _checks.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToHashSet();

        public TrackingFieldDialog(HashSet<string> enabledKeys)
        {
            Text = "출력 필드 선택";
            Size = new Size(260, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(12, 10, 12, 8) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            var checksPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
            };

            foreach (var (key, label, _) in TrackingExportFieldDefs)
            {
                var cb = new CheckBox
                {
                    Text = label,
                    Checked = enabledKeys.Contains(key),
                    AutoSize = true,
                    Margin = new Padding(2, 5, 2, 0),
                };
                _checks[key] = cb;
                checksPanel.Controls.Add(cb);
            }

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 0, 0),
            };
            var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 70 };
            var btnOk = new Button { Text = "출력", DialogResult = DialogResult.OK, Width = 70 };
            btnPanel.Controls.AddRange([btnCancel, btnOk]);

            layout.Controls.Add(checksPanel, 0, 0);
            layout.Controls.Add(btnPanel, 0, 1);
            Controls.Add(layout);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}
