using System.ComponentModel;
using System.Data;
using MiniERP2.Controls;
using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.5절 'OFS(발주처리)' 창
/// </summary>
public class OfsForm : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly CourierRepository _courierRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly OutboundRepository _outboundRepository = new();
    private readonly CourierExporter _courierExporter = new();
    private readonly OrderLoader _orderLoader = new();
    private readonly ItemRepository _itemRepository = new();

    private ExcelLikeDataGridView _ordersGrid = new();
    private DataGridView _previewGrid = new();
    private StatusStrip _statusStrip = new();
    private ToolStripStatusLabel _statusLabel = new();
    private BindingList<OfsOrderItem> _orders = new();
    private string? _lastChannelCode;
    private MappingForm? _subscribedMappingForm;

    // 택배사 출력 미리보기 — 실제 택배사 양식의 헤더 그대로 보여달라는 요청으로, 고정된 컬럼이
    // 아니라 선택된 택배사(_previewCourierCombo)의 HeaderMappingJson을 기준으로 매번 다시 그린다.
    private ComboBox _previewCourierCombo = new();
    private List<ShipmentPreviewRow> _previewRowModels = new();
    private List<HeaderMappingEntry> _previewHeaderEntries = new();
    // RefreshExportPreview()가 DataTable을 새로 채우는 동안에도 CellValueChanged가 발생하는데,
    // 그건 사용자 편집이 아니라 화면을 갱신하는 중이라 모델에 다시 쓰면 안 된다(무한 루프/오염 방지).
    private bool _isRefreshingPreview;
    private const string LastPreviewCourierSettingKey = "OfsPreviewCourier";

    // 택배사 출력 미리보기에서 직접 편집/합포장/분리배송/복사한 내용을 최근 5건까지 실행취소할 수
    // 있게 보관한다. 각 항목은 그 조작 직전의 _orders 전체 스냅샷(복제본)이다.
    private readonly List<List<OfsOrderItem>> _previewUndoStack = new();
    private const int MaxPreviewUndoSteps = 5;

    public OfsForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "OFS (발주처리)";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        // 1. Toolbar
        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnLoadOrders = new Button { Text = "발주 파일 로드", Size = new Size(120, 30) };
        var btnAddManualOrder = new Button { Text = "수동 주문 추가", Size = new Size(120, 30) };
        var btnMappingAssistant = new Button { Text = "매핑 도우미", Size = new Size(100, 30) };
        var btnUnmappedBatch = new Button { Text = "미매핑 일괄 처리", Size = new Size(130, 30) };
        var btnSave = new Button { Text = "저장 (발주확정)", Size = new Size(130, 30) };
        var btnExport = new Button { Text = "택배사 양식으로 내보내기", Size = new Size(180, 30) };
        var btnOutboundHistory = new Button { Text = "발주/출고 이력", Size = new Size(120, 30) };

        btnLoadOrders.Click += OnLoadOrdersClick;
        btnExport.Click += OnExportClick;
        btnSave.Click += OnSaveClick;
        btnAddManualOrder.Click += OnAddManualOrderClick;
        btnMappingAssistant.Click += OnMappingAssistantClick;
        btnUnmappedBatch.Click += OnUnmappedBatchClick;
        btnOutboundHistory.Click += (s, e) => FormManager.Show<OutboundHistoryForm>();

        toolStrip.Controls.Add(btnLoadOrders);
        toolStrip.Controls.Add(btnAddManualOrder);
        toolStrip.Controls.Add(btnMappingAssistant);
        toolStrip.Controls.Add(btnUnmappedBatch);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnOutboundHistory);
        toolStrip.Controls.Add(btnExport);

        // 2. Data Grid
        _ordersGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "OfsForm.OrdersGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true
        };

        // Add columns based on standard order processing fields
        _ordersGrid.Columns.AddRange(
            // Raw/Standardized Data
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", Name = "OrderNo", DataPropertyName = "OrderNo", Width = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "상품명", Name = "ProductName", DataPropertyName = "ProductName", Width = 250 },
            new DataGridViewTextBoxColumn { HeaderText = "옵션명", Name = "OptionName", DataPropertyName = "OptionName", Width = 200 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Quantity", DataPropertyName = "Quantity", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "수취인", Name = "Recipient", DataPropertyName = "Recipient", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "연락처", Name = "Phone", DataPropertyName = "Phone", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "주소", Name = "Address", DataPropertyName = "Address", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            // Mapped/Transformed Data
            new DataGridViewTextBoxColumn { HeaderText = "매핑된 SKU", Name = "MappedSku", DataPropertyName = "MappedSku", Width = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "처리 상태", Name = "Status", DataPropertyName = "Status", Width = 100, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", Width = 150 },
            // 실제 데이터(ShipmentGroupId)는 보통 비어있고(주문번호 단위가 기본값), 화면에는 몇 줄이
            // 묶여있는지만 보여주면 되므로 DataPropertyName 없이 OnOrdersGridCellFormatting에서 채운다.
            new DataGridViewTextBoxColumn { HeaderText = "묶음", Name = "ShipmentGroup", Width = 90, ReadOnly = true }
        );

        // 데이터 바인딩
        _ordersGrid.DataSource = _orders;

        // 상태에 따른 행 색상 변경을 위한 이벤트 핸들러 등록
        _ordersGrid.RowPrePaint += OnOrdersGridRowPrePaint;

        // 셀 값 변경 시 연관 데이터 자동 업데이트를 위한 이벤트 핸들러 등록
        _ordersGrid.CellValueChanged += OnOrdersGridCellValueChanged;

        // "매핑된 SKU" 셀 자동완성 + 유효성 검사
        _ordersGrid.EditingControlShowing += OnOrdersGridEditingControlShowing;
        _ordersGrid.CellValidating += OnOrdersGridCellValidating;

        // "묶음" 열 표시 + 분리배송/합포장 컨텍스트 메뉴
        _ordersGrid.CellFormatting += OnOrdersGridCellFormatting;
        SetupShipmentGroupingContextMenu();

        // 2.5. 위(상세 줄)/아래(택배사 출력 미리보기) 분할
        // 기본값은 상세 목록과 미리보기가 비슷한 비중으로 보이게 250으로 둔다(처음 실행 시에만
        // 적용되고, 한 번 조절하면 PersistentSplitContainer가 그 값을 기억해 다음에도 유지한다).
        var gridSplit = new PersistentSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 250, PersistenceKey = "OfsForm.GridSplit" };
        gridSplit.Panel1.Controls.Add(_ordersGrid);
        gridSplit.Panel2.Controls.Add(CreateExportPreviewPanel());

        // 3. Status Bar
        _statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusLabel = new ToolStripStatusLabel("준비");
        _statusStrip.Items.Add(_statusLabel);

        // Add controls to layout
        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(gridSplit, 0, 1);
        mainLayout.Controls.Add(_statusStrip, 0, 2);

        Controls.Add(mainLayout);

        FormClosing += (s, e) => _ordersGrid.SaveLayout();
    }

    private async void OnLoadOrdersClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv|All files (*.*)|*.*",
            Multiselect = true,
            Title = "발주 파일을 선택하세요",
            // 기획서 2.4절: 기능별 마지막 폴더 위치 기억
            InitialDirectory = _settingsService.GetLastFolder("OfsLoadOrders") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        // 파일을 읽기 전에 어떤 채널의 설정으로 읽을지 사용자에게 묻습니다.
        using var channelDialog = new SelectChannelDialog();
        if (channelDialog.ShowDialog(this) != DialogResult.OK || channelDialog.SelectedChannel == null)
        {
            _statusLabel.Text = "채널이 선택되지 않아 작업을 취소했습니다.";
            return;
        }

        var channelConfig = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelDialog.SelectedChannel.ChannelCode);
        if (channelConfig == null)
        {
            GuideToChannelConfig(channelDialog.SelectedChannel);
            return;
        }

        // 선택된 채널의 매핑 규칙으로 SkuMapper를 생성합니다.
        var skuMapper = new SkuMapper(_mappingRepository, channelConfig.ChannelCode, _channelSkuRepository);
        _lastChannelCode = channelConfig.ChannelCode;

        // UI를 대기 상태로 변경
        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = $"'{channelDialog.SelectedChannel.ChannelName}' 채널의 설정으로 발주 파일을 읽는 중입니다...";

        try
        {
            // 다음을 위해 선택된 파일의 폴더 위치를 저장합니다.
            if (ofd.FileNames.Length > 0)
            {
                _settingsService.SetLastFolder("OfsLoadOrders", Path.GetDirectoryName(ofd.FileNames[0])!);
            }

            // 기존 주문을 지울지 물어봅니다.
            if (_orders.Count > 0)
            {
                var result = MessageBox.Show("기존에 로드된 주문이 있습니다. 새로 불러오시겠습니까?\n'아니오'를 누르면 기존 목록에 추가됩니다.", "확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Yes) _orders.Clear();
                else if (result == DialogResult.Cancel) return;
            }

            var allLoadedItems = new List<OfsOrderItem>();
            foreach (var file in ofd.FileNames)
            {
                var loadedItems = await LoadOrderFileWithPasswordRetryAsync(skuMapper, channelConfig, file);
                if (loadedItems == null) continue; // 사용자가 비밀번호 입력을 취소함

                if (_orderLoader.LastLoadHeaderRowLooksEmpty)
                {
                    MessageBox.Show(
                        $"'{Path.GetFileName(file)}' 파일에서 채널설정에 지정된 헤더 행(헤더 행: {channelConfig.OrderFieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column))?.HeaderRow})의 헤더를 하나도 찾지 못했습니다.\n" +
                        "헤더 행이 비어있거나 셀이 병합되어 있을 수 있습니다. 채널설정에서 헤더 행 번호를 확인해주세요.\n\n확인을 누르면 일단 파일은 그대로 불러옵니다.",
                        "헤더 행 확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                allLoadedItems.AddRange(loadedItems);
            }

            // 누적발주서 채널(과거 이력까지 누적해서 담긴 발주서 파일)이면, 매번 전체를 그대로
            // 처리 대상으로 삼지 않고 발주일 기준 최근 5일 이내 항목만 골라 사용자가 직접 선택한
            // 건만 추가한다. "발주일" 헤더가 매핑되어 있지 않으면(채널설정에서 미설정) 이 옵션은
            // 동작하지 않고 평소처럼 전체를 그대로 추가한다.
            if (channelConfig.IsCumulativeOrderFile && channelConfig.OrderFieldMappings.ContainsKey(StdField.OrderDate))
            {
                int windowDays = channelConfig.CumulativeOrderWindowDays;
                var cutoff = DateTime.Today.AddDays(-windowDays);
                var recentItems = allLoadedItems
                    .Where(o => o.OrderDate is null || (o.OrderDate.Value.Date >= cutoff && o.OrderDate.Value.Date <= DateTime.Today))
                    .ToList();

                if (recentItems.Count == 0)
                {
                    MessageBox.Show($"발주일 기준 최근 {windowDays}일 이내({cutoff:M월 d일}~) 항목이 없습니다.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using var picker = new CumulativeOrderSelectionDialog(recentItems, windowDays);
                if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedItems.Count == 0)
                {
                    _statusLabel.Text = "선택한 항목이 없어 추가되지 않았습니다.";
                    return;
                }

                allLoadedItems = picker.SelectedItems;
            }

            foreach (var item in allLoadedItems) _orders.Add(item);

            _statusLabel.Text = $"총 {_orders.Count}개의 주문이 로드되었습니다.";
            RefreshExportPreview();

            WarnIfOrdersAlreadyHaveHistory(allLoadedItems);

            var unmappedCount = allLoadedItems.Count(o => o.Status == "매핑 실패" || o.Status == "매핑 키 없음");
            if (unmappedCount > 0 && EnsureMasterDbNotEmpty())
            {
                MessageBox.Show($"미매핑건 {unmappedCount}건 있음. 매핑창이 열립니다.", "미매핑 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);

                var mappingForm = Application.OpenForms.OfType<MappingForm>().FirstOrDefault() ?? new MappingForm();
                if (!mappingForm.Visible) mappingForm.Show();
                mappingForm.BringToFront();
                mappingForm.ShowUnmappedItems(channelConfig.ChannelCode, _orders, () => { _ordersGrid.Invalidate(); RefreshExportPreview(); });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n\n{ex.Message}", "로드 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "오류 발생";
        }
        finally
        {
            // UI 상태 복원
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// 같은 발주서 파일을 실수로 다시 불러왔거나, 처리 이력이 꼬여 있을 가능성을 안내한다. 다만
    /// 동일한 곳으로 두 번 출고하는 경우도 있을 수 있으므로 처리 자체를 막지는 않고(발주 프로세스는
    /// 그대로 진행), 이미 발주확정/출고확정 이력이 있는 주문번호가 있으면 안내창만 띄운다. "동일
    /// 주문"의 판단 기준은 OutboundDetailTable의 충돌 판단 키와 같은 OrderNo다(채널 무관).
    /// </summary>
    private void WarnIfOrdersAlreadyHaveHistory(List<OfsOrderItem> loadedItems)
    {
        var orderNos = loadedItems.Select(o => o.OrderNo).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().ToList();
        if (orderNos.Count == 0) return;

        var existing = _outboundRepository.FindByOrderNos(orderNos!);
        if (existing.Count == 0) return;

        var distinctOrderCount = existing.Select(d => d.OrderNo).Distinct().Count();
        var byStatus = existing
            .GroupBy(d => d.Status)
            .Select(g => $"{g.Key} {g.Count()}건")
            .ToList();

        var earliest = existing.Min(d => d.CreatedAt);
        var latest = existing.Max(d => d.CreatedAt);
        var whenText = earliest.Date == latest.Date
            ? $"{earliest:M월 d일 H시}경"
            : $"{earliest:M월 d일} ~ {latest:M월 d일}";

        MessageBox.Show(
            $"이번에 불러온 주문 중 {distinctOrderCount}건의 주문번호가 {whenText} 발주건과 동일한 발주확정/출고확정 이력이 이미 있습니다.\n" +
            $"({string.Join(", ", byStatus)})\n\n" +
            "동일한 곳으로 두 번 출고하는 경우일 수 있어 발주 처리는 그대로 진행됩니다. 중복 처리가 아닌지 한 번 확인해주세요.",
            "동일 주문번호 이력 발견", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// 파일이 암호로 보호되어 있으면 비밀번호를 물어보고 재시도합니다. 사용자가 취소하면 null을 반환합니다.
    /// </summary>
    private async Task<List<OfsOrderItem>?> LoadOrderFileWithPasswordRetryAsync(SkuMapper skuMapper, ChannelConfig channelConfig, string file)
    {
        try
        {
            return await _orderLoader.LoadFromFileAsync(skuMapper, channelConfig, file);
        }
        catch (EncryptedExcelFileException)
        {
            using var dialog = new PasswordPromptDialog(Path.GetFileName(file));
            if (dialog.ShowDialog(this) != DialogResult.OK) return null;

            return await _orderLoader.LoadFromFileAsync(skuMapper, channelConfig, file, dialog.Password);
        }
    }

    /// <summary>
    /// 선택한 채널에 ChannelConfig가 없을 때 안내 후 채널 설정 창을 열어 해당 채널을 바로 보여줍니다.
    /// </summary>
    private void GuideToChannelConfig(SalesChannel channel)
    {
        MessageBox.Show(
            $"'{channel.ChannelName}' 채널의 설정이 없습니다.\n채널 설정 창에서 발주서를 읽는 방법을 먼저 설정해주세요.",
            "채널 설정 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        var configForm = Application.OpenForms.OfType<ChannelConfigForm>().FirstOrDefault() ?? new ChannelConfigForm();
        if (!configForm.Visible) configForm.Show();
        configForm.BringToFront();
        configForm.SelectChannelByCode(channel.ChannelCode);
    }

    /// <summary>
    /// 마스터DB(마스터SKU)가 비어있으면 매핑 작업이 무의미하므로 안내 후 마스터SKU 관리창을 열어준다.
    /// </summary>
    private bool EnsureMasterDbNotEmpty()
    {
        if (_itemRepository.GetAll().Count > 0) return true;

        MessageBox.Show(
            "마스터DB(마스터SKU)가 비어있어 매핑할 수 없습니다.\n마스터SKU 관리창에서 먼저 품목을 등록해주세요.",
            "마스터DB 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        var masterSkuForm = Application.OpenForms.OfType<MasterSkuForm>().FirstOrDefault() ?? new MasterSkuForm();
        if (!masterSkuForm.Visible) masterSkuForm.Show();
        masterSkuForm.BringToFront();
        return false;
    }

    private void OnMappingAssistantClick(object? sender, EventArgs e)
    {
        if (_ordersGrid.CurrentRow?.DataBoundItem is not OfsOrderItem item)
        {
            MessageBox.Show("매핑할 주문 행을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!EnsureMasterDbNotEmpty()) return;

        using var dialog = new OrderSkuMappingDialog(item, item.ChannelCode);
        FormManager.ApplyBoundsTracking(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        item.MappedSku = dialog.ResultMappedSku;
        item.Status = dialog.ResultStatus;
        _ordersGrid.Invalidate();
        RefreshExportPreview();
    }

    /// <summary>
    /// 매핑관리창의 "미매핑 처리" 탭을 열어, 로드된 발주서의 미매핑건을 한 화면에서
    /// 검토하면서 매핑할 수 있게 합니다. 자동 안내 팝업을 닫은 뒤에도 언제든 다시 열 수 있도록
    /// 별도 버튼으로 제공합니다.
    /// </summary>
    private void OnUnmappedBatchClick(object? sender, EventArgs e)
    {
        var channelCode = _lastChannelCode ?? _orders.FirstOrDefault(o => !string.IsNullOrEmpty(o.ChannelCode))?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("먼저 발주 파일을 로드하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!EnsureMasterDbNotEmpty()) return;

        var mappingForm = Application.OpenForms.OfType<MappingForm>().FirstOrDefault() ?? new MappingForm();
        if (!mappingForm.Visible) mappingForm.Show();
        mappingForm.BringToFront();
        mappingForm.ShowUnmappedItems(channelCode, _orders, () => { _ordersGrid.Invalidate(); RefreshExportPreview(); });
    }

    private async void OnExportClick(object? sender, EventArgs e)
    {
        // 그리드에서 줄을 선택해둔 상태면 선택한 건만, 아무것도 선택하지 않았으면 매핑된 전체를 내보낸다.
        var selected = GetSelectedOrderItems();
        var isPartialSelection = selected.Count > 0;
        var ordersToExport = (isPartialSelection ? selected : (IEnumerable<OfsOrderItem>)_orders)
            .Where(o => !string.IsNullOrWhiteSpace(o.MappedSku))
            .ToList();

        if (ordersToExport.Count == 0)
        {
            var message = isPartialSelection
                ? "선택한 줄 중 매핑 성공된 주문이 없습니다."
                : "내보낼 (매핑 성공된) 주문이 없습니다.";
            MessageBox.Show(message, "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 1. 사용자에게 택배사 선택 요청
        using var courierDialog = new SelectCourierDialog();
        if (courierDialog.ShowDialog(this) != DialogResult.OK || courierDialog.SelectedCourier == null)
        {
            _statusLabel.Text = "택배사가 선택되지 않아 내보내기를 취소했습니다.";
            return;
        }

        var courier = courierDialog.SelectedCourier;

        // 2. 사용자에게 저장 위치 요청
        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"{courier.CourierName}_출고_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("OfsExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("OfsExport", Path.GetDirectoryName(filePath)!);

        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = $"'{courier.CourierName}' 양식으로 내보내는 중...";

        try
        {
            var channelConfigsByCode = _channelConfigService.Load().ToDictionary(c => c.ChannelCode);
            var overflowGroups = await _courierExporter.ExportAsync(ordersToExport, courier, filePath, channelConfigsByCode);
            var scopeLabel = isPartialSelection ? "선택한" : "매핑된 전체";
            _statusLabel.Text = $"{scopeLabel} {ordersToExport.Count}건을 '{courier.CourierName}' 양식으로 내보냈습니다.";

            if (overflowGroups.Count > 0)
            {
                MessageBox.Show(
                    $"다음 묶음은 품목이 4줄을 초과해 송장에 다 표시되지 못할 수 있습니다:\n{string.Join(", ", overflowGroups)}\n\n" +
                    "그리드에서 일부 줄을 합쳐 4줄 이하로 줄여주세요. (내보내기는 그대로 완료되었습니다.)",
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

    private async void OnSaveClick(object? sender, EventArgs e)
    {
        var ordersToSave = _orders.Where(o => !string.IsNullOrWhiteSpace(o.MappedSku)).ToList();

        if (ordersToSave.Count == 0)
        {
            MessageBox.Show("저장할 (매핑 성공된) 주문이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show($"{ordersToSave.Count}개의 주문을 발주확정하고 저장하시겠습니까?\n(운송장번호가 아직 없는 건은 '발주확정' 상태로 저장되고, 운송장번호 등록 시 '출고확정'으로 바뀝니다.)", "저장 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = "주문 내역을 저장하는 중...";

        try
        {
            var outboundDetails = new List<OutboundDetail>();
            var failedOrders = new List<OfsOrderItem>();

            await Task.Run(() =>
            {
                foreach (var order in ordersToSave)
                {
                    if (string.IsNullOrEmpty(order.ChannelCode) || string.IsNullOrEmpty(order.MappedSku)) continue;

                    var csku = _channelSkuRepository.GetByChannelAndCskuCode(order.ChannelCode, order.MappedSku);

                    // CSKU 미등록이어도 발주이력에는 기록한다(납품가=0, 나중에 등록 후 수정 가능).
                    // 이력에서 제외하면 마감 대조 시 실제 발송 건이 누락돼 대조가 불가능하다.
                    outboundDetails.Add(new OutboundDetail
                    {
                        ChannelCode = order.ChannelCode ?? string.Empty,
                        OrderNo = order.OrderNo ?? string.Empty,
                        ShipmentGroupKey = ShipmentGrouping.GetEffectiveGroupId(order),
                        TrackingNo = order.TrackingNo ?? string.Empty,
                        MskuCode = order.MappedSku,
                        Qty = order.Quantity,
                        SupplyPrice = csku?.SupplyPrice ?? 0m,
                        // 운송장 결과를 나중에 수령인 기준으로 매칭하려면 이 시점의 수령인/주소/
                        // 품목명을 함께 남겨둬야 한다(발주/출고 이력 관리창에서 사용).
                        Recipient = order.Recipient ?? string.Empty,
                        Address = order.Address ?? string.Empty,
                        ProductName = order.ProductName ?? string.Empty,
                    });

                    if (csku == null) failedOrders.Add(order); // 납품가 없음 표시는 그리드에 유지
                }

                if (outboundDetails.Any())
                {
                    _outboundRepository.SaveOutbound(outboundDetails);
                }
            });

            // 저장 성공/실패에 따라 UI 업데이트
            UpdateOrderStatusAfterSave(outboundDetails, failedOrders);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "저장 오류 발생";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void UpdateOrderStatusAfterSave(List<OutboundDetail> savedDetails, List<OfsOrderItem> failedOrders)
    {
        // 성공한 주문들의 상태를 변경한다. 실무에서 출고확정은 운송장번호가 있어야 성립하므로,
        // 운송장번호가 아직 없으면 '발주확정', 있으면 '출고확정'(OutboundRepository.SaveOutbound와
        // 같은 기준). 나중에 운송장번호를 업로드하면(마감 대조 탭) 그 이력의 상태가 출고확정으로
        // 바뀐다 — 단, 이 화면(OFS)에 그 발주서가 더 이상 열려 있지 않을 수 있으므로 그건
        // OutboundDetail 쪽 기록에만 반영되고 여기 그리드까지 되돌아오지는 않는다.
        var savedOrderNos = new HashSet<string>(savedDetails.Select(d => d.OrderNo));
        var savedOrdersInGrid = _orders.Where(o => o.OrderNo != null && savedOrderNos.Contains(o.OrderNo)).ToList();
        foreach (var order in savedOrdersInGrid)
        {
            order.Status = string.IsNullOrWhiteSpace(order.TrackingNo) ? "발주확정" : "출고확정";
        }

        // 실패한 주문들의 상태를 '납품가 없음'으로 변경
        foreach (var order in failedOrders)
        {
            order.Status = "납품가 없음";
        }

        // 그리드 새로고침
        _ordersGrid.Invalidate();

        var successCount = savedDetails.Count;
        var failCount = failedOrders.Count;
        _statusLabel.Text = $"저장 완료: {successCount}건 성공, {failCount}건 실패 (납품가 없음)";
    }

    private void OnOrdersGridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (_ordersGrid.CurrentCell?.OwningColumn?.Name != "MappedSku" || e.Control is not TextBox textBox) return;

        var channelCode = (_ordersGrid.CurrentRow?.DataBoundItem as OfsOrderItem)?.ChannelCode ?? _lastChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;

        var cskuCodes = _channelSkuRepository.GetAllByChannel(channelCode).Select(c => c.CskuCode).ToArray();
        var source = new AutoCompleteStringCollection();
        source.AddRange(cskuCodes);

        textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        textBox.AutoCompleteCustomSource = source;
    }

    private void OnOrdersGridCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_ordersGrid.Columns[e.ColumnIndex].Name != "MappedSku") return;

        var text = e.FormattedValue?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        var channelCode = (_ordersGrid.Rows[e.RowIndex].DataBoundItem as OfsOrderItem)?.ChannelCode ?? _lastChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;

        var validCodes = _channelSkuRepository.GetAllByChannel(channelCode)
            .Select(c => c.CskuCode)
            .ToList();
        if (!validCodes.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            _statusLabel.Text = $"'{text}'는 등록된 CSKU 코드가 아닙니다 — 자동완성 목록에서 선택하거나 먼저 데이터 관리에서 CSKU를 등록하세요.";
            e.Cancel = true;
        }
    }

    private void OnOrdersGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        // 데이터 행이 아니면 무시
        if (e.RowIndex < 0 || e.RowIndex >= _ordersGrid.Rows.Count || _ordersGrid.Rows[e.RowIndex].IsNewRow)
        {
            return;
        }

        var row = _ordersGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not OfsOrderItem item || item.Status == null)
        {
            return;
        }

        // 매핑 상태에 따라 배경색 설정. 시스템 다크모드에서는 컨트롤 기본 글자색이 흰색으로
        // 바뀔 수 있으므로, 배경색을 칠할 때는 항상 검은 글자색을 함께 지정해 다크/라이트
        // 모드 어느 쪽에서도 잘 보이도록 한다.
        if (item.Status == "매핑 실패" || item.Status == "매핑 키 없음" || item.Status == "납품가 없음")
        {
            row.DefaultCellStyle.BackColor = Color.MistyRose;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else if (item.Status.StartsWith("매핑(") || item.Status == "발주확정" || item.Status == "출고확정")
        {
            row.DefaultCellStyle.BackColor = Color.Honeydew;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else
        {
            // 다른 상태는 기본 배경색/글자색으로 되돌립니다 (가상화 시 중요).
            row.DefaultCellStyle.BackColor = _ordersGrid.DefaultCellStyle.BackColor;
            row.DefaultCellStyle.ForeColor = _ordersGrid.DefaultCellStyle.ForeColor;
        }
    }

    private void OnAddManualOrderClick(object? sender, EventArgs e)
    {
        // 새 주문 항목을 생성하고 그리드에 추가합니다.
        var newItem = new OfsOrderItem { Status = "수동 추가" };
        _orders.Add(newItem);

        // 새로 추가된 행으로 스크롤하고 선택합니다.
        _ordersGrid.ClearSelection();
        int newRowIndex = _ordersGrid.Rows.GetLastRow(DataGridViewElementStates.None);
        if (newRowIndex >= 0)
        {
            _ordersGrid.Rows[newRowIndex].Selected = true;
            _ordersGrid.FirstDisplayedScrollingRowIndex = newRowIndex;

            // 편집을 시작할 첫 번째 보이는 셀을 찾습니다.
            var firstVisibleCell = _ordersGrid.Rows[newRowIndex].Cells
                .Cast<DataGridViewCell>()
                .FirstOrDefault(c => c.Visible && !c.ReadOnly);

            if (firstVisibleCell != null)
            {
                _ordersGrid.CurrentCell = firstVisibleCell;
                _ordersGrid.BeginEdit(true);
            }
        }
    }

    private void OnOrdersGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        // 헤더 행이거나 유효하지 않은 인덱스인 경우 무시
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var changedColumnName = _ordersGrid.Columns[e.ColumnIndex].Name;
        var row = _ordersGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not OfsOrderItem item) return;

        // '매핑된 SKU' 열이 수정되었을 때
        if (changedColumnName == "MappedSku")
        {
            if (!string.IsNullOrWhiteSpace(item.MappedSku))
            {
                item.Status = "수동 매핑";

                // 그리드에서 직접 입력한 CSKU 코드를 1:1 매핑 규칙으로 영구 저장한다.
                // 마감/이익분석 창의 "Msku" 셀 동작(OnSettlementGridCellEndEdit)과 동일한 원칙.
                var channelCode = item.ChannelCode ?? _lastChannelCode;
                var key = (item.ProductName ?? string.Empty) + (item.OptionName ?? string.Empty);
                if (!string.IsNullOrEmpty(channelCode) && !string.IsNullOrWhiteSpace(key))
                {
                    _mappingRepository.UpsertExactRule(channelCode, key, item.MappedSku.Trim());
                    _statusLabel.Text = $"'{item.ProductName} {item.OptionName}' → '{item.MappedSku}' 1:1 매핑 규칙을 저장했습니다.";
                }
            }
            else
            {
                item.Status = "매핑 실패";
            }
            _ordersGrid.InvalidateRow(e.RowIndex);
        }
        // '운송장번호' 열이 수정되면 같은 묶음(송장)의 다른 줄에도 같은 운송장번호를 복사한다
        // (실제로는 한 패키지에 운송장 1개이므로, 묶음 안의 모든 줄이 같은 운송장번호를 가져야 함).
        else if (changedColumnName == "TrackingNo")
        {
            var groupId = ShipmentGrouping.GetEffectiveGroupId(item);
            foreach (var sibling in _orders.Where(o => o != item && ShipmentGrouping.GetEffectiveGroupId(o) == groupId))
            {
                sibling.TrackingNo = item.TrackingNo;
            }
            _ordersGrid.Invalidate();
        }

        // 어떤 열이 바뀌었든(상품명/옵션명/수량 등 포함) 출력 미리보기가 최신 상태를 보여주게 한다.
        RefreshExportPreview();
    }

    /// <summary>
    /// "묶음" 열에 그 줄이 속한 묶음의 줄 수를 보여준다(1줄이면 빈칸, 2줄 이상이면 "N줄 묶음").
    /// 묶음 키는 화면에 노출하지 않고 그 묶음에 몇 줄이 모여있는지만 보여주면 충분하다.
    /// </summary>
    private void OnOrdersGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_ordersGrid.Columns[e.ColumnIndex].Name != "ShipmentGroup") return;
        if (e.RowIndex < 0 || e.RowIndex >= _ordersGrid.Rows.Count) return;
        if (_ordersGrid.Rows[e.RowIndex].DataBoundItem is not OfsOrderItem item) return;

        var groupId = ShipmentGrouping.GetEffectiveGroupId(item);
        var groupSize = _orders.Count(o => ShipmentGrouping.GetEffectiveGroupId(o) == groupId);

        e.Value = groupSize > 1 ? $"{groupSize}줄 묶음" : string.Empty;
        e.FormattingApplied = true;
    }

    /// <summary>
    /// 분리배송(한 주문을 여러 송장으로 나누기)/합포장(여러 줄을 한 송장으로 합치기)/묶음 해제를
    /// 그리드 우클릭 메뉴로 제공한다. 기존 ExcelLikeDataGridView의 복사/붙여넣기 메뉴는 그대로 두고
    /// 구분선 아래에 추가한다.
    /// </summary>
    private void SetupShipmentGroupingContextMenu()
    {
        var menu = _ordersGrid.ContextMenuStrip!;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("조건부 매핑 규칙 추가", null, OnAddConditionRuleFromOrderRowClick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("합포장으로 묶기", null, OnMergeIntoOneShipmentClick);
        menu.Items.Add("분리배송으로 분리", null, OnSplitIntoNewShipmentClick);
        menu.Items.Add("묶음 해제", null, OnResetShipmentGroupClick);
    }

    /// <summary>
    /// 선택한 발주 행의 상품명/옵션명을 초기 조건으로 채운 새 조건부 매핑 규칙을 매핑관리창에서 만든다.
    /// 규칙이 저장되면 발주 목록 전체를 자동 재매핑한다.
    /// </summary>
    private void OnAddConditionRuleFromOrderRowClick(object? sender, EventArgs e)
    {
        var order = GetSelectedOrderItems().FirstOrDefault();
        if (order == null) return;

        var channelCode = order.ChannelCode ?? _lastChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;

        BeginInvoke(() =>
        {
            var mappingForm = Application.OpenForms.OfType<MappingForm>().FirstOrDefault() ?? new MappingForm();
            if (!mappingForm.Visible) mappingForm.Show();
            mappingForm.BringToFront();

            if (!ReferenceEquals(_subscribedMappingForm, mappingForm))
            {
                if (_subscribedMappingForm != null)
                    _subscribedMappingForm.MappingRulesChanged -= ReapplyMappingForAllOrders;
                mappingForm.MappingRulesChanged += ReapplyMappingForAllOrders;
                mappingForm.FormClosed += (_, _) =>
                {
                    if (ReferenceEquals(_subscribedMappingForm, mappingForm))
                        _subscribedMappingForm = null;
                };
                _subscribedMappingForm = mappingForm;
            }

            var conditions = new List<(StdField Field, string Value)>();
            if (!string.IsNullOrWhiteSpace(order.ProductName))
                conditions.Add((StdField.ProductName, order.ProductName));
            if (!string.IsNullOrWhiteSpace(order.OptionName))
                conditions.Add((StdField.OptionName, order.OptionName));

            mappingForm.StartNewConditionRuleFor(channelCode, conditions);
            _statusLabel.Text = "매핑관리창에 새 조건부 매핑 규칙을 만들었습니다. 조건/대상 SKU를 완성하고 저장하면 이 목록에 자동으로 반영됩니다.";
        });
    }

    /// <summary>
    /// 매핑관리창에서 규칙이 저장되면 현재 발주 목록 전체를 다시 매핑한다.
    /// 수동 매핑(UpsertExactRule로 저장된 1:1 규칙)은 재매핑 시에도 동일 결과가 나오므로 건드린다.
    /// </summary>
    private void ReapplyMappingForAllOrders()
    {
        if (_orders.Count == 0 || string.IsNullOrEmpty(_lastChannelCode)) return;
        var mapper = new SkuMapper(_mappingRepository, _lastChannelCode, _channelSkuRepository);
        foreach (var order in _orders)
            mapper.ApplyMapping(order);
        _ordersGrid.Refresh();
        _statusLabel.Text = "매핑규칙 변경을 반영해 발주목록을 다시 매핑했습니다.";
    }

    private List<OfsOrderItem> GetSelectedOrderItems()
    {
        return _ordersGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.DataBoundItem)
            .OfType<OfsOrderItem>()
            .ToList();
    }

    /// <summary>
    /// ContextMenuStrip의 Click 핸들러 안에서 DataGridView.DataSource를 다시 통째로 갈아끼우는
    /// 등 무거운 작업을 곧바로 하면, 메뉴가 완전히 닫히기 전(Win32 메시지 처리가 덜 끝난 상태)에
    /// 그 작업이 겹쳐 마우스 커서가 멈춘 것처럼 보이는 경쟁 상태가 이 환경에서 재현됐다(분리배송
    /// 처리 클릭 시 신고). 다음 메시지 루프 틱으로 미뤄 메뉴가 완전히 닫힌 뒤에 실제 작업을
    /// 실행한다.
    /// </summary>
    private void OnMergeIntoOneShipmentClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedOrderItems();
        BeginInvoke(() =>
        {
            if (selected.Count < 2)
            {
                MessageBox.Show("합포장으로 묶을 줄을 2개 이상 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (selected.Select(o => o.Recipient).Distinct().Count() > 1)
            {
                var confirm = MessageBox.Show(
                    "선택한 줄들의 수취인이 서로 다릅니다. 그래도 한 송장으로 합포장하시겠습니까?",
                    "수취인 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            ClearStaleInvoiceLabelOverrides(selected);
            var groupId = ShipmentGrouping.GetEffectiveGroupId(selected[0]);
            foreach (var item in selected)
            {
                item.ShipmentGroupId = groupId;
            }
            _ordersGrid.Invalidate();
            RefreshExportPreview();
        });
    }

    private void OnSplitIntoNewShipmentClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedOrderItems();
        BeginInvoke(() =>
        {
            if (selected.Count == 0)
            {
                MessageBox.Show("분리배송으로 분리할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ClearStaleInvoiceLabelOverrides(selected);
            var baseId = ShipmentGrouping.GetEffectiveGroupId(selected[0]);
            var newGroupId = $"{baseId}-분리{Guid.NewGuid().ToString("N")[..6]}";
            foreach (var item in selected)
            {
                item.ShipmentGroupId = newGroupId;
            }
            _ordersGrid.Invalidate();
            RefreshExportPreview();
        });
    }

    private void OnResetShipmentGroupClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedOrderItems();
        BeginInvoke(() =>
        {
            if (selected.Count == 0)
            {
                MessageBox.Show("묶음을 해제할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ClearStaleInvoiceLabelOverrides(selected);
            foreach (var item in selected)
            {
                item.ShipmentGroupId = null;
            }
            _ordersGrid.Invalidate();
            RefreshExportPreview();
        });
    }

    /// <summary>
    /// "품목(실제 출력될 내용)" 칸을 직접 수정하면 그 묶음의 줄들에 InvoiceLabel을 덮어써 둔다
    /// (맨 위 줄엔 입력한 텍스트, 나머지 줄엔 빈 문자열 — BuildCombinedItemDescription이 빈 줄은
    /// 걸러내는 방식으로 합쳐진 결과를 그대로 유지하기 위함, <see cref="ShipmentPreviewRow.
    /// ItemsDescription"/> 참고). 그런데 이 묶음을 나중에 합포장/분리배송/묶음해제로 다시 묶으면,
    /// 새 구성에 맞지 않는 옛 오버라이드가 그대로 남아 "분리배송 처리 후 그 줄의 품목 내용이
    /// 비어 보임/엉뚱하게 보임" 버그가 있었다(예: 빈 InvoiceLabel("")을 가진 줄이 분리되어 혼자가
    /// 돼도, null이 아니라 빈 문자열이라 자동 라벨로 안 돌아가고 계속 빈 줄로 표시됨). 그룹 구성을
    /// 바꾸는 작업 직전에, 영향받는 묶음(들 — 옮겨가는 줄과 그 줄이 원래/새로 속하게 될 묶음의
    /// 다른 줄까지 전부)의 InvoiceLabel을 지워서 새 구성 기준으로 다시 계산되게 한다.
    /// </summary>
    private void ClearStaleInvoiceLabelOverrides(IEnumerable<OfsOrderItem> itemsAboutToRegroup)
    {
        var affectedGroupIds = itemsAboutToRegroup.Select(ShipmentGrouping.GetEffectiveGroupId).ToHashSet();
        foreach (var item in _orders.Where(o => affectedGroupIds.Contains(ShipmentGrouping.GetEffectiveGroupId(o))))
        {
            item.InvoiceLabel = null;
        }
    }

    // ===================== 택배사 출력 미리보기 =====================

    /// <summary>
    /// 묶음(=송장) 단위로 묶인 미리보기 한 줄을 나타낸다. 실제 택배사 출력 시 CourierExporter가
    /// 만드는 결과와 같은 조합 규칙(<see cref="ShipmentGrouping.BuildCombinedItemDescription"/>)을
    /// 써서, 내보내기 전에 미리 어떤 모습으로 나갈지 확인할 수 있게 한다. 배송메세지/운송장번호는
    /// 여기서 수정하면 묶음에 속한 모든 원본 줄에 그대로 반영된다.
    /// </summary>
    /// <summary>
    /// 한 묶음(=송장 1건)에 속한 원본 줄들. 미리보기 그리드는 이제 고정된 속성이 아니라 선택된
    /// 택배사의 헤더 매핑을 기준으로 동적으로 그려지므로(CourierFieldResolver 참고), 이 클래스는
    /// 그룹 자체(Items)만 들고 있는다.
    /// </summary>
    private class ShipmentPreviewRow
    {
        public required List<OfsOrderItem> Items { get; init; }
    }

    private Control CreateExportPreviewPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 3, 5, 0) };
        toolStrip.Controls.Add(new Label { Text = "택배사 출력 미리보기(묶음 단위) — 매핑 성공한 줄만 표시", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 4, 10, 0) });

        // 사용자 요청: 실제 택배사 양식에서 세팅한 헤더 그대로 보여주고, 거기에 매핑된 속성이
        // 없는 칸(박스타입/내품수량/운임/선착불유무 등)은 직접 입력해 업로드 전에 조정할 수 있게.
        // 어떤 택배사 기준인지 고를 드롭다운을 새로 두고, 마지막으로 고른 택배사를 기억한다(거의
        // 안 바뀌는 값이라서).
        toolStrip.Controls.Add(new Label { Text = "택배사:", AutoSize = true, Padding = new Padding(10, 4, 4, 0) });
        _previewCourierCombo = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(CourierMaster.CourierName) };
        _previewCourierCombo.SelectedIndexChanged += OnPreviewCourierChanged;
        toolStrip.Controls.Add(_previewCourierCombo);

        var btnRefreshPreview = new Button { Text = "새로고침", Size = new Size(80, 24) };
        btnRefreshPreview.Click += (s, e) => RefreshExportPreview();
        toolStrip.Controls.Add(btnRefreshPreview);
        var btnUndoPreview = new Button { Text = "실행취소", Size = new Size(80, 24) };
        btnUndoPreview.Click += OnUndoPreviewEditClick;
        toolStrip.Controls.Add(btnUndoPreview);

        _previewGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        // 컬럼은 고정돼 있지 않다 — 선택된 택배사의 헤더 매핑(HeaderMappingJson)에 따라
        // RefreshExportPreview()가 매번 다시 만든다(OnPreviewCourierChanged 참고).
        _previewGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        // 셀 편집을 시작하는 순간(커밋 전) 실행취소용 스냅샷을 떠둔다 — CellValueChanged는 값이
        // 이미 바뀐 뒤에 발생해서 "편집 전" 상태를 잡을 수 없기 때문이다.
        _previewGrid.CellBeginEdit += (s, e) => PushPreviewUndoSnapshot();
        _previewGrid.CellValueChanged += OnPreviewGridCellValueChanged;
        _previewGrid.CellFormatting += OnPreviewGridCellFormatting;
        SetupPreviewGridContextMenu();

        LoadPreviewCouriers();

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(_previewGrid, 0, 1);
        return layout;
    }

    /// <summary>
    /// 미리보기 택배사 드롭다운을 채우고, 마지막으로 골랐던 택배사(설정 파일에 기억됨)를 다시
    /// 선택한다. 그 택배사가 더는 없으면(삭제됨) 첫 번째 택배사로 대체한다.
    /// </summary>
    private void LoadPreviewCouriers()
    {
        var couriers = _courierRepository.GetAll();
        _previewCourierCombo.DataSource = couriers;

        if (couriers.Count == 0) return;

        var lastCourierName = _settingsService.GetLastFolder(LastPreviewCourierSettingKey);
        var toSelect = couriers.FirstOrDefault(c => c.CourierName == lastCourierName) ?? couriers[0];
        _previewCourierCombo.SelectedItem = toSelect;
    }

    private void OnPreviewCourierChanged(object? sender, EventArgs e)
    {
        if (_previewCourierCombo.SelectedItem is CourierMaster courier)
        {
            _settingsService.SetLastFolder(LastPreviewCourierSettingKey, courier.CourierName);
        }
        RefreshExportPreview();
    }

    /// <summary>그리드 행 인덱스를 _previewRowModels의 묶음으로 되돌린다(DataTable 바인딩이라 DataBoundItem이 DataRowView).</summary>
    private ShipmentPreviewRow? GetPreviewRowModel(int gridRowIndex)
    {
        if (gridRowIndex < 0 || gridRowIndex >= _previewGrid.Rows.Count) return null;
        if (_previewGrid.Rows[gridRowIndex].Cells["__rowIndex"].Value is not int modelIndex) return null;
        if (modelIndex < 0 || modelIndex >= _previewRowModels.Count) return null;
        return _previewRowModels[modelIndex];
    }

    /// <summary>
    /// 품목이 4줄을 초과하는 묶음은 미리보기에서도 강조해, 내보내기 전에 미리 알아채고 줄을
    /// 합치거나 분리배송으로 나눌 수 있게 한다(CourierExporter의 4줄 초과 경고와 같은 기준).
    /// </summary>
    private void OnPreviewGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var row = GetPreviewRowModel(e.RowIndex);
        if (row == null) return;

        var isOverflow = ShipmentGrouping.CountDescriptionLines(row.Items) > 4;
        _previewGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor = isOverflow ? Color.MistyRose : _previewGrid.DefaultCellStyle.BackColor;
        _previewGrid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = isOverflow ? Color.Black : _previewGrid.DefaultCellStyle.ForeColor;
    }

    /// <summary>
    /// 미리보기 셀을 고치면(택배사 헤더에 매핑된 속성이 있는 칸은 그 묶음의 모든 원본 줄에,
    /// 매핑이 없는 칸은 그 줄만의 수동 입력값으로) 반영한다 — CourierFieldResolver.Apply 참고.
    /// RefreshExportPreview가 DataTable을 다시 채우는 동안에도 이 이벤트가 발생하므로
    /// _isRefreshingPreview로 그 경우는 무시한다(사용자 편집이 아님).
    /// </summary>
    private void OnPreviewGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_isRefreshingPreview) return;
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var columnName = _previewGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "__rowIndex") return;

        var row = GetPreviewRowModel(e.RowIndex);
        var entry = _previewHeaderEntries.FirstOrDefault(en => en.Header == columnName);
        if (row == null || entry == null) return;

        var newValue = Convert.ToString(_previewGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? string.Empty;
        CourierFieldResolver.Apply(entry, row.Items, newValue);

        _ordersGrid.Invalidate();
    }

    private void SetupPreviewGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("합포장(선택한 묶음들을 하나로 합치기)", null, OnMergePreviewGroupsClick);
        menu.Items.Add("분리배송 처리(묶음 풀기/줄이 1개면 복사해서 새 송장으로)", null, OnResetPreviewGroupsClick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("이 줄 복사(상품명 공란 — 송장에 표시할 메시지용)", null, OnDuplicatePreviewRowClick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("실행취소", null, OnUndoPreviewEditClick);
        _previewGrid.ContextMenuStrip = menu;
    }

    private List<ShipmentPreviewRow> GetSelectedPreviewRows()
    {
        return _previewGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => GetPreviewRowModel(r.Index))
            .OfType<ShipmentPreviewRow>()
            .ToList();
    }

    /// <summary>
    /// 하단 미리보기의 ContextMenuStrip Click 핸들러에서 바로 DataGridView.DataSource를 다시
    /// 갈아끼우면, 메뉴가 완전히 닫히기 전(Win32 메시지 처리가 덜 끝난 상태)에 그 작업이 겹쳐
    /// 마우스 커서가 멈춘 것처럼 보이는 경쟁 상태가 이 환경에서 재현됐다("분리배송 처리 클릭하면
    /// 멈춤" 신고). 다음 메시지 루프 틱으로 미뤄 메뉴가 완전히 닫힌 뒤에 실행한다.
    /// </summary>
    private void OnMergePreviewGroupsClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedPreviewRows();
        BeginInvoke(() =>
        {
            if (selected.Count < 2)
            {
                MessageBox.Show("합포장으로 합칠 묶음을 2개 이상 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PushPreviewUndoSnapshot();

            var itemsToRegroup = selected.SelectMany(r => r.Items).ToList();
            ClearStaleInvoiceLabelOverrides(itemsToRegroup);
            var groupId = ShipmentGrouping.GetEffectiveGroupId(selected[0].Items[0]);
            foreach (var item in itemsToRegroup)
            {
                item.ShipmentGroupId = groupId;
            }
            _ordersGrid.Invalidate();
            RefreshExportPreview();
            _statusLabel.Text = $"{selected.Count}개 묶음을 합포장으로 합쳤습니다. ({DateTime.Now:HH:mm:ss})";
        });
    }

    /// <summary>
    /// 줄이 2개 이상인 묶음(합포장돼 있던 것)을 선택하면 각자 단독 묶음으로 풀어준다(기존 동작).
    /// 줄이 1개뿐인 묶음은 풀어낼 게 없어 아무 변화도 안 보이는 것처럼 보인다는 사용자 신고가
    /// 있었음 — 그런 경우는 그 줄을 통째로 복사해 새 별도 송장으로 만들어준다(품목/수량은 사용자가
    /// 그 다음에 직접 수동으로 나눠 입력할 것을 전제로 함). 두 동작 모두 한 메뉴("분리배송 처리")로
    /// 묶어 제공하며, 선택한 묶음마다 줄 수에 따라 적합한 동작을 자동으로 고른다.
    /// </summary>
    private void OnResetPreviewGroupsClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedPreviewRows();
        BeginInvoke(() =>
        {
            if (selected.Count == 0)
            {
                MessageBox.Show("분리배송 처리할 묶음을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PushPreviewUndoSnapshot();

            var multiItemGroups = selected.Where(r => r.Items.Count > 1).ToList();
            var ungroupedCount = 0;
            if (multiItemGroups.Count > 0)
            {
                var itemsToUngroup = multiItemGroups.SelectMany(r => r.Items).ToList();
                ClearStaleInvoiceLabelOverrides(itemsToUngroup);
                foreach (var item in itemsToUngroup)
                {
                    item.ShipmentGroupId = null;
                }
                ungroupedCount = multiItemGroups.Count;
            }

            var singleItemGroups = selected.Where(r => r.Items.Count == 1).ToList();
            foreach (var row in singleItemGroups)
            {
                var template = row.Items[0];
                var duplicate = CloneOrderItem(template);
                duplicate.TrackingNo = null; // 아직 출고되지 않은 별도 송장이라 운송장번호는 새로 받아야 함
                duplicate.InvoiceLabel = null; // 옛 묶음 구성 기준 오버라이드를 그대로 들고 오면 안 됨
                duplicate.ShipmentGroupId = $"{ShipmentGrouping.GetEffectiveGroupId(template)}-분리{Guid.NewGuid().ToString("N")[..6]}";
                _orders.Insert(_orders.IndexOf(template) + 1, duplicate);
            }

            _ordersGrid.Invalidate();
            RefreshExportPreview();

            var messageParts = new List<string>();
            if (ungroupedCount > 0) messageParts.Add($"{ungroupedCount}개 묶음을 분리배송 처리");
            if (singleItemGroups.Count > 0) messageParts.Add($"{singleItemGroups.Count}건을 복사해 새 송장으로 분리(품목/수량을 직접 수정하세요)");
            _statusLabel.Text = $"{string.Join(", ", messageParts)}했습니다. ({DateTime.Now:HH:mm:ss})";
        });
    }

    /// <summary>
    /// 선택한 묶음에 새 줄을 하나 복사해서 추가한다. 상품명만 공란으로 두어, 운영자가 상품명 칸에
    /// 자유 텍스트(CS 메시지, 안내문구 등)를 입력하면 그게 그대로 송장의 품목란에 한 줄로 같이
    /// 나가게 하기 위함이다(택배사 양식엔 별도 메모란이 없는 경우가 많아서 이렇게 끼워 넣는다).
    /// 새 줄은 원본과 같은 묶음으로 묶여 같은 송장에 함께 출력된다.
    /// </summary>
    private void OnDuplicatePreviewRowClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedPreviewRows();
        BeginInvoke(() =>
        {
            if (selected.Count != 1)
            {
                MessageBox.Show("복사할 묶음을 1개만 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PushPreviewUndoSnapshot();

            var template = selected[0].Items[0];
            // 원본도 같은 묶음 키를 명시적으로 가지도록 해서, 새로 추가한 메시지 줄과 항상 같은
            // 송장으로 묶이게 한다(원본이 아직 기본값=그룹화 없음 상태였다면 지금 명시값으로 고정).
            // 묶음 구성이 바뀌므로(줄이 하나 늘어남) 옛 InvoiceLabel 오버라이드가 남아있으면 지운다.
            ClearStaleInvoiceLabelOverrides([template]);
            var groupId = ShipmentGrouping.GetEffectiveGroupId(template);
            template.ShipmentGroupId = groupId;

            var duplicate = new OfsOrderItem
            {
                ChannelCode = template.ChannelCode,
                OrderNo = template.OrderNo,
                ProductName = string.Empty, // 상품명만 공란 — 여기에 송장에 표시할 메시지를 직접 입력한다.
                OptionName = template.OptionName,
                Quantity = template.Quantity,
                Recipient = template.Recipient,
                Phone = template.Phone,
                Address = template.Address,
                DeliveryMessage = template.DeliveryMessage,
                MappedSku = template.MappedSku,
                Status = template.Status,
                TrackingNo = template.TrackingNo,
                ShipmentGroupId = groupId,
            };

            _orders.Insert(_orders.IndexOf(template) + 1, duplicate);
            _ordersGrid.Invalidate();
            RefreshExportPreview();

            // 그리드를 막 다시 그린 직후 모달을 띄우면 같은 위험 패턴이라 상태표시줄로 대체.
            _statusLabel.Text = "줄을 복사했습니다. 위 상세 목록 맨 아래 새 줄의 '상품명' 칸에 송장에 표시할 메시지를 입력하세요.";
        });
    }

    /// <summary>
    /// 매핑 성공한 줄을 묶음 단위로 모아 미리보기 그리드를 다시 채운다. 발주서 로드, 매핑 적용,
    /// 분리배송/합포장 조작 등 미리보기에 영향을 줄 수 있는 작업 뒤에 호출한다. 컬럼은 고정이
    /// 아니라 선택된 택배사의 헤더 매핑(HeaderMappingJson) 그대로 — 실제 출력될 헤더/내용을
    /// 내보내기 전에 미리 보고 싶다는 요청에 따른 것이다(CourierFieldResolver로 실제 내보내기와
    /// 같은 로직을 공유해 미리보기와 실제 결과가 항상 일치하게 한다).
    /// </summary>
    private void RefreshExportPreview()
    {
        var courier = _previewCourierCombo.SelectedItem as CourierMaster;

        _previewRowModels = _orders
            .Where(o => !string.IsNullOrWhiteSpace(o.MappedSku))
            .GroupBy(ShipmentGrouping.GetEffectiveGroupId)
            .Select(g => new ShipmentPreviewRow { Items = g.ToList() })
            .ToList();

        if (courier == null)
        {
            _previewHeaderEntries = [];
            _previewGrid.Columns.Clear();
            _previewGrid.DataSource = null;
            return;
        }

        // 같은 헤더 이름이 중복 설정돼 있으면 DataTable 컬럼 이름 충돌이 나므로, 먼저 나온 것만
        // 살린다(정상적인 택배사 설정에서는 발생하지 않는 방어 코드).
        var entries = CourierHeaderMapping.Parse(courier.HeaderMappingJson)
            .GroupBy(en => en.Header)
            .Select(g => g.First())
            .ToList();

        var headersChanged = !entries.Select(en => en.Header).SequenceEqual(_previewHeaderEntries.Select(en => en.Header));
        _previewHeaderEntries = entries;
        if (headersChanged || _previewGrid.Columns.Count == 0)
        {
            RebuildPreviewColumns(entries);
        }

        var channelConfigsByCode = _channelConfigService.Load().ToDictionary(c => c.ChannelCode);

        var table = new DataTable();
        table.Columns.Add("__rowIndex", typeof(int));
        foreach (var entry in entries) table.Columns.Add(entry.Header, typeof(string));

        for (int i = 0; i < _previewRowModels.Count; i++)
        {
            var row = _previewRowModels[i];
            var channelCode = row.Items[0].ChannelCode;
            var channelConfig = !string.IsNullOrEmpty(channelCode) ? channelConfigsByCode.GetValueOrDefault(channelCode) : null;

            var dataRow = table.NewRow();
            dataRow["__rowIndex"] = i;
            foreach (var entry in entries)
            {
                dataRow[entry.Header] = CourierFieldResolver.Resolve(entry, row.Items, courier, channelConfig) ?? string.Empty;
            }
            table.Rows.Add(dataRow);
        }

        var prevSortColName = _previewGrid.SortedColumn?.Name;
        var prevSortOrder = _previewGrid.SortOrder;

        _isRefreshingPreview = true;
        try
        {
            _previewGrid.DataSource = table;
        }
        finally
        {
            _isRefreshingPreview = false;
        }

        if (_previewGrid.Columns["__rowIndex"] is { } hiddenColumn) hiddenColumn.Visible = false;

        if (prevSortColName != null && prevSortOrder != SortOrder.None &&
            _previewGrid.Columns[prevSortColName] is { } sortCol)
        {
            _previewGrid.Sort(sortCol, prevSortOrder == SortOrder.Ascending
                ? System.ComponentModel.ListSortDirection.Ascending
                : System.ComponentModel.ListSortDirection.Descending);
        }

        // 채널별 고정값 오버라이드가 있는 칸은 직접 입력해도 실제 내보내기엔 반영되지 않으니,
        // 그 줄의 그 칸만 셀 단위로 읽기전용으로 막는다(컬럼 전체를 막으면 다른 채널 줄까지 같이
        // 막혀버려서 — 고정값은 채널마다 다를 수 있어 셀 단위로 처리).
        for (int i = 0; i < _previewRowModels.Count; i++)
        {
            var row = _previewRowModels[i];
            var channelCode = row.Items[0].ChannelCode;
            var channelConfig = !string.IsNullOrEmpty(channelCode) ? channelConfigsByCode.GetValueOrDefault(channelCode) : null;
            foreach (var entry in entries)
            {
                if (CourierFieldResolver.GetFixedOverride(channelConfig, courier.CourierName, entry.Header) != null)
                {
                    _previewGrid.Rows[i].Cells[entry.Header].ReadOnly = true;
                }
            }
        }
    }

    /// <summary>선택된 택배사의 헤더 목록이 바뀌었을 때만 호출 — 그리드 컬럼을 그 헤더 그대로 다시 만든다.</summary>
    private void RebuildPreviewColumns(List<HeaderMappingEntry> entries)
    {
        _previewGrid.Columns.Clear();
        _previewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "__rowIndex", DataPropertyName = "__rowIndex", Visible = false });

        foreach (var entry in entries)
        {
            _previewGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = entry.Header,
                HeaderText = entry.Header,
                DataPropertyName = entry.Header,
                // 매핑된 속성이 없는 칸(박스타입/내품수량/운임/선착불유무 등)과, 그룹 전체에 같은
                // 값을 적용해야 의미가 맞는 칸(수취인/연락처/주소/배송메세지/운송장번호/품목)만
                // 직접 고칠 수 있게 한다. 나머지(매핑된 SKU 등 계산된 값)는 읽기전용.
                ReadOnly = !CourierFieldResolver.IsEditable(entry.PropertyName),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 80,
            });
        }
    }

    /// <summary>
    /// 미리보기에서 직접 편집/합포장/분리배송/줄복사를 하기 직전에 _orders 전체를 복제해 쌓아둔다.
    /// 최근 5건만 보관하며(그 이상이면 가장 오래된 것을 버림), "실행취소"를 누르면 가장 최근
    /// 스냅샷으로 되돌린다.
    /// </summary>
    private void PushPreviewUndoSnapshot()
    {
        _previewUndoStack.Add(_orders.Select(CloneOrderItem).ToList());
        if (_previewUndoStack.Count > MaxPreviewUndoSteps)
        {
            _previewUndoStack.RemoveAt(0);
        }
    }

    private static OfsOrderItem CloneOrderItem(OfsOrderItem item) => new()
    {
        ChannelCode = item.ChannelCode,
        OrderNo = item.OrderNo,
        ProductName = item.ProductName,
        OptionName = item.OptionName,
        Quantity = item.Quantity,
        Recipient = item.Recipient,
        Phone = item.Phone,
        Address = item.Address,
        DeliveryMessage = item.DeliveryMessage,
        OrderDate = item.OrderDate,
        MappedSku = item.MappedSku,
        Status = item.Status,
        TrackingNo = item.TrackingNo,
        InvoiceLabel = item.InvoiceLabel,
        InvoiceDisplayName = item.InvoiceDisplayName,
        ShipmentGroupId = item.ShipmentGroupId,
    };

    private void OnUndoPreviewEditClick(object? sender, EventArgs e)
    {
        BeginInvoke(() =>
        {
            if (_previewUndoStack.Count == 0)
            {
                MessageBox.Show("실행취소할 변경 내용이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var snapshot = _previewUndoStack[^1];
            _previewUndoStack.RemoveAt(_previewUndoStack.Count - 1);

            _orders.RaiseListChangedEvents = false;
            try
            {
                _orders.Clear();
                foreach (var item in snapshot) _orders.Add(item);
            }
            finally
            {
                _orders.RaiseListChangedEvents = true;
            }
            _orders.ResetBindings();

            _ordersGrid.Invalidate();
            RefreshExportPreview();
            _statusLabel.Text = $"이전 상태로 되돌렸습니다. ({DateTime.Now:HH:mm:ss})";
        });
    }
}