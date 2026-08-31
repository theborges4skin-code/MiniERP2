using System.ComponentModel;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §2)에서 OFS를 경유하지 않은 거래내역을 엑셀로
/// 한 번에 올리기 위한 창. <see cref="PartnerManualOrderDialog"/>(1건씩 수동 입력)의 일괄 버전이다.
/// 엑셀은 매출일/수량/CSKU/단가(VAT포함) 4개 헤더만 있으면 되고(<see cref="PartnerBulkOrderLoader"/>),
/// CSKU는 이 채널에 이미 등록된 것을 자동으로 찾아 품목명을 채운다. 등록되지 않은 CSKU가 있으면
/// 안내 후 <see cref="NewChannelCskuDialog"/>로 바로 등록할 수 있게 하고, 등록을 건너뛴 CSKU에
/// 걸린 행은 제외한 채 나머지 정상 행만 확정할 수 있다(사용자 확인 — 전체가 문제없어야만 등록을
/// 허용하는 대신, 정상 행만 우선 등록하고 나머지는 나중에 다시 올리는 쪽을 선택).
/// </summary>
public class PartnerBulkOrderImportDialog : Form
{
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly OutboundRepository _outboundRepo = new();
    private readonly string _channelCode;
    private readonly string _channelName;

    private readonly BindingList<PartnerBulkOrderPreviewRow> _rows = [];
    private readonly DataGridView _grid = new();
    private readonly Label _summaryLabel = new() { AutoSize = true };
    private readonly Button _btnCommit = new() { Text = "확정", Size = new Size(90, 30), Enabled = false };

    public int ImportedCount { get; private set; }
    public DateTime? LatestSaleDate { get; private set; }

