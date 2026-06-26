using System.ComponentModel;
using System.Data;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.6절 '마감/이익분석' 창.
/// 이익분석(자동) 탭과 마감 대조(수기) 탭을 한 화면에서 다룬다.
/// </summary>
public class SettlementForm : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly SettlementRepository _settlementRepository = new();
    private readonly OutboundRepository _outboundRepository = new();
    private readonly SettlementLoader _settlementLoader = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();

    private ExcelLikeDataGridView _settlementGrid = new();
    private BindingList<SettlementData> _settlementRows = new();
    private ToolStripStatusLabel _statusLabel = new();

    private ExcelLikeDataGridView _outboundGrid = new();
    private DataGridView _statementGrid = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ComboBox _reconcileChannelComboBox = new();

    public SettlementForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "마감/이익분석";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var tabControl = new TabControl { Dock = DockStyle.Fill };
        tabControl.TabPages.Add(CreateProfitAnalysisTab());
        tabControl.TabPages.Add(CreateReconciliationTab());

        Controls.Add(tabControl);

        FormClosing += (s, e) =>
        {
            _settlementGrid.SaveLayout();
            _outboundGrid.SaveLayout();
        };
    }

    // ===================== 이익분석(자동) =====================

    private TabPage CreateProfitAnalysisTab()
    {
        var tabPage = new TabPage("이익분석(자동)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnLoad = new Button { Text = "정산파일 로드", Size = new Size(120, 30) };
        var btnSave = new Button { Text = "결과 저장", Size = new Size(100, 30) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };

        btnLoad.Click += OnLoadSettlementClick;
        btnSave.Click += OnSaveSettlementClick;
        btnExport.Click += OnExportSettlementClick;

        toolStrip.Controls.Add(btnLoad);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnExport);

        _settlementGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.SettlementGrid",
            AutoGenerateColumns = false,
        };
        _settlementGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelCode", DataPropertyName = "ChannelCode", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "상품명", Name = "ProductName", DataPropertyName = "ProductName", Width = 220 },
            new DataGridViewTextBoxColumn { HeaderText = "옵션명", Name = "OptionName", DataPropertyName = "OptionName", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "매핑 SKU", Name = "Msku", DataPropertyName = "Msku", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "정산액", Name = "Settlement", DataPropertyName = "Settlement", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "배송비", Name = "Shipping", DataPropertyName = "Shipping", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "입출고비", Name = "Fee", DataPropertyName = "Fee", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "이익액", Name = "Profit", DataPropertyName = "Profit", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "Status", Width = 100, ReadOnly = true }
        );
        _settlementGrid.DataSource = _settlementRows;
        _settlementGrid.RowPrePaint += OnSettlementGridRowPrePaint;

        var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusLabel = new ToolStripStatusLabel("준비");
        statusStrip.Items.Add(_statusLabel);

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_settlementGrid, 0, 1);
        mainLayout.Controls.Add(statusStrip, 0, 2);

        tabPage.Controls.Add(mainLayout);
        return tabPage;
    }

    private async void OnLoadSettlementClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Multiselect = true,
            Title = "정산 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("SettlementLoad") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

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

        var skuMapper = new SkuMapper(_mappingRepository, channelConfig.ChannelCode);

        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = $"'{channelDialog.SelectedChannel.ChannelName}' 채널의 설정으로 정산 파일을 읽는 중입니다...";

        try
        {
            _settingsService.SetLastFolder("SettlementLoad", Path.GetDirectoryName(ofd.FileNames[0])!);

            foreach (var file in ofd.FileNames)
            {
                var loadedRows = await _settlementLoader.LoadFromFileAsync(skuMapper, _itemRepository, channelConfig, file);
                foreach (var row in loadedRows) _settlementRows.Add(row);
            }

            _statusLabel.Text = $"총 {_settlementRows.Count}건의 정산 데이터가 로드되었습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n\n{ex.Message}", "로드 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "오류 발생";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async void OnSaveSettlementClick(object? sender, EventArgs e)
    {
        if (_settlementRows.Count == 0)
        {
            MessageBox.Show("저장할 이익분석 결과가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show($"{_settlementRows.Count}건의 이익분석 결과를 저장하시겠습니까?", "저장 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var rowsToSave = _settlementRows.ToList();
            await Task.Run(() => _settlementRepository.Insert(rowsToSave));
            _statusLabel.Text = $"{rowsToSave.Count}건 저장 완료.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnExportSettlementClick(object? sender, EventArgs e)
    {
        if (_settlementRows.Count == 0)
        {
            MessageBox.Show("내보낼 이익분석 결과가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"이익분석_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("SettlementExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("SettlementExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("이익분석");

            string[] headers = ["채널", "상품명", "옵션명", "매핑SKU", "수량", "정산액", "배송비", "입출고비", "이익액", "상태"];
            for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

            int row = 2;
            foreach (var data in _settlementRows)
            {
                sheet.Cells[row, 1].Value = data.ChannelCode;
                sheet.Cells[row, 2].Value = data.ProductName;
                sheet.Cells[row, 3].Value = data.OptionName;
                sheet.Cells[row, 4].Value = data.Msku;
                sheet.Cells[row, 5].Value = data.Qty;
                sheet.Cells[row, 6].Value = data.Settlement;
                sheet.Cells[row, 7].Value = data.Shipping;
                sheet.Cells[row, 8].Value = data.Fee;
                sheet.Cells[row, 9].Value = data.Profit;
                sheet.Cells[row, 10].Value = data.Status;
                row++;
            }

            sheet.Cells.AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ex.Message}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSettlementGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _settlementGrid.Rows.Count) return;

        var row = _settlementGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not SettlementData data) return;

        if (string.IsNullOrWhiteSpace(data.Msku) || data.Status == "원가 정보 없음")
        {
            row.DefaultCellStyle.BackColor = Color.MistyRose;
        }
        else
        {
            row.DefaultCellStyle.BackColor = _settlementGrid.DefaultCellStyle.BackColor;
        }
    }

    // ===================== 마감 대조(수기) =====================

    private TabPage CreateReconciliationTab()
    {
        var tabPage = new TabPage("마감 대조(수기)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        _reconcileChannelComboBox = new ComboBox { Size = new Size(160, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _reconcileChannelComboBox.DataSource = _salesChannelRepository.GetAll();
        _reconcileChannelComboBox.DisplayMember = "ChannelName";
        _reconcileChannelComboBox.ValueMember = "ChannelCode";

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 100 };

        var btnLoadOutbound = new Button { Text = "출고내역 조회", Size = new Size(110, 30) };
        var btnLoadStatement = new Button { Text = "거래처 마감내역 불러오기", Size = new Size(170, 30) };
        var btnExportOutbound = new Button { Text = "출고내역 엑셀로 내보내기", Size = new Size(170, 30) };

        btnLoadOutbound.Click += OnLoadOutboundClick;
        btnLoadStatement.Click += OnLoadStatementClick;
        btnExportOutbound.Click += OnExportOutboundClick;

        toolStrip.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolStrip.Controls.Add(_reconcileChannelComboBox);
        toolStrip.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_fromDatePicker);
        toolStrip.Controls.Add(new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        toolStrip.Controls.Add(_toDatePicker);
        toolStrip.Controls.Add(btnLoadOutbound);
        toolStrip.Controls.Add(btnExportOutbound);
        toolStrip.Controls.Add(btnLoadStatement);

        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };

        _outboundGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.OutboundGrid",
            AutoGenerateColumns = false,
        };
        _outboundGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", Name = "OrderNo", DataPropertyName = "OrderNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", DataPropertyName = "TrackingNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "SKU", Name = "MskuCode", DataPropertyName = "MskuCode", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "납품가", Name = "SupplyPrice", DataPropertyName = "SupplyPrice", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "출고일시", Name = "CreatedAt", DataPropertyName = "CreatedAt", Width = 130 }
        );

        _statementGrid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true, AllowUserToAddRows = false, ReadOnly = true };

        var outboundPanel = new Panel { Dock = DockStyle.Fill };
        outboundPanel.Controls.Add(WithGroupLabel("출고내역(시스템)", _outboundGrid));
        var statementPanel = new Panel { Dock = DockStyle.Fill };
        statementPanel.Controls.Add(WithGroupLabel("거래처 마감내역(외부 파일)", _statementGrid));

        splitContainer.Panel1.Controls.Add(outboundPanel);
        splitContainer.Panel2.Controls.Add(statementPanel);

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(splitContainer, 0, 1);

        tabPage.Controls.Add(mainLayout);
        return tabPage;
    }

    private static Control WithGroupLabel(string title, Control content)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font(Control.DefaultFont, FontStyle.Bold) }, 0, 0);
        content.Dock = DockStyle.Fill;
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private void OnLoadOutboundClick(object? sender, EventArgs e)
    {
        var channelCode = _reconcileChannelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("조회할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var details = _outboundRepository.GetByChannel(channelCode, _fromDatePicker.Value.Date, _toDatePicker.Value.Date.AddDays(1).AddTicks(-1));
        _outboundGrid.DataSource = new BindingList<OutboundDetail>(details);
        _statusLabel.Text = $"출고내역 {details.Count}건 조회됨.";
    }

    private void OnExportOutboundClick(object? sender, EventArgs e)
    {
        if (_outboundGrid.DataSource is not BindingList<OutboundDetail> details || details.Count == 0)
        {
            MessageBox.Show("내보낼 출고내역이 없습니다. 먼저 조회하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"출고내역_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("OutboundExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("OutboundExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("출고내역");

            string[] headers = ["주문번호", "운송장번호", "SKU", "수량", "납품가", "출고일시"];
            for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

            int row = 2;
            foreach (var detail in details)
            {
                sheet.Cells[row, 1].Value = detail.OrderNo;
                sheet.Cells[row, 2].Value = detail.TrackingNo;
                sheet.Cells[row, 3].Value = detail.MskuCode;
                sheet.Cells[row, 4].Value = detail.Qty;
                sheet.Cells[row, 5].Value = detail.SupplyPrice;
                sheet.Cells[row, 6].Value = detail.CreatedAt;
                row++;
            }

            sheet.Cells.AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ex.Message}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLoadStatementClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "거래처 제공 마감내역 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("StatementLoad") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _settingsService.SetLastFolder("StatementLoad", Path.GetDirectoryName(ofd.FileName)!);

            ExcelLicense.Ensure();
            using var package = new ExcelPackage(new FileInfo(ofd.FileName));
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null)
            {
                MessageBox.Show("엑셀 파일에서 데이터를 찾을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var table = new DataTable();
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString() ?? $"열{col}";
                table.Columns.Add(header);
            }

            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var dataRow = table.NewRow();
                for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                {
                    dataRow[col - 1] = worksheet.Cells[row, col].Value?.ToString() ?? string.Empty;
                }
                table.Rows.Add(dataRow);
            }

            _statementGrid.DataSource = table;
            _statusLabel.Text = $"거래처 마감내역 {table.Rows.Count}건을 불러왔습니다. 좌측 출고내역과 수기로 대조하세요.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "로드 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
