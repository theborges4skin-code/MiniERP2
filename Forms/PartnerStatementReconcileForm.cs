using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감자료 대조. 거래처가 보낸 마감자료 파일과 우리 마감월 출고이력(마감보드와 같은 귀속월 기준)을
/// CSKU × 수량으로 짝지어 일치/단가차이/거래처만/우리만/품명 미연결을 보여준다. 거래처 품명은 처음
/// 한 번 CSKU에 연결해 두면(PartnerItemNameMapTable) 다음 달부터 자동이다. 판정은 <see cref="PartnerStatementReconcileEngine"/>.
/// </summary>
public class PartnerStatementReconcileForm : Form
{
    private const string FileFolderKeyPrefix = "PartnerStatementReconcile.Folder.";

    private readonly string _channelCode;
    private readonly string _channelName;
    private readonly string _period;
    private readonly SettingsService _settings = new();
    private readonly OutboundRepository _outboundRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly PartnerItemNameMapRepository _nameMapRepo = new();

    private readonly Label _fileLabel = new() { AutoSize = true, Padding = new Padding(4, 6, 0, 0), Text = "마감자료: (없음)" };
    private readonly CheckBox _diffOnlyCheck = new() { Text = "차이만 보기", AutoSize = true, Checked = true, Padding = new Padding(12, 4, 0, 0) };
    private readonly Label _summaryLabel = new() { AutoSize = true, Padding = new Padding(4, 4, 4, 4) };
    private readonly ExcelLikeDataGridView _grid = new();
    private string? _filePath;
    private ReconcileResult? _result;

    public PartnerStatementReconcileForm(string channelCode, string channelName, string period)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        _period = period;
        Text = $"거래처 마감자료 대조 — {channelName} / {period}";
        Size = new Size(1300, 760);
        StartPosition = FormStartPosition.CenterParent;
        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var pick = new Button { Text = "마감자료 파일...", AutoSize = true };
        pick.Click += (s, e) => PickFile();
        var run = new Button { Text = "대조", Width = 80, Font = new Font(Font, FontStyle.Bold) };
        run.Click += (s, e) => RunReconcile();
        _diffOnlyCheck.CheckedChanged += (s, e) => BindGrid();
        top.Controls.AddRange([pick, _fileLabel, run, _diffOnlyCheck]);
        root.Controls.Add(top);
        root.Controls.Add(_summaryLabel);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        DataGridViewTextBoxColumn T(string prop, string header, int width, string? format = null) => new()
        {
            DataPropertyName = prop, Name = prop, HeaderText = header, Width = width,
            DefaultCellStyle = format == null ? new DataGridViewCellStyle()
                : new DataGridViewCellStyle { Format = format, Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        _grid.Columns.AddRange(
            T("Kind", "판정", 85), T("PartnerDateText", "거래처 일자", 70), T("OurDateText", "우리 출고일", 70),
            T("Recipient", "수령인", 65), T("PartnerItemName", "거래처 품명", 260), T("CskuCode", "CSKU", 150),
            T("OurItemName", "우리 품명", 220), T("Qty", "수량", 45, "N0"),
            T("PartnerUnitPrice", "거래처 단가(VAT포함)", 85, "N0"), T("OurUnitPrice", "우리 단가", 70, "N0"),
            T("PartnerAmount", "거래처 금액", 80, "N0"), T("OurAmount", "우리 금액", 80, "N0"));
        _grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not ReconcileRow r) return;
            e.CellStyle!.ForeColor = r.Kind switch
            {
                ReconcileRow.KindMatched => e.CellStyle.ForeColor,
                ReconcileRow.KindPriceDiff => Color.DarkOrange,
                ReconcileRow.KindUnmapped => Color.MediumPurple,
                _ => Color.IndianRed,
            };
        };
        root.Controls.Add(_grid);

        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var link = new Button { Text = "선택 행 거래처 품명 → CSKU 연결", AutoSize = true };
        link.Click += (s, e) => LinkSelectedNames();
        var export = new Button { Text = "엑셀 저장", AutoSize = true };
        export.Click += (s, e) => Export();
        bottom.Controls.AddRange([link, export]);
        root.Controls.Add(bottom);

