using MiniERP2.Config;
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
        var btnDeleteOrder = new Button { Text = "발주 삭제", Size = new Size(90, 30), Margin = new Padding(12, 0, 0, 0) };
        btnDeleteOrder.Click += OnDeleteOrderClick;

        _viewModeCombo.SelectedIndexChanged += (s, e) =>
        {
            Render();
            var enableOrderActions = _viewModeCombo.SelectedIndex != ViewModeByCsku;
            btnOpenDetail.Enabled = enableOrderActions;
            btnCopyAsNew.Enabled = enableOrderActions;
            btnExportCourier.Enabled = enableOrderActions;
            btnExportShipment.Enabled = enableOrderActions;
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
        actionBar.Controls.AddRange([btnOpenDetail, btnCopyAsNew, btnExportCourier, btnExportShipment, btnDeleteOrder]);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
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

    private void Render()
    {
        var search = _searchBox.Text.Trim();

        var filtered = _rows.Where(r =>
            search.Length == 0 ||
            r.FbaNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            (r.ShipmentId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
            r.Csku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            r.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            (r.TrackingNo?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        ).ToList();

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
                _grid.Rows.Add(g.FbaNo, g.OrderDate.ToString("yyyy-MM-dd"), g.ShipmentId, g.BoxCount, g.TotalQty, trackingStatus);
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
            _grid.Columns.Add("TrackingNo", "운송장번호");
            _grid.Columns.Add("Status", "상태");

            foreach (var r in filtered)
            {
                _grid.Rows.Add(r.FbaNo, r.OrderDate.ToString("yyyy-MM-dd"), r.ShipmentId, r.BoxSeq,
                    r.Csku, r.ItemName, r.Qty, r.TrackingNo, r.Status);
            }
        }

        _statusLabel.Text = $"{filtered.Count}건 조회됨 (원본 {_rows.Count}건 중)";
    }
}
