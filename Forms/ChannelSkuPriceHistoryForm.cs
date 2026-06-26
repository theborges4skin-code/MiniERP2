using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// 특정 채널 SKU의 납품가 변경 이력을 표시하는 폼입니다.
/// </summary>
public class ChannelSkuPriceHistoryForm : Form
{
    private readonly ChannelSkuRepository _cskuRepository = new();

    public ChannelSkuPriceHistoryForm(string channelCode, string msku)
    {
        InitializeComponent(channelCode, msku);
    }

    private void InitializeComponent(string channelCode, string msku)
    {
        Text = $"납품가 변경 이력 - {channelCode} / {msku}";
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
            new DataGridViewTextBoxColumn { HeaderText = "이전 납품가", DataPropertyName = "OldPrice", DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }, FillWeight = 30 },
            new DataGridViewTextBoxColumn { HeaderText = "새 납품가", DataPropertyName = "NewPrice", DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }, FillWeight = 30 }
        );

        Controls.Add(grid);

        grid.DataSource = _cskuRepository.GetPriceHistory(channelCode, msku);
    }
}