    public PartnerBulkOrderImportDialog(string channelCode, string channelName)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = $"엑셀 일괄 추가 — {_channelName}";
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var topPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 8, 0, 0) };
        topPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Padding = new Padding(0, 6, 15, 0),
            Text = $"필요 컬럼: {PartnerBulkOrderLoader.SaleDateHeader} · {PartnerBulkOrderLoader.QtyHeader} · {PartnerBulkOrderLoader.CskuHeader} · {PartnerBulkOrderLoader.UnitPriceHeader}(VAT포함)",
        });
        var btnDownloadTemplate = new Button { Text = "엑셀 양식 다운로드", Size = new Size(120, 28) };
        btnDownloadTemplate.Click += OnDownloadTemplateClick;
        topPanel.Controls.Add(btnDownloadTemplate);

        var btnSelectFile = new Button { Text = "파일 선택...", Size = new Size(100, 28) };
        btnSelectFile.Click += OnSelectFileClick;
        topPanel.Controls.Add(btnSelectFile);

        BuildGrid();

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(10) };
        var btnClose = new Button { Text = "닫기", Size = new Size(90, 30) };
        btnClose.Click += (s, e) => Close();
        _btnCommit.Click += OnCommitClick;
        bottomPanel.Controls.Add(btnClose);
        bottomPanel.Controls.Add(_btnCommit);
        bottomPanel.Controls.Add(_summaryLabel);

        Controls.Add(_grid);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);
        CancelButton = btnClose;
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _rows;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "RowNumber", HeaderText = "행", DataPropertyName = "RowNumber", Width = 45 },
            new DataGridViewTextBoxColumn { Name = "SaleDateText", HeaderText = "매출일", DataPropertyName = "SaleDateText", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU", DataPropertyName = "CskuCode", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "품목명", DataPropertyName = "ItemName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "수량", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "단가(VAT포함)", DataPropertyName = "UnitPrice", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0" } },
            new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "상태", DataPropertyName = "Status", Width = 160 });
    }

    /// <summary>필요한 4개 헤더(매출일/수량/CSKU/단가)와 예시 1행을 담은 빈 양식을 저장한다.</summary>
    private void OnDownloadTemplateClick(object? sender, EventArgs e)
    {
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            "거래내역_일괄등록_양식.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        try
        {
            ExcelLicense.Ensure();

            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("거래내역");

            var headers = new[]
            {
                PartnerBulkOrderLoader.SaleDateHeader,
                PartnerBulkOrderLoader.QtyHeader,
                PartnerBulkOrderLoader.CskuHeader,
                PartnerBulkOrderLoader.UnitPriceHeader,
            };
            for (var col = 0; col < headers.Length; col++) sheet.Cells[1, col + 1].Value = headers[col];
            sheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
            sheet.View.FreezePanes(2, 1);

            // 예시행 — 날짜 형식/단가가 VAT포함이라는 점을 보여주는 참고용. 그대로 지우고 써도 된다.
            sheet.Cells[2, 1].Value = DateTime.Today.ToString("yyyy-MM-dd");
            sheet.Cells[2, 2].Value = 1;
            sheet.Cells[2, 3].Value = "예시CSKU코드";
            sheet.Cells[2, 4].Value = 10000;
            sheet.Cells[4, 1].Value = "※ 위 예시행(2행)은 참고용입니다. 실제 데이터로 바꾸거나 지우고 사용하세요. 단가는 VAT포함 금액입니다.";

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();

            ExportHelper.SaveExcel(package, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"양식 저장 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSelectFileClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls", Title = "일괄 등록할 거래내역 엑셀을 선택하세요" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        List<PartnerBulkOrderRow> loaded;
        try
        {
            loaded = PartnerBulkOrderLoader.Load(ofd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _rows.Clear();
        foreach (var row in loaded) _rows.Add(new PartnerBulkOrderPreviewRow(row));
        ResolveMapping();
        OfferToRegisterMissingCskus();
        RefreshSummary();
    }

    /// <summary>각 행의 CSKU를 채널에 등록된 CSKU와 대조해 품목명/상태를 채운다. 파싱 오류가 있던
    /// 행은 그 오류를 상태로 그대로 보여준다.</summary>
    private void ResolveMapping()
    {
        foreach (var row in _rows)
        {
            if (row.Source.Errors.Count > 0)
            {
                row.Status = string.Join("; ", row.Source.Errors);
                row.ItemName = "";
                continue;
            }

            var csku = _cskuRepository.GetByChannelAndCskuCode(_channelCode, row.CskuCode);
            if (csku == null)
            {
                row.Status = "CSKU 미등록";
                row.ItemName = "";
            }
            else
            {
                row.ItemName = string.IsNullOrWhiteSpace(csku.InvoiceDisplayName) ? row.CskuCode : csku.InvoiceDisplayName;
                row.Status = "정상";
            }
        }
        _rows.ResetBindings();
    }

    private void OfferToRegisterMissingCskus()
    {
        var missingCodes = _rows.Where(r => r.Status == "CSKU 미등록").Select(r => r.CskuCode).Distinct().ToList();
        if (missingCodes.Count == 0) return;

        var affectedRowCount = _rows.Count(r => r.Status == "CSKU 미등록");
        var confirm = MessageBox.Show(
            $"이 채널에 등록되지 않은 CSKU가 {missingCodes.Count}종({affectedRowCount}행)입니다:\n{string.Join(", ", missingCodes)}\n\n지금 등록하시겠습니까?\n(건너뛰면 해당 행은 확정 대상에서 제외됩니다.)",
            "미등록 CSKU 안내", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        foreach (var code in missingCodes)
        {
            using var dlg = new NewChannelCskuDialog(_channelCode, _channelName, suggestedCskuCode: code);
            if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.SelectedMasterSku == null) continue;

            _cskuRepository.Upsert(new ChannelSkuModel
            {
                ChannelCode = _channelCode,
                CskuCode = dlg.CskuCode,
                Msku = dlg.SelectedMasterSku,
                SupplyPrice = dlg.SupplyPrice,
                InvoiceDisplayName = dlg.InvoiceDisplayName,
                Unit = dlg.Unit,
                Packing = dlg.Packing,
                Note = dlg.Note,
            });

            foreach (var row in _rows.Where(r => r.CskuCode == code))
            {
                row.ItemName = string.IsNullOrWhiteSpace(dlg.InvoiceDisplayName) ? row.CskuCode : dlg.InvoiceDisplayName;
                row.Status = "정상";
            }
        }
        _rows.ResetBindings();
    }

    private void RefreshSummary()
    {
        var validCount = _rows.Count(r => r.Status == "정상");
        var excludedCount = _rows.Count - validCount;
        _summaryLabel.Text = $"정상 {validCount}건 / 제외 {excludedCount}건";
        _btnCommit.Enabled = validCount > 0;
    }

    private void OnCommitClick(object? sender, EventArgs e)
    {
        var validRows = _rows.Where(r => r.Status == "정상").ToList();
        var excludedCount = _rows.Count - validRows.Count;
        var confirmMessage = excludedCount > 0
            ? $"정상 {validRows.Count}건을 등록합니다(오류/미등록 {excludedCount}건은 제외). 계속하시겠습니까?"
            : $"{validRows.Count}건을 등록합니다. 계속하시겠습니까?";
        if (MessageBox.Show(confirmMessage, "확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        var details = validRows.Select(r => new OutboundDetail
        {
            ChannelCode = _channelCode,
            MskuCode = r.CskuCode,
            CskuCode = r.CskuCode,
            Qty = r.Qty,
            SupplyPrice = r.UnitPrice,
            ProductName = r.ItemName,
            Remark = "엑셀 일괄입력(거래처 마감보드)",
            ConfirmedAt = r.Source.SaleDate,
        }).ToList();
        _outboundRepo.AddManualEntries(details);

        ImportedCount = details.Count;
        LatestSaleDate = details.Max(d => d.ConfirmedAt);
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>미리보기 그리드 바인딩용 뷰모델. <see cref="PartnerBulkOrderRow"/>를 감싸 CSKU 매핑
/// 결과(품목명/상태)를 덧붙인다.</summary>
public class PartnerBulkOrderPreviewRow(PartnerBulkOrderRow source)
{
    public PartnerBulkOrderRow Source { get; } = source;
    public int RowNumber => Source.RowNumber;
    public string SaleDateText => Source.SaleDate?.ToString("yyyy-MM-dd") ?? "";
    public string CskuCode => Source.CskuCode;
    public int Qty => Source.Qty;
    public decimal UnitPrice => Source.UnitPrice;
    public string ItemName { get; set; } = "";
    public string Status { get; set; } = "";
}
