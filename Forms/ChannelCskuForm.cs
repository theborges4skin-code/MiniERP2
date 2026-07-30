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
/// 거래처(채널) 하나를 먼저 고른 뒤 그 채널이 취급하는 CSKU 전체를 한 번에 조회·수정하는 화면.
/// 기존 `CSkuForm`("채널 SKU 관리")은 반대 방향(마스터SKU 1개 → 그 SKU를 파는 여러 채널)이라,
/// "이 거래처가 취급하는 품목·단가를 쭉 보고 싶다"는 요구에는 맞지 않아 별도로 신설했다. 저장/삭제/
/// 이력조회/엑셀내보내기 로직은 CSkuForm과 동일한 ChannelSkuRepository를 그대로 재사용한다.
/// </summary>
public class ChannelCskuForm : Form
{
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly SettingsService _settingsService = new();

    private ComboBox _channelCombo = new();
    private ExcelLikeDataGridView _cskuGrid = new();
    private BindingList<ChannelSkuModel> _cskus = new();
    private Label _statusLabel = new();

    // "제조원가"는 ChannelSkuModel에 없는 필드(마스터SKU=ItemTable 소속 값)라 그리드 열에는 직접
    // 바인딩할 수 없다 — Msku 기준으로 옆에서 캐시해뒀다가 CellFormatting/저장 시 반영한다
    // (OutboundHistoryForm의 FreightCost 열과 같은 관례).
    private Dictionary<string, decimal> _costPriceByMsku = new();
    private readonly HashSet<string> _dirtyCostMskus = [];

    public ChannelCskuForm()
    {
        InitializeComponent();
        LoadChannelCombo();
    }

