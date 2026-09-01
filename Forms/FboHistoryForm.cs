using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 네이버 풀필먼트(FBO) 발주/출고 이력 조회창. 기간+채널로 박스-품목 단위 이력을 조회하고,
/// "보기" 콤보로 상세(박스-품목)/발주번호별/CSKU별 집계 3가지 뷰로 전환할 수 있다(기획서 §9).
/// 재고 차감/관리는 하지 않으므로 여기서 보여주는 건 순수 출고 이력 집계일 뿐이다.
/// </summary>
public class FboHistoryForm : Form
{
    private readonly FboOrderRepository _orderRepository = new();
    private readonly FboChannelConfigRepository _channelConfigRepository = new();
    private readonly SettingsService _settingsService = new();

    private const int ViewModeDetail = 0;
    private const int ViewModeByOrder = 1;
    private const int ViewModeByCsku = 2;

    private ComboBox _channelCombo = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ComboBox _viewModeCombo = new();
    private TextBox _searchBox = new();
    private DataGridView _grid = new();
    private Label _statusLabel = new();
    private List<FboChannelConfigModel> _channels = [];
    private List<FboHistoryRow> _rows = [];

    public FboHistoryForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
    }

    private void InitializeComponent()
    {
        Text = "풀필먼트 발주 이력";
        Size = new Size(1050, 620);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        // 1행: 채널/기간/보기/검색 — 조회 조건
        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        // 2행: 발주번호 단위 액션 버튼들 — 예전엔 1행에 전부 나열해서 창을 좁히면 뒤쪽 버튼이
        // 안 보였다(가로로 최대한 늘려야만 다 보임). 조회 조건과 분리한 별도 줄로 옮긴다.
        var actionBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 0) };

        _channels = [new FboChannelConfigModel { ChannelId = "", ChannelName = "(전체)" }, .. _channelConfigRepository.GetAll()];
        _channelCombo = new ComboBox { Size = new Size(160, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _channelCombo.DataSource = _channels;
        _channelCombo.DisplayMember = nameof(FboChannelConfigModel.ChannelName);
        _channelCombo.ValueMember = nameof(FboChannelConfigModel.ChannelId);

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        var btnQuickDate = DateRangeQuickSelect.CreateButton(_fromDatePicker, _toDatePicker);

        var btnQuery = new Button { Text = "조회", Size = new Size(70, 30) };
        btnQuery.Click += (s, e) => RunQuery();

        _viewModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(12, 3, 0, 0) };
        _viewModeCombo.Items.AddRange(["상세보기", "발주번호별", "CSKU별 집계"]);
        _viewModeCombo.SelectedIndex = ViewModeDetail;

        _searchBox = new TextBox { Width = 170, Margin = new Padding(12, 3, 0, 0), PlaceholderText = "검색(CSKU/품목명/발주번호/이송장)" };
        _searchBox.TextChanged += (s, e) => Render();

        // 과거에 나갔던 발주를 그대로 불러와 새 발주를 만드는 기능 — 매번 CSKU를 새로 골라야 하는
        // 불편을 없앤다. CSKU별 집계 뷰는 여러 발주가 섞인 합계라 원본 발주 1건을 특정할 수 없으므로
        // 발주번호가 살아있는 다른 두 뷰(상세보기/발주번호별)에서만 쓸 수 있게 한다.
        var btnCopyAsNew = new Button { Text = "복사하여 신규 발주", Size = new Size(130, 30) };
        btnCopyAsNew.Click += OnCopyAsNewOrderClick;
        // 발주확정은 했는데 그때 뽑아둔 택배사 양식(하배출고이서) 파일 실물을 지우거나 잃어버렸을
        // 때 재출력하는 용도 — FboOrderForm을 다시 열 필요 없이 이력 조회창에서 바로 재출력한다.
        var btnExportCourierForm = new Button { Text = "택배사 양식 출력하기", Size = new Size(140, 30), Margin = new Padding(6, 0, 0, 0) };
        btnExportCourierForm.Click += OnExportCourierFormClick;
        // 발주확정은 했는데 그때 뽑아둔 입고양식 파일 실물을 잃어버렸을 때 재출력하는 용도 —
        // FboOrderForm을 다시 열 필요 없이 이력 조회창에서 바로 재변환한다.
        var btnConvertToInbound = new Button { Text = "입고양식 파일 변환", Size = new Size(140, 30), Margin = new Padding(6, 0, 0, 0) };
        btnConvertToInbound.Click += OnConvertToInboundClick;
        // 과거에 발주확정한 건이 아직 이송장번호가 없어 "입고양식 파일 변환"이 막히는 경우, 발주
        // 작성 화면을 새로 열 필요 없이 여기서 바로 이송장 결과를 반영할 수 있게 한다.
        var btnImportTracking = new Button { Text = "운송장번호 불러오기", Size = new Size(140, 30), Margin = new Padding(6, 0, 0, 0) };
        btnImportTracking.Click += OnImportTrackingClick;
        // 잘못 등록했거나 취소된 발주를 이력에서 지운다. 이미 출고확정(이송장번호 등록)된 건은
        // 실수로 지우면 되돌릴 수 없으므로 더 강한 경고 문구로 한 번 더 확인한다.
        var btnDeleteOrder = new Button { Text = "선택 이력 삭제", Size = new Size(110, 30), Margin = new Padding(12, 0, 0, 0) };
        btnDeleteOrder.Click += OnDeleteOrderClick;

        // 상세보기(라인 단위) 전용 액션 — 지금까지는 발주(FboNo) 전체로만 복사/삭제가 묶여 있어
        // CSKU 한 줄만 고치거나 지우려 해도 발주 전체를 다시 만들어야 했다(사용자 신고, 2026-07-30).
        var btnEditLine = new Button { Text = "선택 라인 수정", Size = new Size(110, 30), Margin = new Padding(12, 0, 0, 0) };
        btnEditLine.Click += OnEditLineClick;
        var btnCopyLine = new Button { Text = "선택 라인 복사(신규발주)", Size = new Size(150, 30), Margin = new Padding(6, 0, 0, 0) };
        btnCopyLine.Click += OnCopyLineAsNewOrderClick;
        var btnDeleteLine = new Button { Text = "선택 라인 삭제", Size = new Size(110, 30), Margin = new Padding(6, 0, 0, 0) };
        btnDeleteLine.Click += OnDeleteLineClick;

        _viewModeCombo.SelectedIndexChanged += (s, e) =>
        {
            Render();
            var enableOrderActions = _viewModeCombo.SelectedIndex != ViewModeByCsku;
            btnCopyAsNew.Enabled = enableOrderActions;
            btnExportCourierForm.Enabled = enableOrderActions;
            btnConvertToInbound.Enabled = enableOrderActions;
            btnDeleteOrder.Enabled = enableOrderActions;

            var enableLineActions = _viewModeCombo.SelectedIndex == ViewModeDetail;
            btnEditLine.Enabled = enableLineActions;
            btnCopyLine.Enabled = enableLineActions;
            btnDeleteLine.Enabled = enableLineActions;
        };

        toolStrip.Controls.AddRange(
        [
            new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) }, _channelCombo,
            new Label { Text = "기간:", AutoSize = true, Padding = new Padding(10, 5, 4, 0) }, _fromDatePicker,
            new Label { Text = "~", AutoSize = true, Padding = new Padding(4, 5, 4, 0) }, _toDatePicker,
            btnQuickDate, btnQuery,
            new Label { Text = "보기:", AutoSize = true, Padding = new Padding(12, 5, 4, 0) }, _viewModeCombo,
            _searchBox,
        ]);
        actionBar.Controls.AddRange(
        [
            btnCopyAsNew, btnExportCourierForm, btnConvertToInbound, btnImportTracking, btnDeleteOrder,
            btnEditLine, btnCopyLine, btnDeleteLine,
        ]);
        // 기본 뷰가 상세보기(ViewModeDetail)이므로 라인 액션도 처음부터 활성화한다(위
        // SelectedIndexChanged는 이후 사용자가 뷰를 바꿀 때만 실행되므로 초기값은 직접 맞춰야 함).

        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        // "발주번호별" 뷰에서만 의미가 있다 — 다른 뷰에서는 OnGridCellDoubleClick 안에서 무시한다.
        _grid.CellDoubleClick += OnGridCellDoubleClick;
        // 상세보기에서만 "이송장번호" 셀을 직접 입력할 수 있게 한다(Render()에서 그리드/열별
        // ReadOnly를 뷰에 맞게 재조정함). 파일로 불러오는 대신 소량 건을 바로 고치는 용도.
        _grid.CellEndEdit += OnTrackingNoCellEndEdit;

        _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(actionBar, 0, 1);
        mainLayout.Controls.Add(_grid, 0, 2);
        mainLayout.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(mainLayout);

        RunQuery();
    }

    /// <summary>현재 그리드에서 선택된 행의 발주번호를 반환한다(선택 없으면 안내 후 null). 발주번호
    /// 단위 액션(복사/재출력) 버튼들이 공용으로 쓴다.</summary>
    private string? GetSelectedFboNo()
    {
        if (_grid.CurrentRow == null)
        {
            MessageBox.Show("대상 발주 건을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        var fboNo = _grid.CurrentRow.Cells["FboNo"].Value?.ToString();
        return string.IsNullOrWhiteSpace(fboNo) ? null : fboNo;
    }

    /// <summary>상세보기(라인 단위) 뷰에서 현재 선택된 CSKU 라인 1건을 (FboNo, BoxSeq, ItemSeq)로
    /// 특정한다. 다른 뷰에서는 ItemSeq 열 자체가 없으므로 null을 반환한다.</summary>
    private (string FboNo, int BoxSeq, int ItemSeq)? GetSelectedLine()
    {
        if (_viewModeCombo.SelectedIndex != ViewModeDetail)
        {
            MessageBox.Show("라인 단위 작업은 '상세보기'에서만 가능합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        if (_grid.CurrentRow == null)
        {
            MessageBox.Show("대상 라인을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var fboNo = _grid.CurrentRow.Cells["FboNo"].Value?.ToString();
        var boxSeq = _grid.CurrentRow.Cells["BoxSeq"].Value?.ToString();
        var itemSeq = _grid.CurrentRow.Cells["ItemSeq"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(fboNo) || !int.TryParse(boxSeq, out var b) || !int.TryParse(itemSeq, out var i))
            return null;

        return (fboNo, b, i);
    }

    /// <summary>선택한 라인이 속한 박스가 이미 출고확정(이송장번호 등록)됐는지 확인한다(수정/삭제 시
    /// 강한 경고 문구를 붙이기 위함 — 이미 나간 실적을 조용히 바꾸면 안 되므로).</summary>
    private bool IsLineShipped(string fboNo, int boxSeq) =>
        !string.IsNullOrEmpty(_orderRepository.GetBox(fboNo, boxSeq)?.TrackingNo);

    /// <summary>선택한 CSKU 라인 1건을 수정한다(§선택 라인 액션). 박스/발주 헤더는 건드리지 않는다.</summary>
    private void OnEditLineClick(object? sender, EventArgs e)
    {
        var line = GetSelectedLine();
        if (line == null) return;
        var (fboNo, boxSeq, itemSeq) = line.Value;

        var (order, _, items) = _orderRepository.GetOrder(fboNo);
        var item = items.FirstOrDefault(i => i.BoxSeq == boxSeq && i.ItemSeq == itemSeq);
        if (order == null || item == null)
        {
            MessageBox.Show("라인 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (IsLineShipped(fboNo, boxSeq))
        {
            var proceed = MessageBox.Show(
                $"박스{boxSeq}는 이미 출고확정(이송장번호 등록) 상태입니다.\n이미 나간 실적 라인을 수정하면 실제 발송 내용과 기록이 어긋날 수 있습니다.\n계속하시겠습니까?",
                "출고확정 건 수정 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (proceed != DialogResult.Yes) return;
        }

        using var dlg = new FboBoxItemEditDialog(fboNo, boxSeq, item.Csku, item.ItemName, item.Qty, item.ExpiryDate);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK) return;

        item.Csku = dlg.Csku;
        item.ItemName = dlg.ItemName;
        item.Qty = dlg.Qty;
        item.ExpiryDate = dlg.ExpiryDate;
        _orderRepository.UpdateBoxItem(item);

        RunQuery();
        _statusLabel.Text = $"'{fboNo}' 박스{boxSeq}의 라인을 수정했습니다.";
    }

    /// <summary>선택한 CSKU 라인 1건만 복사해 새 발주 작성 화면(박스1·라인1짜리 새 발주 초안)을
    /// 연다(§선택 라인 액션 — 발주 전체가 아니라 이 한 줄만 다시 발주하고 싶을 때).</summary>
    private void OnCopyLineAsNewOrderClick(object? sender, EventArgs e)
    {
        var line = GetSelectedLine();
        if (line == null) return;
        var (fboNo, boxSeq, itemSeq) = line.Value;

        var (order, boxes, items) = _orderRepository.GetOrder(fboNo);
        var item = items.FirstOrDefault(i => i.BoxSeq == boxSeq && i.ItemSeq == itemSeq);
        var box = boxes.FirstOrDefault(b => b.BoxSeq == boxSeq);
        if (order == null || item == null)
        {
            MessageBox.Show("라인 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // FboOrderForm(templateOrder, ...) 생성자가 내부적으로 새 FboNo를 발급하고 박스/라인 번호를
        // 다시 매기므로(RecomputeBoxIdentifiers), 여기서는 박스1·라인1짜리 최소 구성만 만들면 된다.
        var templateBox = new FboBox { FboNo = order.FboNo, BoxSeq = 1, ReceiverDisplayName = box?.ReceiverDisplayName ?? "", MatchKey = "", BoxType = box?.BoxType ?? "소" };
        var templateItem = new FboBoxItem
        {
            FboNo = order.FboNo,
            BoxSeq = 1,
            ItemSeq = 1,
            Csku = item.Csku,
            FboItemCode = item.FboItemCode,
            ItemName = item.ItemName,
            InvoiceDisplayName = item.InvoiceDisplayName,
            QtyPerBox = item.QtyPerBox,
            Qty = item.Qty,
            ExpiryDate = item.ExpiryDate,
        };

        var copyForm = new FboOrderForm(order, [templateBox], [templateItem]);
        FormManager.ApplyBoundsTracking(copyForm);
        copyForm.Show();
    }

    /// <summary>선택한 CSKU 라인 1건만 삭제한다(§선택 라인 액션). 박스/발주 헤더는 남는다.</summary>
    private void OnDeleteLineClick(object? sender, EventArgs e)
    {
        var line = GetSelectedLine();
        if (line == null) return;
        var (fboNo, boxSeq, itemSeq) = line.Value;

        var isShipped = IsLineShipped(fboNo, boxSeq);
        var message = isShipped
            ? $"'{fboNo}' 박스{boxSeq}는 이미 출고확정(이송장번호 등록) 상태입니다.\n이 CSKU 라인을 삭제하면 되돌릴 수 없습니다.\n정말 삭제하시겠습니까?"
            : $"'{fboNo}' 박스{boxSeq}의 이 CSKU 라인을 삭제하시겠습니까? 되돌릴 수 없습니다.";
        var confirm = MessageBox.Show(message, "라인 삭제 확인", MessageBoxButtons.YesNo,
            isShipped ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _orderRepository.DeleteBoxItem(fboNo, boxSeq, itemSeq);
        RunQuery();
        _statusLabel.Text = $"'{fboNo}' 박스{boxSeq}의 라인 1건을 삭제했습니다.";
    }

    /// <summary>선택한 행이 속한 발주(FboNo) 전체를 그대로 복사해 새 발주 작성 화면을 새 창으로 연다.
    /// FormManager.Show(싱글턴)를 쓰지 않는다 — 이미 작성 중인 다른 발주가 열려 있어도 그걸
    /// 덮어쓰지 않고 별개의 새 창으로 열어야 하기 때문이다.</summary>
    private void OnCopyAsNewOrderClick(object? sender, EventArgs e)
    {
        var fboNo = GetSelectedFboNo();
        if (fboNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fboNo);
        if (order == null)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var copyForm = new FboOrderForm(order, boxes, items);
        FormManager.ApplyBoundsTracking(copyForm);
        copyForm.Show();
    }

    /// <summary>
    /// 선택한 발주(FboNo)를 지금 저장된 그대로 택배사 양식(하배출고이서)으로 다시 뽑는다. 발주는
    /// 이미 확정했지만 그때 뽑아둔 파일 실물을 지우거나 잃어버렸을 때 재출력하는 용도라, 발주
    /// 상태는 건드리지 않는다(단순 재출력이라 상태 갱신 불필요).
    /// </summary>
    private void OnExportCourierFormClick(object? sender, EventArgs e)
    {
        var fboNo = GetSelectedFboNo();
        if (fboNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fboNo);
        if (order == null || boxes.Count == 0)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var channel = _channels.FirstOrDefault(c => c.ChannelId == order.ChannelId);
        if (channel == null)
        {
            MessageBox.Show("채널설정을 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"풀필먼트출고_{fboNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("FboOrderExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("FboOrderExport", Path.GetDirectoryName(filePath)!);

        try
        {
            FboOrderExporter.Export(order, channel, boxes, items, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 선택한 발주(FboNo)를 지금 상태 그대로(이미 등록된 이송장번호 포함) 입고양식 파일로 다시
    /// 뽑는다. 발주확정은 이미 했지만 그때 만든 입고양식 파일 실물을 잃어버렸을 때 재출력하는
    /// 용도라, FboOrderForm의 "입고양식 파일 변환"과 같은 조건(모든 박스에 이송장번호 필요)을
    /// 그대로 적용한다.
    /// </summary>
    private void OnConvertToInboundClick(object? sender, EventArgs e)
    {
        var fboNo = GetSelectedFboNo();
        if (fboNo == null) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fboNo);
        if (order == null)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var channel = _channels.FirstOrDefault(c => c.ChannelId == order.ChannelId);
        if (channel == null)
        {
            MessageBox.Show("채널설정을 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (boxes.Any(b => string.IsNullOrEmpty(b.TrackingNo)))
        {
            MessageBox.Show("이송장번호가 없는 박스가 있어 입고양식으로 변환할 수 없습니다. 발주 작성 화면에서 먼저 운송장을 불러오세요.", "변환 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"풀필먼트입고_{fboNo}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("FboInboundExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("FboInboundExport", Path.GetDirectoryName(filePath)!);

        try
        {
            FboInboundExporter.Export(channel, boxes, items, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 선택한 발주(FboNo)를 이력에서 완전히 삭제한다(헤더+박스+품목 전부). 이송장번호가 이미
    /// 등록된(=출고확정) 박스가 하나라도 있으면 실수로 지웠을 때 되돌릴 수 없다는 걸 강조하는
    /// 경고 문구로 한 번 더 확인한다.
    /// </summary>
    private void OnDeleteOrderClick(object? sender, EventArgs e)
    {
        var fboNo = GetSelectedFboNo();
        if (fboNo == null) return;

        var (order, boxes, _) = _orderRepository.GetOrder(fboNo);
        if (order == null)
        {
            MessageBox.Show("발주 정보를 찾을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var isShipped = boxes.Any(b => !string.IsNullOrEmpty(b.TrackingNo));
        var message = isShipped
            ? $"발주 '{fboNo}'는 이미 출고확정(운송장번호 등록) 상태입니다.\n삭제하면 이 이력이 완전히 사라지고 되돌릴 수 없습니다.\n정말 삭제하시겠습니까?"
            : $"발주 '{fboNo}'을(를) 삭제하시겠습니까? 되돌릴 수 없습니다.";
        var confirm = MessageBox.Show(message, "발주 이력 삭제", MessageBoxButtons.YesNo,
            isShipped ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _orderRepository.DeleteOrder(fboNo);
        RunQuery();
        _statusLabel.Text = $"발주 '{fboNo}'을(를) 삭제했습니다.";
    }

    /// <summary>"발주번호별" 뷰에서 행을 더블클릭하면 그 발주의 박스/품목 세부내역을 읽기전용
    /// 새 창으로 보여준다.</summary>
    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (_viewModeCombo.SelectedIndex != ViewModeByOrder) return;
        if (e.RowIndex < 0) return;

        var fboNo = _grid.Rows[e.RowIndex].Cells["FboNo"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(fboNo)) return;

        var (order, boxes, items) = _orderRepository.GetOrder(fboNo);
        if (order == null) return;
        var channelName = _channels.FirstOrDefault(c => c.ChannelId == order.ChannelId)?.ChannelName ?? order.ChannelId;

        using var detailDialog = new FboOrderDetailDialog(order, boxes, items, channelName);
        FormManager.ApplyBoundsTracking(detailDialog);
        FormManager.ShowDialogSafe(detailDialog, this);
    }

    /// <summary>과거에 발주확정한 건의 이송장 결과를 여기서 바로 반영한다(FboOrderForm을 열지 않아도
    /// 됨). 매칭은 반품부명 기준으로 전체 미출고 박스를 대상으로 하므로(FboTrackingImporter),
    /// 어떤 발주 화면에서 불러오든 결과는 같다.</summary>
    private void OnImportTrackingClick(object? sender, EventArgs e)
    {
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
                if (result.InconsistentBoxes.Count > 0) msg += $"[이송장번호 불일치/특정불가]\n{string.Join("\n", result.InconsistentBoxes)}";
                MessageBox.Show(msg, "운송장 불러오기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RunQuery();
            _statusLabel.Text = $"운송장번호 {result.AppliedCount}건을 적용했습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>상세보기에서 "이송장번호" 셀을 직접 고쳤을 때 박스에 반영한다(§운송장번호 수동입력).
    /// 이송장번호는 박스 단위 필드라 같은 박스의 다른 CSKU 라인에도 반영되도록 조회를 다시 돈다.</summary>
    private void OnTrackingNoCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_viewModeCombo.SelectedIndex != ViewModeDetail) return;
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "TrackingNo") return;

        var row = _grid.Rows[e.RowIndex];
        var fboNo = row.Cells["FboNo"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(fboNo) || !int.TryParse(row.Cells["BoxSeq"].Value?.ToString(), out var boxSeq))
            return;

        var newValue = row.Cells["TrackingNo"].Value?.ToString()?.Trim();
        var original = _rows.FirstOrDefault(r => r.FboNo == fboNo && r.BoxSeq == boxSeq)?.TrackingNo;
        if (string.Equals(string.IsNullOrEmpty(original) ? null : original, string.IsNullOrEmpty(newValue) ? null : newValue, StringComparison.Ordinal))
            return;

        _orderRepository.SetTrackingNo(fboNo, boxSeq, newValue);
        // CellEndEdit 처리 중 곧바로 그리드를 다시 그리면 편집 종료 마무리와 충돌할 수 있어 한 틱 미룬다.
        BeginInvoke(() =>
        {
            RunQuery();
            _statusLabel.Text = string.IsNullOrEmpty(newValue)
                ? $"'{fboNo}' 박스{boxSeq}의 이송장번호를 지웠습니다."
                : $"'{fboNo}' 박스{boxSeq}의 이송장번호를 '{newValue}'(으)로 입력했습니다.";
        });
    }

    private void RunQuery()
    {
        var channelId = _channelCombo.SelectedValue as string;
        _rows = _orderRepository.GetHistory(string.IsNullOrEmpty(channelId) ? null : channelId, _fromDatePicker.Value.Date, _toDatePicker.Value.Date);
        Render();
    }

    private void Render()
    {
        var channelNames = _channels.ToDictionary(c => c.ChannelId, c => c.ChannelName);
        var search = _searchBox.Text.Trim();

        var filtered = _rows.Where(r =>
            search.Length == 0 ||
            r.Csku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            r.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            r.FboNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
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
                    BoxCount = g.Select(r => (r.FboNo, r.BoxSeq)).Distinct().Count(),
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
            _grid.Columns.Add("FboNo", "발주번호");
            _grid.Columns.Add("OrderDate", "발주일");
            _grid.Columns.Add("ChannelName", "채널");
            _grid.Columns.Add("BoxCount", "박스수");
            _grid.Columns.Add("TotalQty", "총수량");
            _grid.Columns.Add("TrackingStatus", "이송장 상태");

            var groups = filtered.GroupBy(r => r.FboNo)
                .Select(g =>
                {
                    var boxes = g.GroupBy(r => r.BoxSeq).Select(bg => bg.First()).ToList();
                    return new
                    {
                        FboNo = g.Key,
                        OrderDate = g.First().OrderDate,
                        ChannelId = g.First().ChannelId,
                        BoxCount = boxes.Count,
                        TotalQty = g.Sum(r => r.Qty),
                        TrackedCount = boxes.Count(b => !string.IsNullOrEmpty(b.TrackingNo)),
                    };
                })
                .OrderByDescending(g => g.OrderDate).ThenByDescending(g => g.FboNo);

            foreach (var g in groups)
            {
                var channelName = channelNames.TryGetValue(g.ChannelId, out var name) ? name : g.ChannelId;
                var trackingStatus = g.TrackedCount == 0 ? "미등록" : g.TrackedCount == g.BoxCount ? "전체등록" : $"{g.TrackedCount}/{g.BoxCount}건 등록";
                _grid.Rows.Add(g.FboNo, g.OrderDate.ToString("yyyy-MM-dd"), channelName, g.BoxCount, g.TotalQty, trackingStatus);
            }
        }
        else
        {
            _grid.Columns.Add("FboNo", "발주번호");
            _grid.Columns.Add("OrderDate", "발주일");
            _grid.Columns.Add("ChannelName", "채널");
            _grid.Columns.Add("BoxSeq", "박스");
            _grid.Columns.Add("ReceiverDisplayName", "반품부명");
            _grid.Columns.Add("Csku", "CSKU");
            _grid.Columns.Add("ItemName", "품목명");
            _grid.Columns.Add("Qty", "수량");
            _grid.Columns.Add("ExpiryDate", "유통기한");
            _grid.Columns.Add("TrackingNo", "이송장번호");
            _grid.Columns.Add("Status", "상태");
            // 라인 단위 수정/복사/삭제(§선택 라인 액션)가 이 CSKU 줄 하나를 정확히 지목하는 데
            // 쓴다(FboBoxItem PK 구성요소) — 화면엔 안 보여줘도 됨.
            _grid.Columns.Add("ItemSeq", "ItemSeq");
            _grid.Columns["ItemSeq"].Visible = false;

            foreach (var r in filtered)
            {
                var channelName = channelNames.TryGetValue(r.ChannelId, out var name) ? name : r.ChannelId;
                _grid.Rows.Add(r.FboNo, r.OrderDate.ToString("yyyy-MM-dd"), channelName, r.BoxSeq,
                    r.ReceiverDisplayName, r.Csku, r.ItemName, r.Qty, r.ExpiryDate, r.TrackingNo, r.Status, r.ItemSeq);
            }
        }

        // 상세보기에서만 "이송장번호" 열을 직접 입력할 수 있게 열고, 나머지 열/뷰는 그대로 읽기전용
        // 유지한다(§운송장번호 수동입력).
        var isDetailView = _viewModeCombo.SelectedIndex == ViewModeDetail;
        _grid.ReadOnly = !isDetailView;
        if (isDetailView)
        {
            foreach (DataGridViewColumn column in _grid.Columns)
                column.ReadOnly = column.Name != "TrackingNo";
        }

        _statusLabel.Text = $"{filtered.Count}건 조회됨 (원본 {_rows.Count}건 중)";
    }
}