        Controls.Add(root);
    }

    private void PickFile()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls|All files (*.*)|*.*",
            Title = $"{_channelName} 마감자료 파일을 선택하세요",
            InitialDirectory = _settings.GetLastFolder(FileFolderKeyPrefix + _channelCode) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settings.SetLastFolder(FileFolderKeyPrefix + _channelCode, Path.GetDirectoryName(ofd.FileName)!);
        _filePath = ofd.FileName;
        _fileLabel.Text = $"마감자료: {Path.GetFileName(_filePath)}";
        RunReconcile();
    }

    private void RunReconcile()
    {
        if (_filePath == null) { PickFile(); return; }
        try
        {
            ExcelLicense.Ensure();
            using var package = ExcelFileOpener.OpenWithPasswordPrompt(_filePath, this);
            if (package == null) return;
            var parsed = PartnerStatementReconcileEngine.Parse(package.Workbook.Worksheets.First(), _period);
            if (parsed.Error != null)
            {
                MessageBox.Show(this, parsed.Error, "마감자료 읽기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var cskus = _channelSkuRepo.GetAllByChannel(_channelCode);
            var shippingCskus = cskus.Where(c => c.Msku == "shipping" || (c.InvoiceDisplayName ?? "").Contains("배송비"))
                .Select(c => c.CskuCode).ToHashSet(StringComparer.Ordinal);
            var ourLines = _outboundRepo.GetForClosingPeriod(_channelCode, _period);

            static string CskuOf(OutboundDetail d) => string.IsNullOrWhiteSpace(d.CskuCode) ? d.MskuCode : d.CskuCode!;
            var (from, _) = PartnerShipmentBackfillEngine.PeriodRange(_period);
            var known = cskus.Where(c => !string.IsNullOrWhiteSpace(c.InvoiceDisplayName)).Select(c => (c.InvoiceDisplayName!, c.CskuCode))
                .Concat(ourLines.Select(d => (d.ProductName, CskuOf(d))))
                .Concat(_outboundRepo.GetByChannel(_channelCode, from.AddYears(-1), from.AddMonths(1)).Select(d => (d.ProductName, CskuOf(d))))
                .Where(k => !string.IsNullOrWhiteSpace(k.Item1) && !string.IsNullOrWhiteSpace(k.Item2) && !shippingCskus.Contains(k.Item2))
                .Distinct()
                .ToList();

            var saved = _nameMapRepo.GetByChannel(_channelCode);
            foreach (var line in parsed.Lines.Where(l => !l.IsShipping))
            {
                if (saved.TryGetValue(line.ItemName, out var mapped)) { line.CskuCode = mapped; line.CskuFromSavedMap = true; }
                else line.CskuCode = PartnerStatementReconcileEngine.GuessCsku(line.ItemName, known) ?? "";
            }

            _result = PartnerStatementReconcileEngine.Reconcile(parsed.Lines, ourLines, shippingCskus.Contains);
            ShowSummary();
            BindGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "대조 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSummary()
    {
        if (_result is not { } r) return;
        int Count(string kind) => r.Rows.Count(x => x.Kind == kind);
        decimal Qty(string kind) => r.Rows.Where(x => x.Kind == kind).Sum(x => x.Qty);
        _summaryLabel.Text =
            $"상품(VAT포함) — 거래처 {r.PartnerQty:N0}개 {r.PartnerAmount:N0}원 / 우리 {r.OurQty:N0}개 {r.OurAmount:N0}원 / 차액 {r.PartnerAmount - r.OurAmount:N0}원\n" +
            $"배송 — 거래처 {r.PartnerShippingCount:N0}건 {r.PartnerShippingAmount:N0}원 / 우리 송장 {r.OurShipmentCount:N0}건" +
            (r.OurShippingLineQty > 0 ? $"(배송비 라인 {r.OurShippingLineQty:N0})" : "") + "\n" +
            $"일치 {Count(ReconcileRow.KindMatched)}줄 · 단가차이 {Count(ReconcileRow.KindPriceDiff)}줄 · 거래처만 {Qty(ReconcileRow.KindPartnerOnly):N0}개 · " +
            $"우리만 {Qty(ReconcileRow.KindOursOnly):N0}개 · 품명 미연결 {Count(ReconcileRow.KindUnmapped)}줄";
    }

    private void BindGrid()
    {
        if (_result == null) return;
        var rows = _diffOnlyCheck.Checked ? _result.Rows.Where(r => r.IsDifference).ToList() : _result.Rows;
        _grid.DataSource = rows.ToList();
    }

    /// <summary>선택한 행들의 거래처 품명을 고른 CSKU에 연결해 저장하고 다시 대조한다.</summary>
    private void LinkSelectedNames()
    {
        var names = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ReconcileRow>()
            .Select(r => r.PartnerItemName).Where(n => n.Length > 0).Distinct().ToList();
        if (names.Count == 0)
        {
            MessageBox.Show(this, "거래처 품명이 있는 행(품명 미연결·거래처만 등)을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new CskuPickerDialog(_channelCode);
        if (FormManager.ShowDialogSafe(picker, this) != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedCskuCode)) return;
        if (picker.SelectedChannelCode != null && picker.SelectedChannelCode != _channelCode)
        {
            MessageBox.Show(this, $"'{_channelName}' 채널의 CSKU를 고르세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var n in names) _nameMapRepo.Upsert(_channelCode, n, picker.SelectedCskuCode!);
        RunReconcile();
    }

    private void Export()
    {
        if (_result == null) return;
        var path = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"마감자료대조_{_channelName}_{_period.Replace("-", "")}.xlsx", _settings.GetLastFolder(FileFolderKeyPrefix + _channelCode) ?? "");
        if (path == null) return;
        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("대조");
            sheet.Cells[1, 1].Value = _summaryLabel.Text.Replace("\n", " | ");
            string[] headers = ["판정", "거래처 일자", "우리 출고일", "수령인", "거래처 품명", "CSKU", "우리 품명", "수량", "거래처 단가(VAT포함)", "우리 단가", "거래처 금액", "우리 금액"];
            for (int i = 0; i < headers.Length; i++) sheet.Cells[3, i + 1].Value = headers[i];
            int row = 4;
            foreach (var r in _result.Rows)
            {
                object?[] values = [r.Kind, r.PartnerDateText, r.OurDateText, r.Recipient, r.PartnerItemName, r.CskuCode, r.OurItemName,
                    r.Qty, r.PartnerUnitPrice, r.OurUnitPrice, r.PartnerAmount, r.OurAmount];
                for (int i = 0; i < values.Length; i++) sheet.Cells[row, i + 1].Value = values[i];
                row++;
            }
            sheet.Cells[3, 1, row, headers.Length].AutoFitColumns(8, 60);
            ExportHelper.SaveExcel(package, path);
            ExportHelper.ShowPostExportDialog(this, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ExportHelper.DescribeSaveError(ex), "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