    private void InitializeComponent()
    {
        Text = "거래처별 CSKU 관리";
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            RowStyles = { new RowStyle(SizeType.Absolute, 40), new RowStyle(SizeType.Percent, 100) },
        };

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        _channelCombo = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(SalesChannel.ChannelName) };
        _channelCombo.SelectedIndexChanged += (s, e) => LoadData();

        var btnSave = new Button { Text = "저장", Size = new Size(90, 30) };
        btnSave.Click += OnSaveClick;
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };
        btnExport.Click += OnExportClick;

        toolStrip.Controls.Add(new Label { Text = "거래처:", AutoSize = true, Padding = new Padding(0, 7, 2, 0) });
        toolStrip.Controls.Add(_channelCombo);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnExport);
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.DarkGreen };
        toolStrip.Controls.Add(_statusLabel);

        _cskuGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "ChannelCskuForm.CskuGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        _cskuGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU 코드", DataPropertyName = "CskuCode", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "Msku", HeaderText = "마스터SKU", DataPropertyName = "Msku", Width = 130 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "제조원가", DataPropertyName = string.Empty, Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "InvoiceDisplayName", HeaderText = "송장표시명", DataPropertyName = "InvoiceDisplayName", Width = 180 },
            new DataGridViewTextBoxColumn { Name = "SupplyPrice", HeaderText = "납품가", DataPropertyName = "SupplyPrice", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "단위", DataPropertyName = "Unit", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Packing", HeaderText = "포장단위", DataPropertyName = "Packing", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "비고", DataPropertyName = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "UpdatedAt", HeaderText = "마지막 수정", DataPropertyName = "UpdatedAt", Width = 130, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm" } }
        );

        // 마스터SKU 칸 편집이 끝났을 때 CSKU 코드가 비어있으면 "채널명 앞 3글자_마스터SKU" 기본값을
        // 제안한다(CSkuForm의 ChannelCode 트리거와 같은 관례 — 여기선 채널이 이미 고정이라 Msku가 트리거).
        _cskuGrid.CellEndEdit += OnCskuGridCellEndEdit;
        _cskuGrid.CellFormatting += OnCskuGridCellFormatting;

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

        // TempSkuGenerator로 임시 등록된(예: "TEMP004") 마스터SKU를 실제 카탈로그 SKU로 정식
        // 교체하기 위한 진입점(§3 — "정식 등록").
        var assignMasterSkuItem = new ToolStripMenuItem("마스터SKU 지정/변경(정식 등록)...");
        assignMasterSkuItem.Click += OnAssignMasterSkuClick;

        _cskuGrid.ContextMenuStrip!.Items.Add(new ToolStripSeparator());
        _cskuGrid.ContextMenuStrip.Items.Add(historyMenuItem);
        _cskuGrid.ContextMenuStrip.Items.Add(assignMasterSkuItem);
        _cskuGrid.ContextMenuStrip.Opening += (s, e) =>
        {
            historyMenuItem.Enabled = _cskuGrid.SelectedRows.Count == 1;
            assignMasterSkuItem.Enabled = _cskuGrid.SelectedRows.Count == 1;
        };
    }

    private void OnAssignMasterSkuClick(object? sender, EventArgs e)
    {
        if (_cskuGrid.SelectedRows.Count != 1) return;
        var selectedRow = _cskuGrid.SelectedRows[0];
        if (selectedRow.IsNewRow) return;
        if (selectedRow.DataBoundItem is not ChannelSkuModel csku) return;

        using var dlg = new AssignMasterSkuDialog(csku.Msku, csku.InvoiceDisplayName);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedSku == null) return;

        csku.Msku = dlg.SelectedSku;
        _cskuGrid.InvalidateRow(selectedRow.Index);
        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"마스터SKU를 '{dlg.SelectedSku}'(으)로 지정했습니다. [저장]을 눌러야 반영됩니다.";
    }

    private void OnHistoryMenuItemClick(object? sender, EventArgs e)
    {
        if (_cskuGrid.SelectedRows.Count != 1) return;
        var selectedRow = _cskuGrid.SelectedRows[0];
        if (selectedRow.IsNewRow) return;

        if (selectedRow.DataBoundItem is not ChannelSkuModel csku || string.IsNullOrWhiteSpace(csku.CskuCode))
        {
            MessageBox.Show("CSKU 코드가 없는 항목은 이력을 조회할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var historyForm = new ChannelSkuHistoryForm(csku.ChannelCode, csku.CskuCode);
        FormManager.ApplyBoundsTracking(historyForm);
        historyForm.ShowDialog(this);
    }

    private void LoadChannelCombo()
    {
        var channels = _salesChannelRepository.GetAll().OrderBy(c => c.ChannelName).ToList();
        _channelCombo.DataSource = channels;
        if (channels.Count > 0) _channelCombo.SelectedIndex = 0;
        else LoadData();
    }

    private SalesChannel? SelectedChannel => _channelCombo.SelectedItem as SalesChannel;

    private void LoadData()
    {
        var channel = SelectedChannel;
        if (channel == null)
        {
            _cskus = new BindingList<ChannelSkuModel>();
            _cskuGrid.DataSource = _cskus;
            return;
        }

        _cskus = new BindingList<ChannelSkuModel>(_cskuRepository.GetAllByChannel(channel.ChannelCode));
        _cskuGrid.DataSource = _cskus;
        _dirtyCostMskus.Clear();
        _costPriceByMsku = _cskus
            .Select(c => c.Msku)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToDictionary(m => m, m => _itemRepository.GetBySku(m)?.CostPrice ?? 0m);
        _statusLabel.Text = $"{channel.ChannelName} — CSKU {_cskus.Count}건";
        _statusLabel.ForeColor = Color.DarkGreen;
    }

    private void OnCskuGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_cskuGrid.Columns[e.ColumnIndex].Name != "CostPrice") return;
        if (e.RowIndex < 0 || e.RowIndex >= _cskuGrid.Rows.Count) return;
        if (_cskuGrid.Rows[e.RowIndex].DataBoundItem is not ChannelSkuModel csku) return;

        // FormattingApplied=true는 e.Value가 "이미 완성된 표시용 문자열"일 때만 써야 한다 — 이
        // 열은 DefaultCellStyle.Format="N0"로 숫자 서식을 그리드가 대신 입혀야 하므로, 원시
        // decimal 값만 넣고 FormattingApplied는 켜지 않는다(켜면 FormatException 발생).
        e.Value = _costPriceByMsku.TryGetValue(csku.Msku, out var cost) ? cost : 0m;
    }

    private void OnCskuGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _cskuGrid.Rows.Count) return;
        if (_cskuGrid.Rows[e.RowIndex].DataBoundItem is not ChannelSkuModel csku) return;
        var columnName = _cskuGrid.Columns[e.ColumnIndex].Name;

        // "제조원가"는 ChannelSkuModel이 아니라 마스터SKU(ItemTable) 소속 값이라 저장 시점에 따로
        // 반영한다(OnSaveClick 참고) — 여기서는 캐시만 갱신.
        if (columnName == "CostPrice")
        {
            if (string.IsNullOrWhiteSpace(csku.Msku)) return;
            var raw = _cskuGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            _costPriceByMsku[csku.Msku] = decimal.TryParse(raw, out var parsed) ? parsed : 0m;
            _dirtyCostMskus.Add(csku.Msku);
            return;
        }

        if (columnName != "Msku") return;
        if (!string.IsNullOrWhiteSpace(csku.CskuCode) || string.IsNullOrWhiteSpace(csku.Msku)) return;

        var channel = SelectedChannel;
        if (channel == null) return;

        csku.CskuCode = CskuCodeGenerator.BuildDefault(channel.ChannelName, csku.Msku);
        _cskuGrid.InvalidateRow(e.RowIndex);
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var channel = SelectedChannel;
        if (channel == null)
        {
            MessageBox.Show("거래처를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            foreach (var csku in _cskus)
            {
                if (string.IsNullOrWhiteSpace(csku.Msku)) continue; // 마스터SKU 없이는 저장 대상이 아님(신규 빈 행 등)
                csku.ChannelCode = channel.ChannelCode;

                if (string.IsNullOrWhiteSpace(csku.CskuCode))
                    csku.CskuCode = CskuCodeGenerator.BuildDefault(channel.ChannelName, csku.Msku);

                _cskuRepository.Upsert(csku);
            }

            // 제조원가는 마스터SKU(ItemTable) 소속 값이라 CSKU Upsert와 별도로 반영한다. 아직 정식
            // 등록 안 된 임시 마스터SKU(예: TEMP004가 ItemTable에 없는 경우)는 저장할 곳이 없으므로
            // 건너뛰고 안내한다 — 먼저 [마스터SKU 지정/변경(정식 등록)]으로 실제 SKU에 연결해야 한다.
            var unregisteredMskus = new List<string>();
            foreach (var msku in _dirtyCostMskus)
            {
                var existing = _itemRepository.GetBySku(msku);
                if (existing == null) { unregisteredMskus.Add(msku); continue; }
                existing.CostPrice = _costPriceByMsku[msku];
                _itemRepository.Upsert(existing);
            }

            LoadData();
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"성공적으로 저장되었습니다. ({DateTime.Now:HH:mm:ss})";
            if (unregisteredMskus.Count > 0)
            {
                _statusLabel.ForeColor = Color.DarkOrange;
                _statusLabel.Text += $" (제조원가 미반영: {string.Join(", ", unregisteredMskus)} — 마스터SKU 미등록, [마스터SKU 지정/변경]으로 먼저 연결하세요)";
            }
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
        if (e.Row.DataBoundItem is not ChannelSkuModel csku || string.IsNullOrWhiteSpace(csku.CskuCode))
            return; // 아직 저장 안 된 행은 그리드에서만 지우면 됨.

        var result = MessageBox.Show(
            $"CSKU '{csku.CskuCode}'를 삭제하시겠습니까?\n관련된 모든 가격 변경 이력도 함께 삭제됩니다.",
            "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
            _cskuRepository.Delete(csku.ChannelCode, csku.CskuCode);
        else
            e.Cancel = true;
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        var channel = SelectedChannel;
        if (channel == null || _cskuGrid.Rows.Count == 0)
        {
            MessageBox.Show("내보낼 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"CSKU_{channel.ChannelName}_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("ChannelCskuExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("ChannelCskuExport", Path.GetDirectoryName(sfd.FileName)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("ChannelSKU");

            string[] headers = { "CSKU 코드", "마스터SKU", "송장표시명", "납품가", "단위", "포장단위", "비고" };
            for (var i = 0; i < headers.Length; i++) worksheet.Cells[1, i + 1].Value = headers[i];

            for (var i = 0; i < _cskus.Count; i++)
            {
                var csku = _cskus[i];
                var row = i + 2;
                worksheet.Cells[row, 1].Value = csku.CskuCode;
                worksheet.Cells[row, 2].Value = csku.Msku;
                worksheet.Cells[row, 3].Value = csku.InvoiceDisplayName;
                worksheet.Cells[row, 4].Value = (double)csku.SupplyPrice;
                worksheet.Cells[row, 5].Value = csku.Unit;
                worksheet.Cells[row, 6].Value = csku.Packing;
                worksheet.Cells[row, 7].Value = csku.Note;
            }
            if (worksheet.Dimension != null) worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            ExportHelper.SaveExcel(package, sfd.FileName);
            ExportHelper.ShowPostExportDialog(this, sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
