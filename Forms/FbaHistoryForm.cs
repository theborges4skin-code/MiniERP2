using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 아마존 FBA 발주 이력 조회창(기획서 §8). FboHistoryForm과 구조는 같지만, FBA는 채널 개념이
/// 없어(수취지 1곳 고정) 채널 필터가 없고, 선적명세 재출력 액션이 추가로 있다. 행 액션은
/// 발주 상세 열기 / 복사하여 신규 발주 / 하배출고이서·선적명세 재출력 / 발주 삭제 4가지다.
/// </summary>
public class FbaHistoryForm : Form
{
    private readonly FbaOrderRepository _orderRepository = new();
    private readonly FbaConfigRepository _configRepository = new();
    private readonly FbaCskuRepository _cskuRepository = new();
    private readonly SettingsService _settingsService = new();

    private const int ViewModeDetail = 0;
    private const int ViewModeByOrder = 1;
    private const int ViewModeByCsku = 2;

    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ComboBox _viewModeCombo = new();
    private TextBox _searchBox = new();
    private DataGridView _grid = new();
    private Label _statusLabel = new();
    private List<FbaHistoryRow> _rows = [];

    public FbaHistoryForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
    }

    private void InitializeComponent()
    {
        Text = "FBA 발주 이력";
        Size = new Size(1050, 620);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var actionBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 0) };

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        var btnQuickDate = DateRangeQuickSelect.CreateButton(_fromDatePicker, _toDatePicker);

        var btnQuery = new Button { Text = "조회", Size = new Size(70, 30) };
        btnQuery.Click += (s, e) => RunQuery();

        _viewModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(12, 3, 0, 0) };
        _viewModeCombo.Items.AddRange(["상세보기", "발주번호별", "CSKU별 집계"]);
        _viewModeCombo.SelectedIndex = ViewModeDetail;

        _searchBox = new TextBox { Width = 200, Margin = new Padding(12, 3, 0, 0), PlaceholderText = "검색(발주번호/Shipment ID/CSKU/운송장번호)" };
        _searchBox.TextChanged += (s, e) => Render();

        var btnOpenDetail = new Button { Text = "발주 상세 열기", Size = new Size(110, 30) };
        btnOpenDetail.Click += OnOpenDetailClick;
        var btnCopyAsNew = new Button { Text = "복사하여 신규 발주", Size = new Size(130, 30), Margin = new Padding(6, 0, 0, 0) };
        btnCopyAsNew.Click += OnCopyAsNewOrderClick;
        var btnExportCourier = new Button { Text = "하배출고이서 재출력", Size = new Size(140, 30), Margin = new Padding(6, 0, 0, 0) };
        btnExportCourier.Click += OnExportCourierClick;
        var btnExportShipment = new Button { Text = "선적명세 재출력", Size = new Size(120, 30), Margin = new Padding(6, 0, 0, 0) };
        btnExportShipment.Click += OnExportShipmentClick;
        // 선택한 발주 1건 또는 여러 건(일괄 선택)에 동일한 Shipment ID를 적용한다 — 아마존이
        // Shipment ID를 발주 확정 이후에야 발급하는 경우 FbaOrderForm을 다시 열지 않고 여기서
        // 바로 채워 넣기 위함(사용자 요청, 2026-08-10).
        var btnInputShipmentId = new Button { Text = "Shipment ID 입력", Size = new Size(120, 30), Margin = new Padding(12, 0, 0, 0) };
        btnInputShipmentId.Click += OnInputShipmentIdClick;
        // 운송장 결과 파일을 발주 작성 화면(FbaOrderForm)을 다시 열지 않고 이력에서 바로 반영한다
        // (FboHistoryForm과 동일한 편의 동작, 사용자 요청 2026-09-17). 매칭이 발주 단위가 아니라
        // 전체 미출고 박스 대상이므로 행 선택이 필요 없고, 뷰 전환에도 비활성화하지 않는다.
        var btnImportTracking = new Button { Text = "운송장 불러오기", Size = new Size(120, 30), Margin = new Padding(6, 0, 0, 0) };
        btnImportTracking.Click += OnImportTrackingClick;
        var btnDeleteOrder = new Button { Text = "발주 삭제", Size = new Size(90, 30), Margin = new Padding(12, 0, 0, 0) };
        btnDeleteOrder.Click += OnDeleteOrderClick;
        // 선택한 발주(들) 또는 선택이 없으면 현재 조회+검색된 전체 발주를 대상으로 박스 포장용
        // 작업지시서를 낸다(사용자 요청, 2026-08-10). CSKU별 집계 뷰에도 발주번호 구분 없이 "조회된
        // 전체" 대상으로는 계속 동작해야 하므로 다른 발주 단위 버튼들과 달리 뷰 전환에 따라
        // 비활성화하지 않는다.
        var btnIssueWorkOrder = new Button { Text = "작업지시서 발행", Size = new Size(110, 30), Margin = new Padding(12, 0, 0, 0) };
        btnIssueWorkOrder.Click += OnIssueWorkOrderClick;

        _viewModeCombo.SelectedIndexChanged += (s, e) =>
        {
            Render();
            var enableOrderActions = _viewModeCombo.SelectedIndex != ViewModeByCsku;
            btnOpenDetail.Enabled = enableOrderActions;
            btnCopyAsNew.Enabled = enableOrderActions;
            btnExportCourier.Enabled = enableOrderActions;
            btnExportShipment.Enabled = enableOrderActions;
            btnInputShipmentId.Enabled = enableOrderActions;
            btnDeleteOrder.Enabled = enableOrderActions;
        };

        toolStrip.Controls.AddRange(
        [
            new Label { Text = "기간:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) }, _fromDatePicker,
            new Label { Text = "~", AutoSize = true, Padding = new Padding(4, 5, 4, 0) }, _toDatePicker,
            btnQuickDate, btnQuery,
            new Label { Text = "보기:", AutoSize = true, Padding = new Padding(12, 5, 4, 0) }, _viewModeCombo,
            _searchBox,
        ]);
        actionBar.Controls.AddRange([btnOpenDetail, btnCopyAsNew, btnExportCourier, btnExportShipment, btnInputShipmentId, btnImportTracking, btnDeleteOrder, btnIssueWorkOrder]);

        // CellSelect(엑셀식 셀 단위 선택) — FullRowSelect였을 때는 셀 하나를 클릭해도 DataGridView가
        // 행 전체를 선택해버려서 "운송장번호 열만 드래그해서 복사"가 원리적으로 불가능했다(사용자
        // 요청, 2026-09-17). 셀 단위로 바꾸면 드래그한 범위만 복사된다. 발주 단위 버튼들은
        // SelectedRows(셀 단위 모드에서는 항상 비어있다) 대신 GetSelectedGridRows()로 판정한다.
        // ClipboardCopyMode를 명시해 열 머리글 문구가 값과 함께 복사되지 않게 한다.
        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            OnOpenDetailClick(s, e);
        };

        _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(actionBar, 0, 1);
        mainLayout.Controls.Add(_grid, 0, 2);
        mainLayout.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(mainLayout);

        RunQuery();
    }

    /// <summary>현재 그리드에서 선택된 행의 발주번호를 반환한다(선택 없거나 CSKU별 집계 뷰면 안내 후
    /// null). 발주번호 단위 액션(상세/복사/재출력/삭제) 버튼들이 공용으로 쓴다.</summary>
    private string? GetSelectedFbaNo()
    {
        if (_viewModeCombo.SelectedIndex == ViewModeByCsku)
        {
            MessageBox.Show("이 작업은 'CSKU별 집계' 뷰에서는 할 수 없습니다. 상세보기/발주번호별 뷰에서 시도하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        if (_grid.CurrentRow == null)
        {
            MessageBox.Show("대상 발주 건을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        var fbaNo = _grid.CurrentRow.Cells["FbaNo"].Value?.ToString();
        return string.IsNullOrWhiteSpace(fbaNo) ? null : fbaNo;
    }

    /// <summary>
    /// 선택된 셀들이 걸쳐 있는 행을 행 순서대로, 중복 없이 반환한다. 그리드가 CellSelect 모드라
    /// DataGridView.SelectedRows는 항상 비어있으므로(행 전체가 선택되는 모드에서만 채워진다) 발주
    /// 단위 버튼들은 이 메서드로 선택을 판정해야 한다. 아무것도 선택돼 있지 않으면 현재 셀이 있는
    /// 행 하나로 간주한다(클릭만 하고 드래그하지 않은 경우).
    /// </summary>
    private List<DataGridViewRow> GetSelectedGridRows()
    {
        var rows = _grid.SelectedCells.Cast<DataGridViewCell>()
            .Select(c => c.RowIndex)
            .Distinct()
            .OrderBy(index => index)
            .Select(index => _grid.Rows[index])
            .ToList();
        if (rows.Count > 0) return rows;
        return _grid.CurrentRow == null ? [] : [_grid.CurrentRow];
    }

    /// <summary>그리드에서 일괄(드래그 또는 Ctrl/Shift+클릭) 선택된 행들의 발주번호를 중복 없이
    /// 반환한다(선택 없거나 CSKU별 집계 뷰면 안내 후 null). "Shipment ID 입력"이 여러 발주를
    /// 한 번에 처리할 때 쓴다 — 상세보기 뷰에서는 같은 발주의 여러 라인이 함께 선택될 수 있어
    /// Distinct가 필요하다.</summary>
    private List<string>? GetSelectedFbaNos()
    {
        if (_viewModeCombo.SelectedIndex == ViewModeByCsku)
        {
            MessageBox.Show("이 작업은 'CSKU별 집계' 뷰에서는 할 수 없습니다. 상세보기/발주번호별 뷰에서 시도하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var selectedRows = GetSelectedGridRows();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show("대상 발주 건을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var fbaNos = selectedRows
            .Select(r => r.Cells["FbaNo"].Value?.ToString())
            .Where(no => !string.IsNullOrWhiteSpace(no))
            .Select(no => no!)
            .Distinct()
            .ToList();
        return fbaNos.Count == 0 ? null : fbaNos;
    }

    /// <summary>선택한 발주 1건 또는 여러 건에 동일한 Shipment ID를 일괄 적용한다.</summary>
    private void OnInputShipmentIdClick(object? sender, EventArgs e)
    {
        var fbaNos = GetSelectedFbaNos();
        if (fbaNos == null) return;

        using var dlg = new FbaShipmentIdInputDialog(fbaNos.Count);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK) return;

        foreach (var fbaNo in fbaNos)
            _orderRepository.UpdateShipmentId(fbaNo, dlg.ShipmentId);

        RunQuery();
        _statusLabel.Text = $"발주 {fbaNos.Count}건에 Shipment ID '{dlg.ShipmentId}'를 적용했습니다.";
    }

    /// <summary>택배사(CJ)에서 내려받은 운송장 결과 파일("운송장출력데이터 상세")을 가공 없이 그대로
    /// 읽어 미출고 박스의 운송장번호를 채운다(§7.2). 매칭은 고객주문번호 칸에 실려오는
    /// "[SEND] {Shipment ID} 총 N박스중 M번째"에서 뽑은 (Shipment ID, 박스순번)으로 하고, FBA
    /// 형식이 아닌 행(FBO·일반 택배)은 자동으로 건너뛴다(FbaTrackingImporter). 대상이 발주 단위가
    /// 아니라 전체 박스이므로 발주 작성 화면에서 불러오든 여기서 불러오든 결과는 같다. 미매칭이나
    /// 운송장번호 불일치가 하나라도 있으면 아무것도 반영하지 않고 목록을 보여준다(부분 반영 금지).</summary>
    private void OnImportTrackingClick(object? sender, EventArgs e)
    {
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
                MessageBox.Show(result.BuildFailureMessage(), "운송장 불러오기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RunQuery();
            _statusLabel.Text = result.BuildSuccessSummary();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>"작업지시서 발행"의 대상 발주번호를 정한다 — 그리드에 선택된 행이 있으면(상세보기/
    /// 발주번호별 뷰) 그 발주들, 없으면(또는 CSKU별 집계 뷰라 선택 개념이 없으면) 현재 조회+검색된
    /// 전체 발주로 자동 대체한다("임의 또는 조회된 발주건" — 사용자 요청).</summary>
    private List<string> GetTargetFbaNosForWorkOrder()
    {
        if (_viewModeCombo.SelectedIndex != ViewModeByCsku)
        {
            var fbaNos = GetSelectedGridRows()
                .Select(r => r.Cells["FbaNo"].Value?.ToString())
                .Where(no => !string.IsNullOrWhiteSpace(no))
                .Select(no => no!)
                .Distinct()
                .ToList();
            if (fbaNos.Count > 0) return fbaNos;
        }

        return GetFilteredRows().Select(r => r.FbaNo).Distinct().ToList();
    }

    private void OnIssueWorkOrderClick(object? sender, EventArgs e)
    {
        var fbaNos = GetTargetFbaNosForWorkOrder();
        if (fbaNos.Count == 0)
        {
            MessageBox.Show("대상 발주 건이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var orderSets = fbaNos
            .Select(no => _orderRepository.GetOrder(no))
            .Where(o => o.Order != null && o.Boxes.Count > 0)
            .Select(o => new FbaWorkOrderExporter.OrderBoxSet(o.Order!.FbaNo, o.Boxes, o.Items))
            .ToList();
        if (orderSets.Count == 0)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var defaultName = orderSets.Count == 1
            ? $"FBA작업지시서_{orderSets[0].FbaNo}_{DateTime.Now:yyyyMMdd}.xlsx"
            : $"FBA작업지시서_{DateTime.Now:yyyyMMdd}.xlsx";
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx", defaultName,
            _settingsService.GetLastFolder("FbaWorkOrderExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("FbaWorkOrderExport", Path.GetDirectoryName(filePath)!);

        try
        {
            FbaWorkOrderExporter.Export(orderSets, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnOpenDetailClick(object? sender, EventArgs e)
    {
        var fbaNo = GetSelectedFbaNo();
        if (fbaNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fbaNo);
        if (order == null)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var detailDialog = new FbaOrderDetailDialog(order, boxes, items);
        FormManager.ApplyBoundsTracking(detailDialog);
        FormManager.ShowDialogSafe(detailDialog, this);
    }

    /// <summary>선택한 발주(FbaNo) 전체를 그대로 복사해 새 발주 작성 화면을 새 창으로 연다.
    /// FormManager.Show(싱글턴)를 쓰지 않는다 — 이미 작성 중인 다른 발주가 열려 있어도 그걸
    /// 덮어쓰지 않고 별개의 새 창으로 열어야 하기 때문이다.</summary>
    private void OnCopyAsNewOrderClick(object? sender, EventArgs e)
    {
        var fbaNo = GetSelectedFbaNo();
        if (fbaNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fbaNo);
        if (order == null)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var copyForm = new FbaOrderForm(order, boxes, items);
        FormManager.ApplyBoundsTracking(copyForm);
        copyForm.Show();
    }

    /// <summary>선택한 발주를 지금 저장된 스냅샷 그대로 하배출고이서로 다시 뽑는다. 재출력이므로
    /// 발주 상태는 건드리지 않는다(§8 — 재출력이 항상 동일한 값을 내려면 스냅샷 컬럼이 필수).</summary>
    private void OnExportCourierClick(object? sender, EventArgs e)
    {
        var fbaNo = GetSelectedFbaNo();
        if (fbaNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fbaNo);
        if (order == null || boxes.Count == 0)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"FBA출고_{fbaNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("FbaOrderExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("FbaOrderExport", Path.GetDirectoryName(filePath)!);

        try
        {
            FbaCourierExporter.Export(order, _configRepository.GetDefault(), boxes, items, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>선택한 발주를 지금 저장된 스냅샷 그대로 아마존 선적명세로 다시 뽑는다.</summary>
    private void OnExportShipmentClick(object? sender, EventArgs e)
    {
        var fbaNo = GetSelectedFbaNo();
        if (fbaNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fbaNo);
        if (order == null || boxes.Count == 0)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"FBA선적명세_{fbaNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("FbaShipmentExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("FbaShipmentExport", Path.GetDirectoryName(filePath)!);

        try
        {
            FbaShipmentExporter.Export(boxes, items, _cskuRepository.GetAll(), filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>선택한 발주(FbaNo)를 이력에서 완전히 삭제한다(헤더+박스+품목 전부). 운송장번호가
    /// 이미 등록된(=출고완료) 박스가 하나라도 있으면 실수로 지웠을 때 되돌릴 수 없다는 걸 강조하는
    /// 경고 문구로 한 번 더 확인한다.</summary>
    private void OnDeleteOrderClick(object? sender, EventArgs e)
    {
        var fbaNo = GetSelectedFbaNo();
        if (fbaNo == null) return;

        var (order, boxes, _) = _orderRepository.GetOrder(fbaNo);
        if (order == null)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var isShipped = boxes.Any(b => !string.IsNullOrEmpty(b.TrackingNo));
        var message = isShipped
            ? $"발주 '{fbaNo}'는 이미 운송장번호가 등록된 박스가 있습니다.\n삭제하면 이 이력이 완전히 사라지고 되돌릴 수 없습니다.\n정말 삭제하시겠습니까?"
            : $"발주 '{fbaNo}'을(를) 삭제하시겠습니까? 되돌릴 수 없습니다.";
        var confirm = MessageBox.Show(message, "발주 이력 삭제", MessageBoxButtons.YesNo,
            isShipped ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _orderRepository.DeleteOrder(fbaNo);
        RunQuery();
        _statusLabel.Text = $"발주 '{fbaNo}'을(를) 삭제했습니다.";
    }

    private void RunQuery()
    {
        _rows = _orderRepository.GetHistory(_fromDatePicker.Value.Date, _toDatePicker.Value.Date);
        Render();
    }

    /// <summary>현재 검색어로 걸러진 조회 결과. Render()와 "작업지시서 발행"(선택 없을 때 조회된
    /// 전체를 대상으로 하는 기본 동작)이 공용으로 쓴다.</summary>
    private List<FbaHistoryRow> GetFilteredRows()
    {
        var search = _searchBox.Text.Trim();
        return _rows.Where(r =>
            search.Length == 0 ||
            KoreanSearch.Matches(r.FbaNo, search) ||
            KoreanSearch.Matches(r.ShipmentId, search) ||
            KoreanSearch.Matches(r.Csku, search) ||
            KoreanSearch.Matches(r.ItemName, search) ||
            KoreanSearch.Matches(r.TrackingNo, search)
        ).ToList();
    }

    private void Render()
    {
        var filtered = GetFilteredRows();

        _grid.Columns.Clear();
        _grid.Rows.Clear();

        if (_viewModeCombo.SelectedIndex == ViewModeByCsku)
        {
            _grid.Columns.Add("Csku", "CSKU");
            _grid.Columns.Add("ItemName", "품목명");
            _grid.Columns.Add("BoxCount", "박스수");
            _grid.Columns.Add("TotalQty", "수량합");

            var groups = filtered.GroupBy(r => (r.Csku, r.ItemName))
                .Select(g => new
                {
                    g.Key.Csku,
                    g.Key.ItemName,
                    BoxCount = g.Select(r => (r.FbaNo, r.BoxSeq)).Distinct().Count(),
                    TotalQty = g.Sum(r => r.Qty),
                })
                .OrderByDescending(g => g.TotalQty);

            foreach (var g in groups)
            {
                _grid.Rows.Add(g.Csku, g.ItemName, g.BoxCount, g.TotalQty);
            }
        }
        else if (_viewModeCombo.SelectedIndex == ViewModeByOrder)
        {
            _grid.Columns.Add("FbaNo", "발주번호");
            _grid.Columns.Add("OrderDate", "발주일");
            _grid.Columns.Add("ShipmentId", "Shipment ID");
            _grid.Columns.Add("BoxCount", "박스수");
            _grid.Columns.Add("TotalQty", "총수량");
            _grid.Columns.Add("TrackingStatus", "운송장 상태");

            var groups = filtered.GroupBy(r => r.FbaNo)
                .Select(g =>
                {
                    var boxes = g.GroupBy(r => r.BoxSeq).Select(bg => bg.First()).ToList();
                    return new
                    {
                        FbaNo = g.Key,
                        OrderDate = g.First().OrderDate,
                        ShipmentId = g.First().ShipmentId,
                        BoxCount = boxes.Count,
                        TotalQty = g.Sum(r => r.Qty),
                        TrackedCount = boxes.Count(b => !string.IsNullOrEmpty(b.TrackingNo)),
                    };
                })
                .OrderByDescending(g => g.OrderDate).ThenByDescending(g => g.FbaNo);

            foreach (var g in groups)
            {
                var trackingStatus = g.TrackedCount == 0 ? "미등록" : g.TrackedCount == g.BoxCount ? "전체등록" : $"{g.TrackedCount}/{g.BoxCount}건 등록";
                _grid.Rows.Add(g.FbaNo, g.OrderDate.ToString("yyyy-MM-dd"), g.ShipmentId!, g.BoxCount, g.TotalQty, trackingStatus);
            }
        }
        else
        {
            _grid.Columns.Add("FbaNo", "발주번호");
            _grid.Columns.Add("OrderDate", "발주일");
            _grid.Columns.Add("ShipmentId", "Shipment ID");
            _grid.Columns.Add("BoxSeq", "박스");
            _grid.Columns.Add("Csku", "CSKU");
            _grid.Columns.Add("ItemName", "품목명");
            _grid.Columns.Add("Qty", "수량");
            _grid.Columns.Add("ExpiryDate", "유통기한");
            _grid.Columns.Add("TrackingNo", "운송장번호");
            _grid.Columns.Add("Status", "상태");

            foreach (var r in filtered)
            {
                _grid.Rows.Add(r.FbaNo, r.OrderDate.ToString("yyyy-MM-dd"), r.ShipmentId!, r.BoxSeq,
                    r.Csku, r.ItemName, r.Qty, r.ExpiryDate!, r.TrackingNo!, r.Status);
            }
        }

        _statusLabel.Text = $"{filtered.Count}건 조회됨 (원본 {_rows.Count}건 중)";
    }
}
