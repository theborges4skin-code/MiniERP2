using MiniERP2.Controls;
using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// 특정 채널 SKU(CSKU)의 변경 이력을 표시하는 폼입니다. 납품가 변경(ChannelSkuPriceHistory)과
/// 마스터SKU/송장표시명/비고 변경(ChannelSkuFieldHistory)을 한 목록에 시간순으로 합쳐서 보여줍니다.
/// </summary>
public class ChannelSkuHistoryForm : Form
{
    private readonly ChannelSkuRepository _cskuRepository = new();

    public ChannelSkuHistoryForm(string channelCode, string cskuCode)
    {
        InitializeComponent(channelCode, cskuCode);
    }

    private record HistoryRow(DateTime ChangedAt, string FieldName, string OldValue, string NewValue, string Reason);

    private void InitializeComponent(string channelCode, string cskuCode)
    {
        Text = $"CSKU 변경 이력 - {channelCode} / {cskuCode}";
        Size = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;

        var grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "변경일시", DataPropertyName = "ChangedAt", DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" }, FillWeight = 25 },
            new DataGridViewTextBoxColumn { HeaderText = "필드", DataPropertyName = "FieldName", FillWeight = 15 },
            new DataGridViewTextBoxColumn { HeaderText = "이전 값", DataPropertyName = "OldValue", FillWeight = 20 },
            new DataGridViewTextBoxColumn { HeaderText = "새 값", DataPropertyName = "NewValue", FillWeight = 20 },
            new DataGridViewTextBoxColumn { HeaderText = "사유", DataPropertyName = "Reason", FillWeight = 20 }
        );

        Controls.Add(grid);

        var rows = _cskuRepository.GetPriceHistory(channelCode, cskuCode)
            .Select(p => new HistoryRow(p.ChangedAt, "납품가", p.OldPrice.ToString("N0"), p.NewPrice.ToString("N0"), p.Reason ?? ""))
            .Concat(_cskuRepository.GetFieldHistory(channelCode, cskuCode)
                .Select(f => new HistoryRow(f.ChangedAt, f.FieldName, f.OldValue ?? "", f.NewValue ?? "", "")))
            .OrderBy(r => r.ChangedAt)
            .ToList();

        grid.DataSource = rows;
    }
}
