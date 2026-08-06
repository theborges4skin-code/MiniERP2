using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 판매가/납품가 적용(§6.2) — 편집창. 채널 하나에 속한 라인들을 확인·수정한 뒤 확정하면
/// PriceQuoteTable(Status=Draft, Origin=Calculator)에 견적 1건 + 라인 여러 개로 저장한다.
/// </summary>
public class MarginQuoteApplyEditDialog : Form
{
    private sealed class LineRow
    {
        public required MarginCalcRow SourceRow { get; init; }
        public string CskuCode => SourceRow.CskuCode ?? "";
        public string Msku => SourceRow.Msku ?? "";
        public string ItemName => SourceRow.ItemName ?? "";
        public decimal? OldPrice { get; init; }
        public decimal NewPrice { get; set; }
    }

    private readonly string _channelCode;
    private readonly PriceQuoteRepository _quoteRepository;
    private readonly ChannelSkuRepository _cskuRepository;
    private readonly BindingList<LineRow> _lines;
    private readonly ExcelLikeDataGridView _grid = new();

    /// <summary>표시기준(VAT 포함/별도)을 DB 저장기준(VAT포함 고정, §4.6)으로 되돌리는 역환산.
    /// 계산기 창의 현재 VAT 토글에 따라 항등함수이거나 ÷1.1이다.</summary>
    private readonly Func<decimal, decimal> _toDbBasis;

    public string? SavedQuoteNo { get; private set; }

    public MarginQuoteApplyEditDialog(string channelCode, string channelName, List<MarginCalcRow> rows,
        PriceQuoteRepository quoteRepository, ChannelSkuRepository cskuRepository, Func<decimal, decimal>? toDbBasis = null)
    {
        _channelCode = channelCode;
        _quoteRepository = quoteRepository;
        _cskuRepository = cskuRepository;
        _toDbBasis = toDbBasis ?? (v => v);

        _lines = new BindingList<LineRow>(rows.Select(r =>
        {
            var current = _cskuRepository.GetByChannelAndCskuCode(channelCode, r.CskuCode!);
            return new LineRow
            {
                SourceRow = r,
                OldPrice = current?.SupplyPrice,
                NewPrice = r.SalePrice ?? 0m,
            };
        }).ToList());

        InitializeComponent(channelName);
    }

    private void InitializeComponent(string channelName)
    {
        Text = $"판매가/납품가 적용 — {channelName}";
        Size = new Size(700, 460);
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.DataSource = _lines;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU", DataPropertyName = "CskuCode", ReadOnly = true, Width = 100 },
            new DataGridViewTextBoxColumn { Name = "Msku", HeaderText = "MSKU", DataPropertyName = "Msku", ReadOnly = true, Width = 100 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", ReadOnly = true, Width = 160 },
            new DataGridViewTextBoxColumn { Name = "OldPrice", HeaderText = "기존 납품가", DataPropertyName = "OldPrice", ReadOnly = true, Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = SystemColors.GrayText } },
            new DataGridViewTextBoxColumn { Name = "NewPrice", HeaderText = "신규 납품가", DataPropertyName = "NewPrice", Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        layout.Controls.Add(_grid, 0, 0);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnSave = new Button { Text = "저장", Width = 90 };
        var btnCancel = new Button { Text = "취소", Width = 90 };
        btnSave.Click += OnSaveClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnSave);
        layout.Controls.Add(buttonPanel, 0, 1);

        Controls.Add(layout);
        CancelButton = btnCancel;
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var quote = new PriceQuote
        {
            QuoteNo = _quoteRepository.GenerateNextQuoteNo(now),
            ChannelCode = _channelCode,
            PriceKind = "Supply",
            QuoteFormType = "UnitOnly",
            Origin = "Calculator",
            Title = "간이 마진 계산기에서 생성",
            QuoteDate = now,
            EffectiveFrom = now.Date,
            Status = "Draft",
            PriceBasis = "VatIncl",
        };

        var lines = _lines.Select(l => new PriceQuoteLine
        {
            CskuCode = l.CskuCode,
            Msku = l.Msku,
            ItemNameSnap = l.ItemName,
            Unit = "EA",
            Qty = 0,
            OldPrice = l.OldPrice,
            NewPrice = _toDbBasis(l.NewPrice),
        }).ToList();

        _quoteRepository.SaveQuote(quote, lines);
        SavedQuoteNo = quote.QuoteNo;
        DialogResult = DialogResult.OK;
        Close();
    }
}
