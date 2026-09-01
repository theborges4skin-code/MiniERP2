using System.Text.RegularExpressions;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 수출요약 수동 입력 편집기.
/// 마켓을 드롭다운으로 선택하고, (연월/지표/통화/금액)을 표 형식으로 일괄 입력한다.
/// "DB에 저장"으로 임시저장하고 나중에 다시 불러올 수 있다.
/// "집계에 추가"를 누르면 현재 그리드 내용을 Entries로 반환하고 닫힌다.
/// </summary>
public class ExportSummaryManualEntryDialog : Form
{
    private readonly List<ExportSummaryMarket> _markets;
    private readonly ExportSummaryDraftRepository _repo = new();

    private ComboBox _marketComboBox = new();
    private DataGridView _grid = new();
    private Label _statusLabel = new();

    public List<ExportSummaryEntry> Entries { get; private set; } = [];

    private static readonly string[] Indicators = ["신고액", "매출액", "실익액"];

    private const int ColYearMonth = 0;
    private const int ColIndicator = 1;
    private const int ColCurrency = 2;
    private const int ColAmount = 3;

    public ExportSummaryManualEntryDialog(List<ExportSummaryMarket> markets)
    {
        _markets = markets;
        InitializeComponent();
        LoadMarketCombo();
    }

    private void InitializeComponent()
    {
        Text = "수동 입력 편집기";
        Size = new Size(680, 520);
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(10),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 마켓 선택
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 그리드
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));    // 행 추가/삭제
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));    // 버튼 바

        outer.Controls.Add(BuildMarketRow(), 0, 0);
        outer.Controls.Add(BuildGrid(), 0, 1);
        outer.Controls.Add(BuildRowButtons(), 0, 2);
        outer.Controls.Add(BuildFooter(), 0, 3);

        Controls.Add(outer);
    }

    // ─── 마켓 선택 ────────────────────────────────────────────────────────────

    private Control BuildMarketRow()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };

        panel.Controls.Add(new Label { Text = "마켓:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });

        _marketComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
        };
        _marketComboBox.SelectedIndexChanged += OnMarketChanged;
        panel.Controls.Add(_marketComboBox);

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Gray,
            Padding = new Padding(16, 6, 0, 0),
        };
        panel.Controls.Add(_statusLabel);

        return panel;
    }

    private void LoadMarketCombo()
    {
        _marketComboBox.DataSource = _markets.ToList();
        _marketComboBox.DisplayMember = nameof(ExportSummaryMarket.MarketName);
        if (_marketComboBox.Items.Count > 0)
            _marketComboBox.SelectedIndex = 0;
    }

    private void OnMarketChanged(object? s, EventArgs e)
    {
        if (_marketComboBox.SelectedItem is not ExportSummaryMarket market) return;
        var saved = _repo.GetByMarket(market.MarketCode);
        PopulateGrid(saved);
        _statusLabel.Text = saved.Count > 0
            ? $"DB에서 {saved.Count}건 불러옴 ({saved.Max(r => r.SavedAt)[..10]})"
            : "저장된 항목 없음";
    }

    // ─── 그리드 ──────────────────────────────────────────────────────────────

    private DataGridView BuildGrid()
    {
        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            RowHeadersVisible = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "연월", HeaderText = "연월", Width = 90,
            ToolTipText = "YYYY-MM 형식으로 입력",
        });

        var indCol = new DataGridViewComboBoxColumn
        {
            Name = "지표", HeaderText = "지표", Width = 90,
            DataSource = Indicators.ToList(),
            FlatStyle = FlatStyle.Flat,
        };
        _grid.Columns.Add(indCol);

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "통화", HeaderText = "통화", Width = 70,
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "금액", HeaderText = "금액", Width = 140,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
        });

        _grid.CellValueChanged += OnGridCellValueChanged;
        _grid.DataError += (s, e) => e.Cancel = true;

        return _grid;
    }

    private void PopulateGrid(List<ExportSummaryDraftRow> rows)
    {
        _grid.Rows.Clear();
        foreach (var r in rows)
            _grid.Rows.Add(r.YearMonth, r.Indicator, r.Currency, r.Amount.ToString("G"));
    }

    private void OnGridCellValueChanged(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != ColIndicator) return;
        if (_grid.Rows[e.RowIndex].IsNewRow) return;

        var indicator = _grid.Rows[e.RowIndex].Cells[ColIndicator].Value as string ?? "";
        var market = _marketComboBox.SelectedItem as ExportSummaryMarket;

        var currency = indicator == "실익액" ? "USD" : (market?.Currency ?? "");
        _grid.Rows[e.RowIndex].Cells[ColCurrency].Value = currency;
    }

    // ─── 행 추가/삭제 버튼 ───────────────────────────────────────────────────

    private Control BuildRowButtons()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };

        var addBtn = new Button { Text = "+ 행 추가", Width = 80, Height = 28 };
        addBtn.Click += (_, _) =>
        {
            var market = _marketComboBox.SelectedItem as ExportSummaryMarket;
            var ym = DateTime.Today.AddMonths(-1).ToString("yyyy-MM");
            _grid.Rows.Add(ym, "매출액", market?.Currency ?? "", "");
            _grid.CurrentCell = _grid.Rows[^1].Cells[ColYearMonth];
        };

        var delBtn = new Button { Text = "선택 삭제", Width = 80, Height = 28, Margin = new Padding(6, 0, 0, 0) };
        delBtn.Click += (_, _) =>
        {
            var rows = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .OrderByDescending(r => r.Index)
                .ToList();
            foreach (var r in rows) _grid.Rows.Remove(r);
        };

        panel.Controls.Add(addBtn);
        panel.Controls.Add(delBtn);
        return panel;
    }

    // ─── 버튼 바 ─────────────────────────────────────────────────────────────

    private Control BuildFooter()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };

        var saveBtn = new Button { Text = "DB에 저장", Width = 90, Height = 32, Font = new Font(Font, FontStyle.Bold) };
        saveBtn.Click += OnSaveClick;

        var addBtn = new Button { Text = "집계에 추가", Width = 90, Height = 32, Margin = new Padding(8, 0, 0, 0) };
        addBtn.Click += OnAddToAggregateClick;

        var closeBtn = new Button { Text = "닫기", Width = 70, Height = 32, Margin = new Padding(16, 0, 0, 0) };
        closeBtn.Click += (_, _) => Close();

        panel.Controls.Add(saveBtn);
        panel.Controls.Add(addBtn);
        panel.Controls.Add(closeBtn);
        return panel;
    }

    // ─── DB 저장 ─────────────────────────────────────────────────────────────

    private void OnSaveClick(object? s, EventArgs e)
    {
        if (_marketComboBox.SelectedItem is not ExportSummaryMarket market) return;
        _grid.EndEdit();

        var rows = CollectRows(market, out var errors);
        if (errors.Count > 0)
        {
            MessageBox.Show($"저장 전 오류를 수정하세요:\n{string.Join("\n", errors)}", "유효성 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _repo.SaveForMarket(market.MarketCode, rows);
        _statusLabel.Text = $"저장 완료 ({DateTime.Now:HH:mm:ss}) — {rows.Count}건";
    }

    // ─── 집계에 추가 ─────────────────────────────────────────────────────────

    private void OnAddToAggregateClick(object? s, EventArgs e)
    {
        if (_marketComboBox.SelectedItem is not ExportSummaryMarket market) return;
        _grid.EndEdit();

        var rows = CollectRows(market, out var errors);
        if (errors.Count > 0)
        {
            MessageBox.Show($"다음 행을 수정하세요:\n{string.Join("\n", errors)}", "유효성 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (rows.Count == 0)
        {
            MessageBox.Show("추가할 항목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Entries = rows.Select(r => new ExportSummaryEntry(
            IndicatorToTrack(r.Indicator), market.MarketCode, r.YearMonth, r.Amount,
            r.Indicator, r.Currency)).ToList();

        DialogResult = DialogResult.OK;
        Close();
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────────────

    private List<ExportSummaryDraftRow> CollectRows(ExportSummaryMarket market, out List<string> errors)
    {
        errors = [];
        var result = new List<ExportSummaryDraftRow>();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;

            var ym = (row.Cells[ColYearMonth].Value as string ?? "").Trim();
            var ind = (row.Cells[ColIndicator].Value as string ?? "").Trim();
            var cur = (row.Cells[ColCurrency].Value as string ?? "").Trim();
            var amtText = (row.Cells[ColAmount].Value as string ?? row.Cells[ColAmount].Value?.ToString() ?? "").Trim();

            if (string.IsNullOrEmpty(ym) && string.IsNullOrEmpty(amtText)) continue; // 빈 행 무시

            if (!Regex.IsMatch(ym, @"^\d{4}-\d{2}$"))
            {
                errors.Add($"행 {row.Index + 1}: 연월 형식 오류 ({ym}) — YYYY-MM 형식으로 입력");
                continue;
            }
            if (!decimal.TryParse(amtText.Replace(",", ""), out var amount))
            {
                errors.Add($"행 {row.Index + 1}: 금액을 숫자로 입력하세요 ({amtText})");
                continue;
            }
            if (string.IsNullOrEmpty(ind))
                ind = "매출액";
            if (string.IsNullOrEmpty(cur))
                cur = ind == "실익액" ? "USD" : market.Currency;

            result.Add(new ExportSummaryDraftRow
            {
                MarketCode = market.MarketCode,
                YearMonth = ym,
                Indicator = ind,
                Currency = cur,
                Amount = amount,
            });
        }

        return result;
    }

    private static string IndicatorToTrack(string indicator) => indicator switch
    {
        "신고액" => "A",
        "매출액" => "B",
        "실익액" => "C",
        _ => "B",
    };
}
