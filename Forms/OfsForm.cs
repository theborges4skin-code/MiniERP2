using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Mapping;
using MiniERP2.Models;
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

    private ExcelLikeDataGridView _ordersGrid = new();
    private StatusStrip _statusStrip = new();
    private ToolStripStatusLabel _statusLabel = new();
    private BindingList<OfsOrderItem> _orders = new();

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
        var btnSave = new Button { Text = "저장 (출고 확정)", Size = new Size(130, 30) };
        var btnExport = new Button { Text = "택배사 양식으로 내보내기", Size = new Size(180, 30) };

        btnLoadOrders.Click += OnLoadOrdersClick;
        btnExport.Click += OnExportClick;
        btnSave.Click += OnSaveClick;
        btnAddManualOrder.Click += OnAddManualOrderClick;

        toolStrip.Controls.Add(btnLoadOrders);
        toolStrip.Controls.Add(btnAddManualOrder);
        toolStrip.Controls.Add(btnSave);
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
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", Width = 150 }
        );

        // 데이터 바인딩
        _ordersGrid.DataSource = _orders;

        // 상태에 따른 행 색상 변경을 위한 이벤트 핸들러 등록
        _ordersGrid.RowPrePaint += OnOrdersGridRowPrePaint;

        // 셀 값 변경 시 연관 데이터 자동 업데이트를 위한 이벤트 핸들러 등록
        _ordersGrid.CellValueChanged += OnOrdersGridCellValueChanged;

        // 3. Status Bar
        _statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusLabel = new ToolStripStatusLabel("준비");
        _statusStrip.Items.Add(_statusLabel);

        // Add controls to layout
        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_ordersGrid, 0, 1);
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
            MessageBox.Show($"선택한 채널 '{channelDialog.SelectedChannel.ChannelName}'의 설정을 찾을 수 없습니다.", "설정 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 선택된 채널의 매핑 규칙으로 SkuMapper를 생성합니다.
        var skuMapper = new SkuMapper(_mappingRepository, channelConfig.ChannelCode);

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

            foreach (var file in ofd.FileNames)
            {
                var loadedItems = await _orderLoader.LoadFromFileAsync(skuMapper, channelConfig, file);
                foreach (var item in loadedItems) _orders.Add(item);
            }

            _statusLabel.Text = $"총 {_orders.Count}개의 주문이 로드되었습니다.";
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

    private async void OnExportClick(object? sender, EventArgs e)
    {
        if (_orders.Count == 0)
        {
            MessageBox.Show("내보낼 주문 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        // 3. (TODO) CourierExporter를 사용하여 파일 생성
        try
        {
            // 이 부분에 실제 엑셀 파일을 생성하는 CourierExporter 로직이 들어갑니다.
            // 현재는 임시 파일을 생성하여 후처리 과정을 시연합니다.
            File.WriteAllText(filePath, "택배사 양식 엑셀 파일 내용 (구현 예정)");
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ex.Message}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        var result = MessageBox.Show($"{ordersToSave.Count}개의 주문을 출고 확정하고 저장하시겠습니까?", "저장 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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

                    var csku = _channelSkuRepository.GetByChannelAndMsku(order.ChannelCode, order.MappedSku);
                    if (csku != null)
                    {
                        outboundDetails.Add(new OutboundDetail
                        {
                            ChannelCode = order.ChannelCode ?? string.Empty,
                            OrderNo = order.OrderNo ?? string.Empty,
                            TrackingNo = order.TrackingNo ?? string.Empty,
                            MskuCode = order.MappedSku,
                            Qty = order.Quantity,
                            SupplyPrice = csku.SupplyPrice
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
        // 성공한 주문들의 상태를 '출고 완료'로 변경
        var savedOrderNos = new HashSet<string>(savedDetails.Select(d => d.OrderNo));
        var savedOrdersInGrid = _orders.Where(o => o.OrderNo != null && savedOrderNos.Contains(o.OrderNo)).ToList();
        foreach (var order in savedOrdersInGrid)
        {
            order.Status = "출고 완료";
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

        // 매핑 상태에 따라 배경색 설정
        if (item.Status == "매핑 실패" || item.Status == "매핑 키 없음" || item.Status == "납품가 없음")
        {
            row.DefaultCellStyle.BackColor = Color.MistyRose;
        }
        else if (item.Status.StartsWith("매핑("))
        {
            row.DefaultCellStyle.BackColor = Color.Honeydew;
        }
        else if (item.Status == "출고 완료")
        {
            row.DefaultCellStyle.BackColor = Color.Honeydew;
        }
        else
        {
            // 다른 상태는 기본 배경색으로 되돌립니다 (가상화 시 중요).
            row.DefaultCellStyle.BackColor = _ordersGrid.DefaultCellStyle.BackColor;
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
    }
}