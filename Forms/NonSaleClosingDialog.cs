using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드 [비매출 내역](샘플발송이력관리_개발기획서.md §6) — 샘플·CS·기타로 지정된
/// 비매출성 발송을 월별로 별도 추적하는 읽기 전용 다이얼로그입니다.
///
/// 기존 마감보드 그리드를 토글로 재활용하지 않는 이유: 같은 화면에 "마감확정"·"명세표 발행" 버튼이
/// 살아 있어, 비매출 목록이 떠 있는 상태에서 오조작할 위험이 있습니다. 비매출은 확정·발행 대상이
/// 아니므로(§4.4 기본 스코프 SaleOnly로 이미 자동 배제됨) 화면을 분리하는 편이 안전합니다.
/// </summary>
public class NonSaleClosingDialog : Form
{
    private readonly PartnerClosingRepository _closingRepo = new();

    private static readonly (string Label, string? Kind)[] LineKindFilterDefs =
    [
        ("전체", null),
        (LineKinds.Sample, LineKinds.Sample),
        (LineKinds.Cs, LineKinds.Cs),
        (LineKinds.Other, LineKinds.Other),
    ];

    private TextBox _periodBox = new();
    private ComboBox _lineKindCombo = new();
    private ExcelLikeDataGridView _summaryGrid = new();
    private ExcelLikeDataGridView _lineGrid = new();
    private Label _summaryLabel = new();
    private List<NonSaleSummaryRow> _rows = [];

    public NonSaleClosingDialog(string initialPeriod)
    {
        InitializeComponent(initialPeriod);
        Load += (s, e) => RunQuery();
    }

    private void InitializeComponent(string initialPeriod)
    {
        Text = "비매출 발송 현황";
        Size = new Size(1000, 620);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        _periodBox = new TextBox { Width = 80, Text = initialPeriod };
        _lineKindCombo = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        _lineKindCombo.Items.AddRange(LineKindFilterDefs.Select(d => d.Label).ToArray());
        _lineKindCombo.SelectedIndex = 0;

        var btnRefresh = new Button { Text = "새로고침", Size = new Size(80, 28) };
        btnRefresh.Click += (s, e) => RunQuery();
        var btnExport = new Button { Text = "엑셀 내보내기", Size = new Size(100, 28) };
        btnExport.Click += OnExportClick;

        toolbar.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolbar.Controls.Add(_periodBox);
        toolbar.Controls.Add(new Label { Text = "구분:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolbar.Controls.Add(_lineKindCombo);
        toolbar.Controls.Add(btnRefresh);
        toolbar.Controls.Add(btnExport);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 420 };

        _summaryGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _summaryGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "거래처", Name = "ChannelName", DataPropertyName = "ChannelName", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "구분", Name = "LineKind", DataPropertyName = "LineKind", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", Name = "Count", DataPropertyName = "Count", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "원가금액", Name = "CostAmount", DataPropertyName = "CostAmount", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _summaryGrid.SelectionChanged += (s, e) => LoadLineGrid();
        split.Panel1.Controls.Add(_summaryGrid);

        _lineGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
        };
        _lineGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "일자", Name = "LineDate", DataPropertyName = "LineDate", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "품목", Name = "ItemName", DataPropertyName = "ItemName", Width = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 55, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "원가", Name = "CostPrice", DataPropertyName = "CostPrice", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "메모", Name = "Remark", DataPropertyName = "Remark", Width = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "수령인", Name = "Recipient", DataPropertyName = "Recipient", Width = 90 }
        );
        split.Panel2.Controls.Add(_lineGrid);

        _summaryLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolbar, 0, 0);
        mainLayout.Controls.Add(split, 0, 1);
        mainLayout.Controls.Add(_summaryLabel, 0, 2);
        Controls.Add(mainLayout);
    }

    private void RunQuery()
    {
        var period = _periodBox.Text.Trim();
        var lineKind = LineKindFilterDefs[Math.Max(_lineKindCombo.SelectedIndex, 0)].Kind;

        _rows = _closingRepo.GetNonSaleSummary(period, lineKind);
        _summaryGrid.DataSource = new BindingList<NonSaleSummaryRow>(_rows);
        LoadLineGrid();

        var unshippedCount = _closingRepo.GetNonSaleUnshippedCount(period, lineKind);
        var byKind = _rows.GroupBy(r => r.LineKind)
            .Select(g => $"{g.Key} {g.Sum(r => r.Count)}건 {g.Sum(r => r.CostAmount):N0}원")
            .ToList();
        var breakdown = byKind.Count > 0 ? string.Join(" / ", byKind) : "내역 없음";
        var unshippedNote = unshippedCount > 0 ? $"  (미출고 {unshippedCount}건 별도 — 집계 금액 미포함)" : "";
        _summaryLabel.Text = $"{period} 합계 — {breakdown}{unshippedNote}";
    }

    private void LoadLineGrid()
    {
        var selected = _summaryGrid.CurrentRow?.DataBoundItem as NonSaleSummaryRow;
        _lineGrid.DataSource = selected == null ? null : new BindingList<NonSaleDetailLine>(selected.Lines);
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("내보낼 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var period = _periodBox.Text.Trim();
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"비매출발송현황_{period}_{DateTime.Now:yyyyMMdd}.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();

            var summarySheet = package.Workbook.Worksheets.Add("비매출집계");
            var summaryHeaders = new[] { "거래처", "구분", "건수", "수량", "원가금액" };
            for (var i = 0; i < summaryHeaders.Length; i++) summarySheet.Cells[1, i + 1].Value = summaryHeaders[i];
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var r = i + 2;
                summarySheet.Cells[r, 1].Value = row.ChannelName;
                summarySheet.Cells[r, 2].Value = row.LineKind;
                summarySheet.Cells[r, 3].Value = row.Count;
                summarySheet.Cells[r, 4].Value = row.Qty;
                summarySheet.Cells[r, 5].Value = row.CostAmount;
            }

            var detailSheet = package.Workbook.Worksheets.Add("라인상세");
            var detailHeaders = new[] { "거래처", "구분", "일자", "CSKU", "품목", "수량", "원가", "메모", "수령인" };
            for (var i = 0; i < detailHeaders.Length; i++) detailSheet.Cells[1, i + 1].Value = detailHeaders[i];
            var r2 = 2;
            foreach (var row in _rows)
            {
                foreach (var line in row.Lines)
                {
                    detailSheet.Cells[r2, 1].Value = row.ChannelName;
                    detailSheet.Cells[r2, 2].Value = row.LineKind;
                    detailSheet.Cells[r2, 3].Value = line.LineDate.ToString("yyyy-MM-dd");
                    detailSheet.Cells[r2, 4].Value = line.CskuCode;
                    detailSheet.Cells[r2, 5].Value = line.ItemName;
                    detailSheet.Cells[r2, 6].Value = line.Qty;
                    detailSheet.Cells[r2, 7].Value = line.CostPrice;
                    detailSheet.Cells[r2, 8].Value = line.Remark;
                    detailSheet.Cells[r2, 9].Value = line.Recipient;
                    r2++;
                }
            }

            ExportHelper.SaveExcel(package, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExportHelper.DescribeSaveError(ex), "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
