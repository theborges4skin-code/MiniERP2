using MiniERP2.Controls;
using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// B2B 견적관리 §4 "통합 조회창" — 마스터SKU 1개를 기준으로 매입처들의 매입가 이력과 판매처들의
/// 납품가(CSKU)를 한 화면에 모아, kg당 마진을 매입처×판매처 조합별로 비교해서 보여줍니다.
/// 견적 시점에는 물류비(발송운임 배부)가 아직 확정되지 않으므로(§3 "견적 시점: 물류비 미확정"),
/// 여기서는 원가/kg 대비 납품가/kg의 단순 마진만 계산합니다 — 실질 마진(물류비 포함)은 출고확정
/// 이후 발주/출고 이력에서 계산됩니다(M5).
/// </summary>
public class PurchaseSalesOverviewForm : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly PurchaseSkuRepository _purchaseSkuRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();

    public PurchaseSalesOverviewForm(string masterSku)
    {
        InitializeComponent(masterSku);
    }

    private record MarginRow(string SalesChannel, string CskuCode, decimal SupplyPrice, string SalesUnit,
        string PurchaseSource, decimal CostPerUnit, decimal MarginPerUnit, decimal MarginRate);

    private void InitializeComponent(string masterSku)
    {
        var item = _itemRepository.GetBySku(masterSku);
        Text = $"매입·납품 통합 조회 - {masterSku}" + (item != null ? $" ({item.ItemName})" : "");
        Size = new Size(1000, 620);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterParent;

        var channelNames = _salesChannelRepository.GetAll().ToDictionary(c => c.ChannelCode, c => c.ChannelName);
        string ChannelLabel(string code) => channelNames.TryGetValue(code, out var name) ? name : code;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var headerLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Font = new Font(Font, FontStyle.Bold),
            Text = item != null
                ? $"{masterSku}  {item.ItemName}   대표원가(CostPrice): {item.CostPrice:N0}원/{item.Unit}"
                : $"{masterSku}  (마스터DB에 등록되지 않은 SKU — 대표원가 없음)",
        };

        // ── 상단: 매입처별 매입가 | 판매처별 납품가(CSKU) ──
        var topSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 480 };

        var purchases = _purchaseSkuRepository.GetAllByMsku(masterSku);
        var purchaseGrid = BuildGrid();
        purchaseGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "매입처", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "매입가", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단위", Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "마지막 수정", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { HeaderText = "비고", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        foreach (var p in purchases.OrderBy(p => p.PurchasePrice))
        {
            purchaseGrid.Rows.Add(ChannelLabel(p.ChannelCode), p.PurchasePrice, p.Unit, p.UpdatedAt, p.Note);
        }
        AddPanel(topSplit.Panel1.Controls, "매입처별 매입가", purchaseGrid);

        var sales = _channelSkuRepository.GetAllByMsku(masterSku);
        var salesGrid = BuildGrid();
        salesGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "판매처", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "납품가", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단위", Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "마지막 수정", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } }
        );
        foreach (var s in sales.OrderByDescending(s => s.SupplyPrice))
        {
            salesGrid.Rows.Add(ChannelLabel(s.ChannelCode), s.CskuCode, s.SupplyPrice, s.Unit, s.UpdatedAt);
        }
        AddPanel(topSplit.Panel2.Controls, "판매처별 납품가(CSKU)", salesGrid);

        // ── 하단: 매입처×판매처 조합별 kg당 마진(대표원가도 매입 후보 중 하나로 포함) ──
        var marginGrid = BuildGrid();
        marginGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "판매처", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "납품가", Width = 85, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "원가 출처", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "원가", Width = 85, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "마진(물류비 제외)", Width = 120, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "마진율", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "P1", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );

        var costOptions = new List<(string Source, decimal Cost)>();
        if (item != null) costOptions.Add(("대표원가", item.CostPrice));
        costOptions.AddRange(purchases.OrderBy(p => p.PurchasePrice).Select(p => ($"{ChannelLabel(p.ChannelCode)} 매입가", p.PurchasePrice)));

        foreach (var s in sales.OrderByDescending(s => s.SupplyPrice))
        {
            // "CSKU 개별원가"는 CSKU마다 있거나 없을 수 있는 값이라, Msku 전체에 공통인 costOptions에
            // 정적으로 넣지 않고 이 CSKU 행에서만 조합한다(CSKU제조원가_개별관리_개발기획서.md §5).
            var rowCostOptions = s.CostPriceOverride.HasValue
                ? costOptions.Append(("CSKU 개별원가", s.CostPriceOverride.Value))
                : costOptions;

            foreach (var (source, cost) in rowCostOptions)
            {
                var margin = s.SupplyPrice - cost;
                var rate = s.SupplyPrice == 0 ? 0 : margin / s.SupplyPrice;
                var rowIdx = marginGrid.Rows.Add(ChannelLabel(s.ChannelCode), s.CskuCode, s.SupplyPrice, source, cost, margin, rate);
                if (margin < 0) marginGrid.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        }
        var marginLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 20,
            ForeColor = Color.DimGray,
            Padding = new Padding(8, 2, 0, 0),
            Text = "물류비(발송운임 배부)는 견적 시점에 미확정이라 제외했습니다 — 실질 마진은 출고확정 후 발주/출고 이력에서 확인하세요.",
        };
        var marginPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        marginPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        marginPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        marginPanel.Controls.Add(marginLabel, 0, 0);
        marginPanel.Controls.Add(marginGrid, 0, 1);

        mainLayout.Controls.Add(headerLabel, 0, 0);
        mainLayout.Controls.Add(topSplit, 0, 1);
        mainLayout.Controls.Add(marginPanel, 0, 2);
        Controls.Add(mainLayout);
    }

    private static DataGridView BuildGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
    };

    private static void AddPanel(Control.ControlCollection host, string title, DataGridView grid)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(grid.Font, FontStyle.Bold), Padding = new Padding(4, 4, 0, 0) }, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        host.Add(layout);
    }
}
