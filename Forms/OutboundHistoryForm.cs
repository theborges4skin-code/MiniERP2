using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;

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

    private ComboBox _channelComboBox = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ExcelLikeDataGridView _historyGrid = new();
    private Label _statusLabel = new();
    private bool _suppressCellEndEdit;

    public OutboundHistoryForm()
    {
        InitializeComponent();
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

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 100 };

        var btnLoad = new Button { Text = "조회", Size = new Size(80, 30) };
        var btnImportTracking = new Button { Text = "운송장번호 불러오기", Size = new Size(150, 30) };
        var btnExport = new Button { Text = "선택 건 택배사 양식 출력", Size = new Size(170, 30) };
        var btnDelete = new Button { Text = "선택 삭제", Size = new Size(90, 30) };

        btnLoad.Click += OnLoadClick;
        btnImportTracking.Click += OnImportTrackingClick;
        btnExport.Click += OnExportClick;
        btnDelete.Click += OnDeleteClick;

        toolStrip.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolStrip.Controls.Add(_channelComboBox);
        toolStrip.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_fromDatePicker);
        toolStrip.Controls.Add(new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        toolStrip.Controls.Add(_toDatePicker);
        toolStrip.Controls.Add(btnLoad);
        toolStrip.Controls.Add(btnImportTracking);
        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnDelete);

        _historyGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "OutboundHistoryForm.HistoryGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };

        _historyGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelCode", DataPropertyName = "ChannelCode", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", Name = "OrderNo", DataPropertyName = "OrderNo", Width = 120, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "수령인", Name = "Recipient", DataPropertyName = "Recipient", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "주소", Name = "Address", DataPropertyName = "Address", Width = 220, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "품목명", Name = "ProductName", DataPropertyName = "ProductName", Width = 130, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "SKU", Name = "MskuCode", DataPropertyName = "MskuCode", Width = 110, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 55 },
            new DataGridViewTextBoxColumn { HeaderText = "납품가", Name = "SupplyPrice", DataPropertyName = "SupplyPrice", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", DataPropertyName = "TrackingNo", Width = 120 },
            new DataGridViewComboBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "Status", Width = 90, Items = { "발주확정", "출고확정" }, FlatStyle = FlatStyle.Flat },
            new DataGridViewTextBoxColumn { HeaderText = "발주확정 시점", Name = "CreatedAt", DataPropertyName = "CreatedAt", Width = 130, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "출고확정 시점", Name = "ConfirmedAt", DataPropertyName = "ConfirmedAt", Width = 130, ReadOnly = true }
        );
        _historyGrid.CellEndEdit += OnHistoryGridCellEndEdit;
        // 옛 용어("발송대기"/"발송완료")로 저장된 데이터가 DB 정규화 전에 이미 메모리에 올라온 경우 등
        // 상태 콤보(Items)에 없는 값이 들어와도 창이 죽지 않도록 방어한다(DataGridViewComboBoxCell 오류).
        _historyGrid.DataError += (s, e) => { e.ThrowException = false; };

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "조회 버튼을 눌러 발주/출고 이력을 불러오세요.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_historyGrid, 0, 1);
        mainLayout.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(mainLayout);
    }

    private void OnLoadClick(object? sender, EventArgs e)
    {
        var channelCode = _channelComboBox.SelectedValue as string;
        var from = _fromDatePicker.Value.Date;
        var to = _toDatePicker.Value.Date.AddDays(1).AddTicks(-1);

        var details = _outboundRepository.GetHistory(string.IsNullOrEmpty(channelCode) ? null : channelCode, from, to);
        EnsureStatusItemsInclude(details.Select(d => d.Status));
        _suppressCellEndEdit = true;
        _historyGrid.DataSource = new BindingList<OutboundDetail>(details);
        _suppressCellEndEdit = false;
        _statusLabel.Text = $"발주/출고 이력 {details.Count}건 조회됨.";
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
    /// 그리드 셀을 직접 수정(수량/납품가/운송장번호/상태)하면 바로 DB에 반영한다. 상태를
    /// "출고확정"으로 직접 바꾸면 확정일시가 비어있을 때 현재 시각으로 채운다.
    /// </summary>
    private void OnHistoryGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressCellEndEdit) return;
        if (e.RowIndex < 0 || e.RowIndex >= _historyGrid.Rows.Count) return;
        if (_historyGrid.Rows[e.RowIndex].DataBoundItem is not OutboundDetail detail) return;

        if (detail.Status == "출고확정" && detail.ConfirmedAt is null)
        {
            detail.ConfirmedAt = DateTime.UtcNow;
        }
        else if (detail.Status == "발주확정")
        {
            detail.ConfirmedAt = null;
        }

        _outboundRepository.UpdateDetail(detail);
        _historyGrid.InvalidateRow(e.RowIndex);
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

        var selected = _historyGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.DataBoundItem)
            .OfType<OutboundDetail>()
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("택배사 양식으로 출력할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var courierDialog = new SelectCourierDialog();
        if (courierDialog.ShowDialog(this) != DialogResult.OK || courierDialog.SelectedCourier is not { } courier)
        {
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"{courier.CourierName}_출고_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("OutboundHistoryExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
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
            var overflowGroups = await _courierExporter.ExportAsync(orderItems, courier, filePath, channelConfigsByCode);
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
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ex.Message}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        var selected = _historyGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.DataBoundItem)
            .OfType<OutboundDetail>()
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("삭제할 줄을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show($"선택한 {selected.Count}건을 삭제하시겠습니까?\n삭제하면 되돌릴 수 없습니다.", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _outboundRepository.DeleteByIds(selected.Select(d => d.Id));
        foreach (var item in selected) details.Remove(item);
        _statusLabel.Text = $"{selected.Count}건을 삭제했습니다.";
    }

    /// <summary>
    /// 운송장 결과 파일(택배사 프로그램에서 받은 엑셀)을 불러와 수령인 기준으로 매칭하고 운송장번호를
    /// 채운다. 주소/품목이 운송장 파일엔 불분명하게 나오므로 매칭은 수령인만으로 하고, 동일 수령인이
    /// 여러 건이면 사용자에게 직접 고르게 한다.
    /// </summary>
    private void OnImportTrackingClick(object? sender, EventArgs e)
    {
        if (_historyGrid.DataSource is not BindingList<OutboundDetail> details || details.Count == 0)
        {
            MessageBox.Show("먼저 조회 버튼으로 발주확정 대상 이력을 불러오세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var courierDialog = new SelectCourierDialog();
        if (courierDialog.ShowDialog(this) != DialogResult.OK || courierDialog.SelectedCourier is not { } courier)
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
            Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
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
        using var package = ExcelFileOpener.OpenWithPasswordPrompt(filePath, this);
        if (package == null) return;

        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        var headerRow = courier.TrackingImportHeaderRow;
        if (worksheet?.Dimension == null || headerRow > worksheet.Dimension.End.Row)
        {
            MessageBox.Show("엑셀 파일에서 데이터를 찾을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        int? recipientCol = null, trackingCol = null;
        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var header = worksheet.Cells[headerRow, col].Value?.ToString()?.Trim();
            if (header is null) continue;
            if (string.Equals(header, courier.TrackingImportRecipientHeader, StringComparison.OrdinalIgnoreCase)) recipientCol = col;
            if (string.Equals(header, courier.TrackingImportTrackingNoHeader, StringComparison.OrdinalIgnoreCase)) trackingCol = col;
        }

        if (recipientCol is null || trackingCol is null)
        {
            MessageBox.Show(
                $"설정된 헤더(\"{courier.TrackingImportRecipientHeader}\"/\"{courier.TrackingImportTrackingNoHeader}\")를 {headerRow}행에서 찾지 못했습니다.\n택배사 양식 설정을 확인하세요.",
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 매칭 대상은 운송장번호가 아직 없는(=발주확정 상태) 건으로 한정한다. 수령인별로 모아두면
        // 동일 수령인 다건을 한 번에 비교할 수 있다.
        var candidatesByRecipient = details
            .Where(d => string.IsNullOrWhiteSpace(d.TrackingNo))
            .GroupBy(d => d.Recipient, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var appliedCount = 0;
        var skippedNoMatch = new List<string>();
        var skippedByUser = 0;

        for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
        {
            var recipient = worksheet.Cells[row, recipientCol.Value].Value?.ToString()?.Trim();
            var trackingNo = worksheet.Cells[row, trackingCol.Value].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(trackingNo)) continue;

            if (!candidatesByRecipient.TryGetValue(recipient, out var candidates) || candidates.Count == 0)
            {
                skippedNoMatch.Add($"{recipient}({trackingNo})");
                continue;
            }

            OutboundDetail target;
            if (candidates.Count == 1)
            {
                target = candidates[0];
            }
            else
            {
                using var picker = new TrackingMatchPickerDialog(recipient, trackingNo, candidates);
                if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is null)
                {
                    skippedByUser++;
                    continue;
                }
                target = picker.Selected;
            }

            _outboundRepository.ApplyTrackingNo(target.Id, trackingNo);
            target.TrackingNo = trackingNo;
            target.Status = "출고확정";
            target.ConfirmedAt = DateTime.UtcNow;
            candidates.Remove(target); // 같은 수령인의 다른 건에 같은 운송장번호가 재적용되지 않게 한다.
            appliedCount++;
        }

        _historyGrid.Refresh();
        _statusLabel.Text = $"운송장번호 {appliedCount}건 적용 완료.";

        var summary = $"운송장번호 {appliedCount}건을 적용해 출고확정으로 처리했습니다.";
        if (skippedNoMatch.Count > 0) summary += $"\n일치하는 발주확정 건이 없어 건너뛴 항목: {skippedNoMatch.Count}건";
        if (skippedByUser > 0) summary += $"\n동일 수령인 중 사용자가 건너뛴 항목: {skippedByUser}건";
        MessageBox.Show(summary, "운송장번호 불러오기 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
