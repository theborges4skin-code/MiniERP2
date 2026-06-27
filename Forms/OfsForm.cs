using System.ComponentModel;
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
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Quantity", DataPropertyName = "Quantity", Width = 60 },
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
                    if (csku != null)
                    {
                        outboundDetails.Add(new OutboundDetail
                        {
                            ChannelCode = order.ChannelCode ?? string.Empty,
                            OrderNo = order.OrderNo ?? string.Empty,
                            TrackingNo = order.TrackingNo ?? string.Empty,
                            MskuCode = order.MappedSku,
                            Qty = order.Quantity,
                            SupplyPrice = csku.SupplyPrice,
                            // 운송장 결과를 나중에 수령인 기준으로 매칭하려면 이 시점의 수령인/주소/
                            // 품목명을 함께 남겨둬야 한다(발주/출고 이력 관리창에서 사용).
                            Recipient = order.Recipient ?? string.Empty,
                            Address = order.Address ?? string.Empty,
                            ProductName = order.ProductName ?? string.Empty,
                        });
                    }
                    else
                    {
                        failedOrders.Add(order);
                    }
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
            // SKU가 수동으로 입력되었는지, 아니면 지워졌는지 확인
            if (!string.IsNullOrWhiteSpace(item.MappedSku))
            {
                item.Status = "수동 매핑";
            }
            else
            {
                // SKU가 비워졌다면, 원래의 매핑 로직을 다시 적용해볼 수 있습니다.
                // 여기서는 간단하게 '매핑 실패'로 처리합니다.
                item.Status = "매핑 실패";
            }
            // 변경된 상태를 그리드에 즉시 반영하기 위해 해당 행을 무효화합니다.
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
        menu.Items.Add("합포장으로 묶기", null, OnMergeIntoOneShipmentClick);
        menu.Items.Add("분리배송으로 분리", null, OnSplitIntoNewShipmentClick);
        menu.Items.Add("묶음 해제", null, OnResetShipmentGroupClick);
    }

    private List<OfsOrderItem> GetSelectedOrderItems()
    {
        return _ordersGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.DataBoundItem)
            .OfType<OfsOrderItem>()
            .ToList();
    }

    private void OnMergeIntoOneShipmentClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedOrderItems();
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

        var groupId = ShipmentGrouping.GetEffectiveGroupId(selected[0]);
        foreach (var item in selected)
        {
            item.ShipmentGroupId = groupId;
        }
        _ordersGrid.Invalidate();
        RefreshExportPreview();
    }

    private void OnSplitIntoNewShipmentClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedOrderItems();
        if (selected.Count == 0)
        {
            MessageBox.Show("분리배송으로 분리할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var baseId = ShipmentGrouping.GetEffectiveGroupId(selected[0]);
        var newGroupId = $"{baseId}-분리{Guid.NewGuid().ToString("N")[..6]}";
        foreach (var item in selected)
        {
            item.ShipmentGroupId = newGroupId;
        }
        _ordersGrid.Invalidate();
        RefreshExportPreview();
    }

    private void OnResetShipmentGroupClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedOrderItems();
        if (selected.Count == 0)
        {
            MessageBox.Show("묶음을 해제할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var item in selected)
        {
            item.ShipmentGroupId = null;
        }
        _ordersGrid.Invalidate();
        RefreshExportPreview();
    }

    // ===================== 택배사 출력 미리보기 =====================

    /// <summary>
    /// 묶음(=송장) 단위로 묶인 미리보기 한 줄을 나타낸다. 실제 택배사 출력 시 CourierExporter가
    /// 만드는 결과와 같은 조합 규칙(<see cref="ShipmentGrouping.BuildCombinedItemDescription"/>)을
    /// 써서, 내보내기 전에 미리 어떤 모습으로 나갈지 확인할 수 있게 한다. 배송메세지/운송장번호는
    /// 여기서 수정하면 묶음에 속한 모든 원본 줄에 그대로 반영된다.
    /// </summary>
    private class ShipmentPreviewRow
    {
        public required List<OfsOrderItem> Items { get; init; }

        public string OrderNos => string.Join(", ", Items.Select(i => i.OrderNo).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct());

        public string? Recipient
        {
            get => Items[0].Recipient;
            set { foreach (var item in Items) item.Recipient = value; }
        }

        public string? Phone
        {
            get => Items[0].Phone;
            set { foreach (var item in Items) item.Phone = value; }
        }

        public string? Address
        {
            get => Items[0].Address;
            set { foreach (var item in Items) item.Address = value; }
        }

        public string? DeliveryMessage
        {
            get => Items[0].DeliveryMessage;
            set { foreach (var item in Items) item.DeliveryMessage = value; }
        }

        /// <summary>
        /// 실제 송장에 출력될 품목 내용입니다. 직접 고치면 이 묶음의 첫 줄(Items[0])의
        /// InvoiceLabel을 그 값으로 덮어쓰고, 나머지 줄들의 InvoiceLabel은 빈 문자열로 비워
        /// (BuildCombinedItemDescription이 빈 줄은 걸러내므로) 결합 결과가 입력한 값 그대로
        /// 나가게 한다. CourierExporter도 같은 InvoiceLabel을 읽으므로 실제 내보내기에도 그대로
        /// 반영된다(별도의 미리보기 전용 오버라이드 저장소가 필요 없음).
        /// </summary>
        public string ItemsDescription
        {
            get => ShipmentGrouping.BuildCombinedItemDescription(Items);
            set
            {
                Items[0].InvoiceLabel = value;
                for (int i = 1; i < Items.Count; i++)
                {
                    Items[i].InvoiceLabel = string.Empty;
                }
            }
        }

        public int TotalQuantity => Items.Sum(i => i.Quantity);

        public string? TrackingNo
        {
            get => Items[0].TrackingNo;
            set { foreach (var item in Items) item.TrackingNo = value; }
        }

        public int LineCount => Items.Count;
    }

    private Control CreateExportPreviewPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 3, 5, 0) };
        toolStrip.Controls.Add(new Label { Text = "택배사 출력 미리보기(묶음 단위) — 매핑 성공한 줄만 표시", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 4, 10, 0) });
        var btnRefreshPreview = new Button { Text = "새로고침", Size = new Size(80, 24) };
        btnRefreshPreview.Click += (s, e) => RefreshExportPreview();
        toolStrip.Controls.Add(btnRefreshPreview);
        var btnUndoPreview = new Button { Text = "실행취소", Size = new Size(80, 24) };
        btnUndoPreview.Click += OnUndoPreviewEditClick;
        toolStrip.Controls.Add(btnUndoPreview);

        _previewGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        // 주문번호들/총수량/줄수는 여러 줄을 합친 순수 집계값이라 직접 수정할 단일 대상이 없어
        // 읽기전용으로 둔다. 그 외(수취인/연락처/주소/배송메세지/품목/운송장번호)는 모두 직접
        // 고칠 수 있게 했고, 고치면 해당 묶음의 원본 줄에 그대로 반영된다(각 속성의 setter 처리).
        _previewGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "OrderNos", HeaderText = "주문번호", DataPropertyName = "OrderNos", Width = 150, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "Recipient", HeaderText = "수취인", DataPropertyName = "Recipient", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Phone", HeaderText = "연락처", DataPropertyName = "Phone", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "주소", DataPropertyName = "Address", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "DeliveryMessage", HeaderText = "배송메세지", DataPropertyName = "DeliveryMessage", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "ItemsDescription", HeaderText = "품목(실제 출력될 내용)", DataPropertyName = "ItemsDescription", Width = 220 },
            new DataGridViewTextBoxColumn { Name = "TotalQuantity", HeaderText = "총수량", DataPropertyName = "TotalQuantity", Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "TrackingNo", HeaderText = "운송장번호", DataPropertyName = "TrackingNo", Width = 130 },
            new DataGridViewTextBoxColumn { Name = "LineCount", HeaderText = "줄수", DataPropertyName = "LineCount", Width = 50, ReadOnly = true }
        );
        _previewGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        // 셀 편집을 시작하는 순간(커밋 전) 실행취소용 스냅샷을 떠둔다 — CellValueChanged는 값이
        // 이미 바뀐 뒤에 발생해서 "편집 전" 상태를 잡을 수 없기 때문이다.
        _previewGrid.CellBeginEdit += (s, e) => PushPreviewUndoSnapshot();
        _previewGrid.CellValueChanged += OnPreviewGridCellValueChanged;
        _previewGrid.CellFormatting += OnPreviewGridCellFormatting;
        SetupPreviewGridContextMenu();

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(_previewGrid, 0, 1);
        return layout;
    }

    /// <summary>
    /// 품목이 4줄을 초과하는 묶음은 미리보기에서도 강조해, 내보내기 전에 미리 알아채고 줄을
    /// 합치거나 분리배송으로 나눌 수 있게 한다(CourierExporter의 4줄 초과 경고와 같은 기준).
    /// </summary>
    private void OnPreviewGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _previewGrid.Rows.Count) return;
        if (_previewGrid.Rows[e.RowIndex].DataBoundItem is not ShipmentPreviewRow row) return;

        var isOverflow = ShipmentGrouping.CountDescriptionLines(row.Items) > 4;
        _previewGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor = isOverflow ? Color.MistyRose : _previewGrid.DefaultCellStyle.BackColor;
        _previewGrid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = isOverflow ? Color.Black : _previewGrid.DefaultCellStyle.ForeColor;
    }

    /// <summary>
    /// 미리보기에서 배송메세지/운송장번호를 고치면 그 묶음에 속한 모든 원본 줄(ShipmentPreviewRow.
    /// Items)에 즉시 반영된다(속성 setter가 처리). 위쪽 상세 그리드도 같이 보이도록 무효화한다.
    /// </summary>
    private void OnPreviewGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        _ordersGrid.Invalidate();
    }

    private void SetupPreviewGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("합포장(선택한 묶음들을 하나로 합치기)", null, OnMergePreviewGroupsClick);
        menu.Items.Add("분리배송 처리(묶음 풀기)", null, OnResetPreviewGroupsClick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("이 줄 복사(상품명 공란 — 송장에 표시할 메시지용)", null, OnDuplicatePreviewRowClick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("실행취소", null, OnUndoPreviewEditClick);
        _previewGrid.ContextMenuStrip = menu;
    }

    private List<ShipmentPreviewRow> GetSelectedPreviewRows()
    {
        return _previewGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem)
            .OfType<ShipmentPreviewRow>()
            .ToList();
    }

    private void OnMergePreviewGroupsClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedPreviewRows();
        if (selected.Count < 2)
        {
            MessageBox.Show("합포장으로 합칠 묶음을 2개 이상 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        PushPreviewUndoSnapshot();

        var groupId = ShipmentGrouping.GetEffectiveGroupId(selected[0].Items[0]);
        foreach (var item in selected.SelectMany(r => r.Items))
        {
            item.ShipmentGroupId = groupId;
        }
        _ordersGrid.Invalidate();
        RefreshExportPreview();
    }

    private void OnResetPreviewGroupsClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedPreviewRows();
        if (selected.Count == 0)
        {
            MessageBox.Show("묶음을 해제할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        PushPreviewUndoSnapshot();

        foreach (var item in selected.SelectMany(r => r.Items))
        {
            item.ShipmentGroupId = null;
        }
        _ordersGrid.Invalidate();
        RefreshExportPreview();
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
        if (selected.Count != 1)
        {
            MessageBox.Show("복사할 묶음을 1개만 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        PushPreviewUndoSnapshot();

        var template = selected[0].Items[0];
        // 원본도 같은 묶음 키를 명시적으로 가지도록 해서, 새로 추가한 메시지 줄과 항상 같은
        // 송장으로 묶이게 한다(원본이 아직 기본값=그룹화 없음 상태였다면 지금 명시값으로 고정).
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

        _orders.Add(duplicate);
        _ordersGrid.Invalidate();
        RefreshExportPreview();

        MessageBox.Show(
            "줄을 복사했습니다. 위 상세 목록 맨 아래 새 줄의 '상품명' 칸에 송장에 표시할 메시지를 입력하세요.",
            "복사 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// 매핑 성공한 줄을 묶음 단위로 모아 미리보기 그리드를 다시 채운다. 발주서 로드, 매핑 적용,
    /// 분리배송/합포장 조작 등 미리보기에 영향을 줄 수 있는 작업 뒤에 호출한다.
    /// </summary>
    private void RefreshExportPreview()
    {
        var rows = _orders
            .Where(o => !string.IsNullOrWhiteSpace(o.MappedSku))
            .GroupBy(ShipmentGrouping.GetEffectiveGroupId)
            .Select(g => new ShipmentPreviewRow { Items = g.ToList() })
            .ToList();

        _previewGrid.DataSource = new BindingList<ShipmentPreviewRow>(rows);
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
        MappedSku = item.MappedSku,
        Status = item.Status,
        TrackingNo = item.TrackingNo,
        InvoiceLabel = item.InvoiceLabel,
        ShipmentGroupId = item.ShipmentGroupId,
    };

    private void OnUndoPreviewEditClick(object? sender, EventArgs e)
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
    }
}