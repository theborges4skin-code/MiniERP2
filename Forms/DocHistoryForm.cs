using System.Diagnostics;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MiniERP2.Forms;

public class DocHistoryForm : Form
{
    private readonly DocHistoryRepository _repo;

    private DateTimePicker _dtFrom  = null!;
    private DateTimePicker _dtTo    = null!;
    private ComboBox _cmbDocType    = null!;
    private Button _btnSearch       = null!;
    private DataGridView _grid      = null!;
    private Button _btnOpenFile     = null!;
    private Button _btnDelete       = null!;
    private Button _btnExportExcel  = null!;
    private Label _lblStatus        = null!;

    private static readonly string[] DocTypeLabels =
        { "(전체)", "거래명세표(VAT별도)", "거래명세표(VAT포함)", "견적서(기본)", "견적서(수량형)", "가격조정명세서", "매출장" };

    private List<DocHistoryRecord> _records = [];

    public DocHistoryForm(DocHistoryRepository repo)
    {
        _repo = repo;
        InitializeComponent();
        OnSearchClick(null, EventArgs.Empty);
    }

    private void InitializeComponent()
    {
        Text = "문서 발행 이력";
        Size = new Size(900, 580);
        MinimumSize = new Size(700, 400);
        StartPosition = FormStartPosition.CenterParent;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(6, 6, 6, 0),
            FlowDirection = FlowDirection.LeftToRight,
        };

