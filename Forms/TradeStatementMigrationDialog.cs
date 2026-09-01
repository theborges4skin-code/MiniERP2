using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Migration;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 레거시 엑셀 거래명세표를 스캔 → 검토(포함/제외 체크) → 커밋 3단계로 DB에 적재하는 창.
/// 이 앱의 다른 "레거시 가져오기" 기능들과 달리 확인창 없이 즉시 커밋하지 않는다 — 463개 시트
/// 규모라 사람이 훑어보고 결정할 그리드가 필요해서(거래명세표_DB이식_개발스펙.md §3.2 3-패스 원칙).
/// </summary>
public class TradeStatementMigrationDialog : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly LegacyStatementCommitService _commitService = new();

    private static readonly HashSet<string> DefaultExcludeFlags = new()
    {
        "NOISE_NO_HEADER", "NOISE_EMPTY_SHEET", "NOISE_BLANK_SHEET_NAME", "DISCARDED"
    };

    private TextBox _folderBox = new();
    private Label _fileCountLabel = new();
    private DataGridView _grid = new();
    private Label _statusLabel = new();
    private Button _btnScan = new();
    private Button _btnCommit = new();

    private List<string> _matchedFiles = new();
    private readonly List<ParsedStatementSheet> _scannedSheets = new();

    public TradeStatementMigrationDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "거래명세표(엑셀) 마이그레이션";
        Size = new Size(1000, 700);
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterParent;

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        main.Controls.Add(BuildTopBar(), 0, 0);
        main.Controls.Add(BuildGrid(), 0, 1);
        main.Controls.Add(BuildBottomBar(), 0, 2);
        Controls.Add(main);
    }

    private Control BuildTopBar()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var btnFolder = new Button { Text = "폴더 선택...", Width = 100, Height = 30 };
        btnFolder.Click += OnSelectFolderClick;
        _folderBox = new TextBox { Width = 420, ReadOnly = true };
        _fileCountLabel = new Label { AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
        _btnScan = new Button { Text = "스캔", Width = 90, Height = 30, Enabled = false };
        _btnScan.Click += OnScanClick;

        flow.Controls.AddRange(new Control[] { btnFolder, _folderBox, _fileCountLabel, _btnScan });
        return flow;
    }

    private Control BuildGrid()
    {
        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Include",
            HeaderText = "포함",
            Width = 45,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
        });
        _grid.Columns.Add("FileName", "파일명");
        _grid.Columns.Add("SheetName", "시트명");
        _grid.Columns.Add("Party", "거래처");
        _grid.Columns.Add("IssueDate", "발행일");
        _grid.Columns.Add("Signature", "시그니처");
        _grid.Columns.Add("TotalsStatus", "총계상태");
        _grid.Columns.Add("Flags", "플래그");
        return _grid;
    }

    private Control BuildBottomBar()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(0, 8, 16, 0) };
        _btnCommit = new Button { Text = "커밋", Width = 90, Height = 30, Enabled = false };
        _btnCommit.Click += OnCommitClick;
        var btnClose = new Button { Text = "닫기", Width = 90, Height = 30 };
        btnClose.Click += (s, e) => Close();

        flow.Controls.AddRange(new Control[] { _statusLabel, _btnCommit, btnClose });
        return flow;
    }

    private void OnSelectFolderClick(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "레거시 거래명세표 엑셀 파일이 있는 폴더를 선택하세요",
        };
        var last = _settingsService.GetLastFolder("TradeStatementMigration");
        if (!string.IsNullOrWhiteSpace(last)) dlg.SelectedPath = last;

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _settingsService.SetLastFolder("TradeStatementMigration", dlg.SelectedPath);
        _folderBox.Text = dlg.SelectedPath;

        _matchedFiles = Directory.GetFiles(dlg.SelectedPath, "거래명세표*.xls*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("~$")) // 엑셀 임시파일 제외
            .ToList();
        _fileCountLabel.Text = $"{_matchedFiles.Count}개 파일 발견";
        _btnScan.Enabled = _matchedFiles.Count > 0;

        _grid.Rows.Clear();
        _scannedSheets.Clear();
        _btnCommit.Enabled = false;
        _statusLabel.Text = "";
    }

    private void OnScanClick(object? sender, EventArgs e)
    {
        Cursor = Cursors.WaitCursor;
        _btnScan.Enabled = false;
        try
        {
            _scannedSheets.Clear();
            _grid.Rows.Clear();
            var openFailures = new List<string>();

            foreach (var file in _matchedFiles)
            {
                var pkg = ExcelFileOpener.OpenWithPasswordPrompt(file, this);
                if (pkg == null) { openFailures.Add(Path.GetFileName(file)); continue; }

                using (pkg)
                {
                    foreach (var ws in pkg.Workbook.Worksheets)
                    {
                        var parsed = TradeStatementSheetParser.Parse(ws, Path.GetFileName(file));
                        _scannedSheets.Add(parsed);
                        AddGridRow(parsed);
                    }
                }
            }

            _statusLabel.Text = $"스캔 완료: {_scannedSheets.Count}개 시트"
                + (openFailures.Count > 0 ? $" (열기 실패/취소 {openFailures.Count}건)" : "");
            _btnCommit.Enabled = _scannedSheets.Count > 0;
        }
        finally
        {
            Cursor = Cursors.Default;
            _btnScan.Enabled = true;
        }
    }

    private void AddGridRow(ParsedStatementSheet sheet)
    {
        string party = !string.IsNullOrWhiteSpace(sheet.Buyer?.RegNo)
            ? $"{sheet.Buyer!.RegNo} {sheet.Buyer.CompanyName}"
            : !string.IsNullOrWhiteSpace(sheet.Buyer?.CompanyName) ? sheet.Buyer!.CompanyName : "(식별불가)";

        string totalsStatus = sheet.Flags.Contains("TOTALS_MISMATCH") ? "불일치"
            : sheet.Flags.Contains("NO_TOTALS_ROW") ? "없음" : "일치";

        // 노이즈/폐기 시트만 기본 제외 — 나머지(사본추정/총계불일치/거래처식별불가 등)는 차단이 아니라
        // 표기만 하고 기본 포함한다(스펙 §3.6 원칙).
        bool includeDefault = !sheet.Flags.Any(DefaultExcludeFlags.Contains);

        _grid.Rows.Add(includeDefault, sheet.SourceFileName, sheet.SourceSheetName, party,
            sheet.IssueDate?.ToString("yyyy-MM-dd") ?? "", sheet.TemplateSignature,
            totalsStatus, string.Join(", ", sheet.Flags));
    }

    private void OnCommitClick(object? sender, EventArgs e)
    {
        _grid.EndEdit();
        var toCommit = new List<ParsedStatementSheet>();
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].Cells["Include"].Value is true)
                toCommit.Add(_scannedSheets[i]);
        }

        if (toCommit.Count == 0)
        {
            MessageBox.Show("포함된 항목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                $"체크된 {toCommit.Count}건을 DB에 반영합니다. 이미 있는 (파일명, 시트명) 조합은 덮어씁니다. 계속하시겠습니까?",
                "커밋 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var result = _commitService.Commit(toCommit);
            MessageBox.Show(
                $"완료.\n\n신규 거래처: {result.NewPartiesCreated}건 / 기존 거래처 재사용: {result.ExistingPartiesReused}건 / " +
                $"발행건 저장: {result.StatementsSaved}건 / 스킵: {result.StatementsSkipped}건",
                "커밋 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _statusLabel.Text = "커밋 완료.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"커밋 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}
