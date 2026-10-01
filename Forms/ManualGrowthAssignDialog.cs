using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 이공그로스수동마감의 미배정 상품명 일괄 배정(ManualGrowthClosing_Spec.md §5-4). 상품명(=CSKU코드)마다
/// 마스터SKU를 고르면 그 채널에 CSKU를 만든다 — 상품명이 한 글자라도 다르면 별도 행으로 나오며,
/// 세트 구성도 여기서 사용자가 직접 판단해 배정한다. MSKU를 비워둔 행은 건너뛴다(다음에 다시 뜸).
/// </summary>
public class ManualGrowthAssignDialog : Form
{
    public sealed class Candidate
    {
        public string CskuCode { get; init; } = "";
        public decimal Qty { get; init; }
        /// <summary>CSKU 납품가로 저장할 VAT포함 단가(ChannelSkuTable.SupplyPrice 기준과 동일).</summary>
        public decimal UnitPriceVatIncluded { get; init; }
    }

    private readonly string _channelCode;
    private readonly List<Candidate> _candidates;
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly ItemRepository _itemRepo = new();
    private readonly ExcelLikeDataGridView _grid = new();

    public int CreatedCount { get; private set; }

    private const int ColCsku = 0, ColQty = 1, ColPrice = 2, ColMsku = 3, ColItemName = 4;

    public ManualGrowthAssignDialog(string channelCode, string channelName, List<Candidate> candidates)
    {
        _channelCode = channelCode;
        _candidates = candidates;

        Text = $"미배정 상품명 일괄 배정 — {channelName}";
        Size = new Size(860, 480);
        MinimumSize = new Size(640, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "상품명마다 마스터SKU를 지정하세요. MSKU 칸을 더블클릭하거나 [MSKU 검색]으로 찾을 수 있습니다.\n" +
                   "한 글자라도 다른 상품명은 별도 CSKU로 등록됩니다. MSKU를 비워둔 행은 이번에 등록하지 않습니다.",
        }, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.PersistenceKey = "ManualGrowthAssignDialog.Grid";
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상품명(=CSKU)", Width = 260, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Width = 60, ReadOnly = true, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품가(VAT포함)", Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "MSKU", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "마스터 품명", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true }
        );
        foreach (var c in candidates)
            _grid.Rows.Add(c.CskuCode, c.Qty, c.UnitPriceVatIncluded, "", "");
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == ColMsku) PickMsku(e.RowIndex); };
        _grid.CellEndEdit += (s, e) => { if (e.ColumnIndex == ColMsku) RefreshItemName(e.RowIndex); };
        layout.Controls.Add(_grid, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelBtn = new Button { Text = "취소", Size = new Size(80, 30) };
        var okBtn = new Button { Text = "등록", Size = new Size(80, 30), Font = new Font(Font, FontStyle.Bold) };
        var searchBtn = new Button { Text = "MSKU 검색", Size = new Size(90, 30) };
        cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        okBtn.Click += OnOkClick;
        searchBtn.Click += (s, e) => { if (_grid.CurrentCell != null) PickMsku(_grid.CurrentCell.RowIndex); };
        buttons.Controls.AddRange([cancelBtn, okBtn, searchBtn]);
        layout.Controls.Add(buttons, 0, 2);

        Controls.Add(layout);
        CancelButton = cancelBtn;
    }

    private void PickMsku(int rowIndex)
    {
        var row = _grid.Rows[rowIndex];
        using var picker = new MasterSkuPickerDialog(row.Cells[ColCsku].Value as string);
        if (FormManager.ShowDialogSafe(picker, this) != DialogResult.OK || string.IsNullOrEmpty(picker.SelectedSku)) return;
        row.Cells[ColMsku].Value = picker.SelectedSku;
        row.Cells[ColItemName].Value = picker.SelectedItemName ?? "";
    }

    private void RefreshItemName(int rowIndex)
    {
        var row = _grid.Rows[rowIndex];
        var msku = (row.Cells[ColMsku].Value as string ?? "").Trim();
        row.Cells[ColItemName].Value = msku.Length == 0 ? "" : _itemRepo.GetBySku(msku)?.ItemName ?? "(마스터에 없음)";
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _grid.EndEdit();

        var invalid = new List<string>();
        var toCreate = new List<(string Csku, string Msku, decimal Price)>();
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            var row = _grid.Rows[i];
            var msku = (row.Cells[ColMsku].Value?.ToString() ?? "").Trim();
            if (msku.Length == 0) continue;
            if (_itemRepo.GetBySku(msku) == null) { invalid.Add($"{row.Cells[ColCsku].Value} → {msku}"); continue; }
            decimal.TryParse(row.Cells[ColPrice].Value?.ToString(), out var price);
            toCreate.Add((_candidates[i].CskuCode, msku, price));
        }

        if (invalid.Count > 0)
        {
            MessageBox.Show("마스터DB에 없는 MSKU가 있습니다. 확인 후 다시 시도하세요.\n\n" + string.Join("\n", invalid),
                "MSKU 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (toCreate.Count == 0)
        {
            MessageBox.Show("MSKU를 지정한 행이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var (csku, msku, price) in toCreate)
        {
            if (!_channelSkuRepo.CreateIfNew(_channelCode, csku, msku, price, csku))
            {
                // 이미 CSKU는 있는데 MSKU가 비어 있던 경우 — MSKU만 채운다.
                var existing = _channelSkuRepo.GetByChannelAndCskuCode(_channelCode, csku)!;
                existing.Msku = msku;
                _channelSkuRepo.Upsert(existing);
            }
        }
        CreatedCount = toCreate.Count;
        DialogResult = DialogResult.OK;
        Close();
    }
}