        top.Controls.Add(new Label { Text = "발행일:", AutoSize = true, Margin = new Padding(2, 6, 4, 0) });
        _dtFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        top.Controls.Add(_dtFrom);
        top.Controls.Add(new Label { Text = "~", AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
        _dtTo = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        top.Controls.Add(_dtTo);
        top.Controls.Add(DateRangeQuickSelect.CreateButton(_dtFrom, _dtTo));

        top.Controls.Add(new Label { Text = "문서종류:", AutoSize = true, Margin = new Padding(10, 6, 4, 0) });
        _cmbDocType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _cmbDocType.Items.AddRange(DocTypeLabels);
        _cmbDocType.SelectedIndex = 0;
        top.Controls.Add(_cmbDocType);

        _btnSearch = new Button { Text = "조회", Width = 70, Margin = new Padding(8, 2, 0, 0) };
        _btnSearch.Click += OnSearchClick;
        top.Controls.Add(_btnSearch);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id",         Visible = false });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DocType",    HeaderText = "문서종류", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "IssueDate",  HeaderText = "발행일",   FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BuyerName",  HeaderText = "공급받는자", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TotalAmount",HeaderText = "합계금액", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FilePath",   HeaderText = "파일경로", FillWeight = 38 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CreatedAt",  HeaderText = "저장시점", FillWeight = 10 });
        _grid.CellDoubleClick += OnGridDoubleClick;

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6, 6, 6, 0),
        };

        _btnExportExcel = new Button { Text = "엑셀로 내보내기", Width = 110, Margin = new Padding(4, 0, 0, 0) };
        _btnExportExcel.Click += OnExportExcelClick;
        bottom.Controls.Add(_btnExportExcel);

        _btnDelete = new Button { Text = "이력 삭제", Width = 80, Margin = new Padding(4, 0, 0, 0) };
        _btnDelete.Click += OnDeleteClick;
        bottom.Controls.Add(_btnDelete);

        _btnOpenFile = new Button { Text = "파일 열기", Width = 80, Margin = new Padding(4, 0, 0, 0) };
        _btnOpenFile.Click += OnOpenFileClick;
        bottom.Controls.Add(_btnOpenFile);

        _lblStatus = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0), ForeColor = Color.DimGray };
        bottom.Controls.Add(_lblStatus);

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    private void OnSearchClick(object? sender, EventArgs e)
    {
        string? docType = _cmbDocType.SelectedIndex > 0 ? DocTypeLabels[_cmbDocType.SelectedIndex] : null;
        _records = _repo.Query(_dtFrom.Value.Date, _dtTo.Value.Date, docType);
        PopulateGrid();
        _lblStatus.Text = $"총 {_records.Count}건";
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var r in _records)
        {
            _grid.Rows.Add(
                r.Id,
                r.DocType,
                r.IssueDate.ToString("yyyy-MM-dd"),
                r.BuyerName,
                r.TotalAmount.ToString("#,##0"),
                r.FilePath,
                r.CreatedAt.ToString("MM-dd HH:mm"));
        }
    }

    private DocHistoryRecord? SelectedRecord()
    {
        if (_grid.CurrentRow is not { Index: >= 0 } row) return null;
        int id = row.Cells["Id"].Value is int v ? v : 0;
        return _records.Find(r => r.Id == id);
    }

    private void OnGridDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        OpenSelected();
    }

    private void OnOpenFileClick(object? sender, EventArgs e) => OpenSelected();

    /// <summary>
    /// 원본 경로의 파일이 그대로 있으면 그걸 열고, 사용자가 파일을 옮기거나 지워서 없어졌으면
    /// 발행 시점에 DB에 함께 백업해둔 바이트(DocHistoryTable.FileBytes)를 임시 폴더에 복원해
    /// 대신 연다(사용자 신고 — 예전엔 FilePath에만 의존해 파일을 못 찾으면 그대로 실패했었음).
    /// FileBytes도 없으면(도입 이전 옛 이력) 기존처럼 안내만 하고 끝낸다.
    /// </summary>
    private void OpenSelected()
    {
        var r = SelectedRecord();
        if (r == null) return;

        if (File.Exists(r.FilePath))
        {
            Process.Start(new ProcessStartInfo(r.FilePath) { UseShellExecute = true });
            return;
        }

        var fileBytes = _repo.GetFileBytes(r.Id);
        if (fileBytes == null)
        {
            MessageBox.Show("파일을 찾을 수 없고, 이 이력에는 DB 백업본도 없습니다.\n" + r.FilePath, "파일 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var restoreDir = Path.Combine(Path.GetTempPath(), "MiniERP2_DocHistory");
            Directory.CreateDirectory(restoreDir);
            var restoredPath = Path.Combine(restoreDir, $"{Path.GetFileNameWithoutExtension(r.FilePath)}_{r.Id}.xlsx");
            File.WriteAllBytes(restoredPath, fileBytes);

            MessageBox.Show(
                "원본 파일을 찾을 수 없어, 발행 시점에 DB에 백업해둔 내용으로 복원해 엽니다.\n" +
                "필요하면 열린 문서를 '다른 이름으로 저장'해 원하는 위치에 다시 보관하세요.",
                "DB 백업본으로 복원", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo(restoredPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("백업본 복원에 실패했습니다.\n" + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        var r = SelectedRecord();
        if (r == null) return;
        if (MessageBox.Show($"이력을 삭제하시겠습니까?\n({r.DocType} | {r.IssueDate:yyyy-MM-dd} | {r.BuyerName})",
                "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _repo.Delete(r.Id);
        OnSearchClick(null, EventArgs.Empty);
    }

    private void OnExportExcelClick(object? sender, EventArgs e)
    {
        if (_records.Count == 0)
        {
            MessageBox.Show("내보낼 이력이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"문서이력_{DateTime.Now:yyyyMMdd}.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        try
        {
            ExportToExcel(_records, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show("내보내기 실패: " + ExportHelper.DescribeSaveError(ex), "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ExportToExcel(List<DocHistoryRecord> records, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("문서이력");

        string[] headers = { "문서종류", "발행일", "공급받는자", "합계금액", "파일경로", "저장시점" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cells[1, c + 1];
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        for (int r = 0; r < records.Count; r++)
        {
            var rec = records[r];
            ws.Cells[r + 2, 1].Value = rec.DocType;
            ws.Cells[r + 2, 2].Value = rec.IssueDate.ToString("yyyy-MM-dd");
            ws.Cells[r + 2, 3].Value = rec.BuyerName;
            ws.Cells[r + 2, 4].Value = (double)rec.TotalAmount;
            ws.Cells[r + 2, 4].Style.Numberformat.Format = "#,##0";
            ws.Cells[r + 2, 5].Value = rec.FilePath;
            ws.Cells[r + 2, 6].Value = rec.CreatedAt.ToString("yyyy-MM-dd HH:mm");
        }

        if (ws.Dimension != null)
            ws.Cells[ws.Dimension.Address].AutoFitColumns();

        ExportHelper.SaveExcel(package, filePath);
    }
}
