using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// 특정 SKU의 원가 변경 이력을 표시하는 폼입니다.
/// </summary>
public class CostHistoryForm : Form
{
    private readonly ItemRepository _itemRepository = new();

    public CostHistoryForm(string sku)
    {
        InitializeComponent(sku);
    }

    private void InitializeComponent(string sku)
    {
        Text = $"원가 변경 이력 - {sku}";
        Size = new Size(600, 400);
        StartPosition = FormStartPosition.CenterParent;

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "변경일시", DataPropertyName = "ChangedAt", DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" }, FillWeight = 40 },
            new DataGridViewTextBoxColumn { HeaderText = "이전 원가", DataPropertyName = "OldCost", DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }, FillWeight = 30 },
            new DataGridViewTextBoxColumn { HeaderText = "새 원가", DataPropertyName = "NewCost", DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }, FillWeight = 30 }
        );

        Controls.Add(grid);

        grid.DataSource = _itemRepository.GetCostHistory(sku);
    }
}