using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 특정 마스터 SKU에 연결된 채널별 SKU(CSKU)를 관리하는 폼입니다.
/// </summary>
public class CSkuForm : Form
{
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly SettingsService _settingsService = new();
    private readonly string _masterSku;
    private ExcelLikeDataGridView _cskuGrid = new();
    private BindingList<ChannelSkuModel> _cskus = new();
    private Label _statusLabel = new();

    public CSkuForm(string masterSku)
    {
        _masterSku = masterSku;
        InitializeComponent();
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = $"채널 SKU 관리 - {_masterSku}";
        Size = new Size(700, 500);
        StartPosition = FormStartPosition.CenterParent;

        // Enable drag-and-drop functionality
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            RowStyles = { new RowStyle(SizeType.Absolute, 40), new RowStyle(SizeType.Percent, 100) }
        };

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnSave = new Button { Text = "저장", Size = new Size(100, 30) };
        btnSave.Click += OnSaveClick;
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };
        btnExport.Click += OnExportClick;

        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnExport);
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.DarkGreen };
        toolStrip.Controls.Add(_statusLabel);

        _cskuGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "CSkuForm.CskuGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };

        _cskuGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ChannelCode", HeaderText = "채널 코드", DataPropertyName = "ChannelCode", Width = 130 },
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU 코드", DataPropertyName = "CskuCode", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "InvoiceDisplayName", HeaderText = "송장표시명", DataPropertyName = "InvoiceDisplayName", Width = 180 },
            new DataGridViewTextBoxColumn { Name = "SupplyPrice", HeaderText = "납품가", DataPropertyName = "SupplyPrice", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "단위", DataPropertyName = "Unit", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Packing", HeaderText = "포장단위", DataPropertyName = "Packing", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "비고", DataPropertyName = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "UpdatedAt", HeaderText = "마지막 수정", DataPropertyName = "UpdatedAt", Width = 130, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm" } }
        );

        // 같은 채널 안에서도 옵션별로 CSKU 코드를 다르게 해야 할 수 있으므로, 채널 코드를 입력/수정하면
        // CSKU 코드가 비어있는 경우에만 "채널명 앞 3글자_마스터SKU" 기본값을 채워준다(직접 수정 가능).
        _cskuGrid.CellEndEdit += OnCskuGridCellEndEdit;

        SetupContextMenu();

        _cskuGrid.UserDeletingRow += OnUserDeletingRow;

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_cskuGrid, 0, 1);
        Controls.Add(mainLayout);

        FormClosing += (s, e) => _cskuGrid.SaveLayout();
    }

    private void SetupContextMenu()
    {
        var historyMenuItem = new ToolStripMenuItem("변경 이력 보기(&H)");
        historyMenuItem.Click += OnHistoryMenuItemClick;

        _cskuGrid.ContextMenuStrip!.Items.Add(new ToolStripSeparator());
        _cskuGrid.ContextMenuStrip.Items.Add(historyMenuItem);

        // Enable the menu item only when a single row is selected.
        _cskuGrid.ContextMenuStrip.Opening += (s, e) =>
        {
            historyMenuItem.Enabled = _cskuGrid.SelectedRows.Count == 1;
        };
    }

    private void OnHistoryMenuItemClick(object? sender, EventArgs e)
    {
        if (_cskuGrid.SelectedRows.Count != 1) return;

        var selectedRow = _cskuGrid.SelectedRows[0];
        if (selectedRow.IsNewRow) return;

        var csku = selectedRow.DataBoundItem as ChannelSkuModel;
        if (csku == null || string.IsNullOrWhiteSpace(csku.ChannelCode))
        {
            MessageBox.Show("채널 코드가 없는 항목은 이력을 조회할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Open the history form as a dialog.
        using var historyForm = new ChannelSkuHistoryForm(csku.ChannelCode, csku.CskuCode);
        FormManager.ApplyBoundsTracking(historyForm);
        historyForm.ShowDialog(this);
    }

    /// <summary>
    /// 채널 코드 칸 편집이 끝났을 때, CSKU 코드가 비어있으면 "채널명 앞 3글자_마스터SKU" 기본값을
    /// 제안해준다. 이미 값이 있으면(직접 입력했거나 기존 데이터) 건드리지 않는다.
    /// </summary>
    private void OnCskuGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_cskuGrid.Columns[e.ColumnIndex].Name != "ChannelCode") return;
        if (e.RowIndex < 0 || e.RowIndex >= _cskuGrid.Rows.Count) return;
        if (_cskuGrid.Rows[e.RowIndex].DataBoundItem is not ChannelSkuModel csku) return;
        if (!string.IsNullOrWhiteSpace(csku.CskuCode) || string.IsNullOrWhiteSpace(csku.ChannelCode)) return;

        var channelName = _salesChannelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == csku.ChannelCode)?.ChannelName ?? csku.ChannelCode;
        csku.CskuCode = CskuCodeGenerator.BuildDefault(channelName, _masterSku);
        _cskuGrid.InvalidateRow(e.RowIndex);
    }

    private void LoadData()
    {
        var allCskus = _cskuRepository.GetAllByMsku(_masterSku);
        _cskus = new BindingList<ChannelSkuModel>(allCskus);
        _cskuGrid.DataSource = _cskus;
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        try
        {
            foreach (var csku in _cskus)
            {
                if (string.IsNullOrWhiteSpace(csku.ChannelCode)) continue;

                // 새 행에 마스터 SKU가 설정되었는지 확인하고 설정합니다.
                if (string.IsNullOrWhiteSpace(csku.Msku))
                {
                    csku.Msku = _masterSku;
                }

                // CSKU 코드를 직접 입력하지 않은 행은 저장 시점에 기본값을 채운다(편집 중 놓친 경우 대비).
                if (string.IsNullOrWhiteSpace(csku.CskuCode))
                {
                    var channelName = _salesChannelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == csku.ChannelCode)?.ChannelName ?? csku.ChannelCode;
                    csku.CskuCode = CskuCodeGenerator.BuildDefault(channelName, _masterSku);
                }

                _cskuRepository.Upsert(csku);
            }
            LoadData();
            // 2026-06-28 점검: 그리드를 다시 불러온 직후 모달을 띄우는 패턴이 다른 화면들에서
            // 반복 재현됐던 경쟁 상태와 같은 위험군이라 비모달 라벨로 대체.
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"성공적으로 저장되었습니다. ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = $"저장 중 오류가 발생했습니다: {ex.Message}";
        }
    }

    private void OnUserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
    {
        if (e.Row is null || e.Row.IsNewRow) return;

        var cskuToDelete = e.Row.DataBoundItem as ChannelSkuModel;
        if (cskuToDelete == null || string.IsNullOrWhiteSpace(cskuToDelete.ChannelCode))
        {
            // If the row is not yet saved to the DB, just allow UI deletion.
            return;
        }

        var result = MessageBox.Show(
            $"채널 '{cskuToDelete.ChannelCode}'의 SKU 매핑을 삭제하시겠습니까?\n관련된 모든 가격 변경 이력도 함께 삭제됩니다.",
            "삭제 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            _cskuRepository.Delete(cskuToDelete.ChannelCode, cskuToDelete.CskuCode);
        }
        else
        {
            e.Cancel = true;
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        // Check if the dragged data is a file.
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // Allow only a single file.
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length == 1 && Path.GetExtension(files[0]).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                e.Effect = DragDropEffects.Copy; // Show copy cursor.
            }
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var files = (string[])e.Data!.GetData(DataFormats.FileDrop)!;
        var filePath = files[0];

        try
        {
            using var package = ExcelFileOpener.OpenWithPasswordPrompt(filePath, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                MessageBox.Show("The Excel file does not contain any worksheets.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var cskusToImport = new List<ChannelSkuModel>();
            // Start from row 2 to skip the header.
            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var channelCode = worksheet.Cells[row, 1].Value?.ToString();
                if (string.IsNullOrWhiteSpace(channelCode)) continue;

                if (!decimal.TryParse(worksheet.Cells[row, 2].Value?.ToString(), out var supplyPrice))
                {
                    supplyPrice = 0; // Default to 0 if parsing fails.
                }

                var channelName = _salesChannelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == channelCode)?.ChannelName ?? channelCode;
                cskusToImport.Add(new ChannelSkuModel
                {
                    ChannelCode = channelCode,
                    CskuCode = CskuCodeGenerator.BuildDefault(channelName, _masterSku),
                    Msku = _masterSku,
                    SupplyPrice = supplyPrice,
                });
            }

            if (cskusToImport.Count == 0)
            {
                MessageBox.Show("No valid data to import.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show($"Read {cskusToImport.Count} items. Do you want to apply them to the database?", "Confirm Import", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                foreach (var csku in cskusToImport)
                {
                    _cskuRepository.Upsert(csku);
                }
                MessageBox.Show("Data has been successfully applied.", "Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadData(); // Refresh the grid.
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"An error occurred while reading the Excel file.\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        if (_cskuGrid.Rows.Count == 0)
        {
            MessageBox.Show("내보낼 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"CSKU_{_masterSku}_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("CSkuExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("CSkuExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("ChannelSKU");

            var visibleColumns = _cskuGrid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            for (var i = 0; i < visibleColumns.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = visibleColumns[i].HeaderText;
            }

            for (var rowIndex = 0; rowIndex < _cskus.Count; rowIndex++)
            {
                var csku = _cskus[rowIndex];
                // 그리드의 열 순서(ChannelCode, CskuCode, InvoiceDisplayName, SupplyPrice)와 맞춘다.
                worksheet.Cells[rowIndex + 2, 1].Value = csku.ChannelCode;
                worksheet.Cells[rowIndex + 2, 2].Value = csku.CskuCode;
                worksheet.Cells[rowIndex + 2, 3].Value = csku.InvoiceDisplayName;
                worksheet.Cells[rowIndex + 2, 4].Value = csku.SupplyPrice;
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            ExportHelper.SaveExcel(package, filePath);

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}