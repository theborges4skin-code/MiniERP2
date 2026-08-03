using System.ComponentModel;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 명세표/매출장을 파일로 저장하지 않고 화면에서 바로 확인하는 미리보기 창(거래처 마감보드
/// §버그수정1). 매번 파일로 받아 엑셀을 열어 확인·재발행하는 왕복을 없애기 위해, 발행 직전
/// PartnerClosingDocumentBuilder가 만든 문서 객체를 그대로 읽기전용 그리드로 보여준다. 여러 창을
/// 띄워 나란히 비교할 수 있도록 항상 새 창(비모달)으로 연다.
/// </summary>
public class DocumentPreviewForm : Form
{
    private DocumentPreviewForm(string title)
    {
        Text = title;
        Size = new Size(820, 640);
        StartPosition = FormStartPosition.CenterScreen;
    }

    public static DocumentPreviewForm ForTradeStatement(TradeStatementDoc doc)
    {
        var vatLabel = doc.IsVatExcluded ? "VAT 별도" : "VAT 포함";
        var form = new DocumentPreviewForm($"미리보기 — 거래명세표({vatLabel})");

        var grid = BuildGrid();
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "일자", Name = "Date", DataPropertyName = "Date", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "품목", Name = "ItemName", DataPropertyName = "ItemName", Width = 200 },
            new DataGridViewTextBoxColumn { HeaderText = "규격", Name = "Spec", DataPropertyName = "Spec", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단가", Name = "UnitPrice", DataPropertyName = "UnitPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "공급가", Name = "SupplyAmount", DataPropertyName = "SupplyAmount", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        var rows = doc.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ItemName) || l.Qty != 0 || l.UnitPrice != 0)
            .Select(l => new
            {
                Date = l.Year > 0 ? $"{l.Year:0000}-{l.Month:00}-{(l.Day > 0 ? l.Day.ToString("00") : "-")}" : "",
                l.ItemName,
                l.Spec,
                l.Qty,
                l.UnitPrice,
                l.SupplyAmount,
            }).ToList();
        grid.DataSource = new BindingList<object>(rows.Cast<object>().ToList());

        var totalsLines = new List<string>
        {
            $"공급가액 합계: {doc.TotalSupply:N0}원",
        };
        if (doc.IsVatExcluded) totalsLines.Add($"세액: {doc.TotalTax:N0}원");
        totalsLines.Add($"합계금액{(doc.IsVatExcluded ? "" : "(VAT포함)")}: {doc.GrandTotal:N0}원");

        form.BuildLayout(
            $"공급자: {doc.Supplier.CompanyName}    →    공급받는자: {doc.Buyer.CompanyName}",
            grid,
            totalsLines);
        return form;
    }

    public static DocumentPreviewForm ForSalesLedger(SalesLedgerDoc doc)
    {
        var vatLabel = doc.IsVatExcluded ? "VAT 별도" : "VAT 포함";
        var form = new DocumentPreviewForm($"미리보기 — 매출장({vatLabel}, 내부검토용)");

        var grid = BuildGrid();
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "일자", Name = "Date", DataPropertyName = "Date", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "품목", Name = "ItemName", DataPropertyName = "ItemName", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "규격", Name = "Spec", DataPropertyName = "Spec", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단가", Name = "UnitPrice", DataPropertyName = "UnitPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "공급가", Name = "SupplyAmount", DataPropertyName = "SupplyAmount", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "원가", Name = "CostPrice", DataPropertyName = "CostPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "이익", Name = "ProfitAmount", DataPropertyName = "ProfitAmount", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        var rows = doc.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ItemName) || l.Qty != 0 || l.UnitPrice != 0)
            .Select(l => new
            {
                Date = l.Year > 0 ? $"{l.Year:0000}-{l.Month:00}-{(l.Day > 0 ? l.Day.ToString("00") : "-")}" : "",
                l.ItemName,
                l.Spec,
                l.Qty,
                l.UnitPrice,
                l.SupplyAmount,
                l.CostPrice,
                l.ProfitAmount,
            }).ToList();
        grid.DataSource = new BindingList<object>(rows.Cast<object>().ToList());

        var totalsLines = new List<string>
        {
            $"공급가 합계: {doc.TotalSupply:N0}원",
            $"원가 합계: {doc.TotalCost:N0}원",
            $"이익 합계: {doc.TotalProfit:N0}원",
        };

        form.BuildLayout(
            $"공급자: {doc.Supplier.CompanyName}    →    공급받는자: {doc.Buyer.CompanyName}",
            grid,
            totalsLines);
        return form;
    }

    private static DataGridView BuildGrid() => new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };

    private void BuildLayout(string partyLine, DataGridView grid, List<string> totalsLines)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var partyLabel = new Label { Text = partyLine, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };
        layout.Controls.Add(partyLabel, 0, 0);
        layout.Controls.Add(grid, 0, 1);

        var totalsLabel = new Label
        {
            Text = string.Join("     ", totalsLines),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 12, 0),
            Font = new Font(Font, FontStyle.Bold),
        };
        layout.Controls.Add(totalsLabel, 0, 2);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 8, 4) };
        var btnClose = new Button { Text = "닫기", Size = new Size(80, 30) };
        btnClose.Click += (s, e) => Close();
        buttonPanel.Controls.Add(btnClose);
        layout.Controls.Add(buttonPanel, 0, 3);

        var hintLabel = new Label
        {
            Text = "⚠ 미리보기 화면입니다 — 저장/발행되지 않으며, 실제 엑셀 서식(테두리·글꼴 등)과는 다를 수 있습니다.",
            Dock = DockStyle.Top,
            ForeColor = Color.DimGray,
            Padding = new Padding(8, 4, 0, 0),
        };
        Controls.Add(layout);
        Controls.Add(hintLabel);
    }
}
