using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 운송장 파일 누락건 점검 → [채널별 송장 대조]. 운송장 파일의 실제 발송 송장을 채널별로 세고, 송장마다
/// 배송비가 청구됐는지(배송비 라인 유무) 보여준다. 위 표에서 채널을 고르면 아래에 그 채널 송장 목록이
/// 나오고, 수령인·주소를 보며 골라 [선택 송장 배송비 청구]/[청구 해제]를 할 수 있다 — OFS에서 체크를
/// 깜빡했거나 푸디처럼 월말에 몰아서 판단하는 거래처용. 청구는 그 송장의 묶음에 배송비 라인 1줄을 붙이는
/// 것이라 OFS 토글과 같은 결과가 된다(ShippingFeeLineService). 마감확정된 달의 송장은 건드리지 않는다.
/// </summary>
public class ChannelShipmentReconcileForm : Form
{
    private readonly OutboundRepository _outboundRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly PartnerClosingRepository _closingRepo = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly ShippingFeeLineService _shippingFeeLines;

    private readonly List<TrackingBackfillRow> _fileRows;
    private List<ChannelShipmentRow> _shipments = [];

    private ExcelLikeDataGridView _summaryGrid = new();
    private ExcelLikeDataGridView _detailGrid = new();
    private CheckBox _chkUnchargedOnly = new();
    private ComboBox _amountCombo = new();
    private Label _summaryLabel = new();

    public ChannelShipmentReconcileForm(List<TrackingBackfillRow> fileRows)
    {
        _fileRows = fileRows;
        _shippingFeeLines = new ShippingFeeLineService(_channelSkuRepo);
        InitializeComponent();
        Reload();
    }

    private void InitializeComponent()
    {
        Text = "채널별 송장 대조 (배송비 청구)";
        Size = new Size(1150, 760);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 4));

        _summaryLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(5, 5, 0, 0) };

        _summaryGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        };
        DataGridViewCellStyle Num() => new() { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight };
        _summaryGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "채널", DataPropertyName = nameof(ChannelShipmentSummaryRow.GroupName), Width = 200 },
            new DataGridViewTextBoxColumn { HeaderText = "송장 수(파일)", DataPropertyName = nameof(ChannelShipmentSummaryRow.ShipmentCount), Width = 100, DefaultCellStyle = Num() },
            new DataGridViewTextBoxColumn { HeaderText = "배송비 청구", DataPropertyName = nameof(ChannelShipmentSummaryRow.ChargedCount), Width = 90, DefaultCellStyle = Num() },
            new DataGridViewTextBoxColumn { Name = "Uncharged", HeaderText = "미청구", DataPropertyName = nameof(ChannelShipmentSummaryRow.UnchargedCount), Width = 80, DefaultCellStyle = Num() },
            new DataGridViewTextBoxColumn { HeaderText = "청구액 합계", DataPropertyName = nameof(ChannelShipmentSummaryRow.ChargedAmount), Width = 100, DefaultCellStyle = Num() },
            new DataGridViewTextBoxColumn { HeaderText = "실 운임 합계(참고)", DataPropertyName = nameof(ChannelShipmentSummaryRow.FreightTotal), Width = 120, DefaultCellStyle = Num() }
        );
        _summaryGrid.SelectionChanged += (_, _) => RefreshDetail();

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 5, 5, 0), WrapContents = false };
        _chkUnchargedOnly = new CheckBox { Text = "미청구만 보기", AutoSize = true, Padding = new Padding(0, 4, 10, 0) };
        _chkUnchargedOnly.CheckedChanged += (_, _) => RefreshDetail();
        toolbar.Controls.Add(_chkUnchargedOnly);
        toolbar.Controls.Add(new Label { Text = "청구액(원, VAT포함):", AutoSize = true, Padding = new Padding(10, 6, 2, 0) });
        _amountCombo = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDown };
        toolbar.Controls.Add(_amountCombo);
        var btnCharge = new Button { Text = "선택 송장 배송비 청구", Size = new Size(160, 28) };
        btnCharge.Click += OnChargeClick;
        toolbar.Controls.Add(btnCharge);
        var btnUncharge = new Button { Text = "선택 송장 청구 해제", Size = new Size(140, 28) };
        btnUncharge.Click += OnUnchargeClick;
        toolbar.Controls.Add(btnUncharge);
        var btnReload = new Button { Text = "새로고침", Size = new Size(80, 28), Margin = new Padding(20, 3, 3, 3) };
        btnReload.Click += (_, _) => Reload();
        toolbar.Controls.Add(btnReload);

        _detailGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        };
        _detailGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "접수일자", DataPropertyName = nameof(ChannelShipmentRow.ReceivedAt), Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", DataPropertyName = nameof(ChannelShipmentRow.TrackingNo), Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "수령인", DataPropertyName = nameof(ChannelShipmentRow.Recipient), Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "주소", DataPropertyName = nameof(ChannelShipmentRow.Address), Width = 260 },
            new DataGridViewTextBoxColumn { HeaderText = "품목명(파일)", DataPropertyName = nameof(ChannelShipmentRow.ProductName), Width = 220 },
            new DataGridViewTextBoxColumn { HeaderText = "운임", DataPropertyName = nameof(ChannelShipmentRow.FreightCost), Width = 70, DefaultCellStyle = Num() },
            new DataGridViewTextBoxColumn { HeaderText = "귀속월", DataPropertyName = nameof(ChannelShipmentRow.Period), Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "배송비", DataPropertyName = nameof(ChannelShipmentRow.ChargeText), Width = 100 }
        );
        _detailGrid.CellFormatting += OnDetailCellFormatting;

        layout.Controls.Add(_summaryLabel, 0, 0);
        layout.Controls.Add(_summaryGrid, 0, 1);
        layout.Controls.Add(toolbar, 0, 2);
        layout.Controls.Add(_detailGrid, 0, 3);
        Controls.Add(layout);
    }

    /// <summary>이력을 다시 읽어 송장별 청구 상태를 새로 판정한다(청구/해제 직후에도 호출).</summary>
    private void Reload()
    {
        var keepGroup = (_summaryGrid.CurrentRow?.DataBoundItem as ChannelShipmentSummaryRow)?.GroupName;

        var trackingNos = _fileRows.Select(r => r.TrackingNo).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
        var lines = _outboundRepo.GetByTrackingNos(trackingNos);
        // 배송비 라인은 운송장이 비어 있어도 같은 묶음키면 그 송장 것이다 — 묶음키로 한 번 더 읽는다.
        var groupKeys = lines.Select(l => l.ShipmentGroupKey).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        foreach (var chunk in groupKeys.Chunk(500))
            lines.AddRange(_outboundRepo.GetByShipmentGroupKeys(chunk, Utils.LineKindScope.All));
        lines = lines.DistinctBy(l => l.Id).ToList();

        var channelNames = _channelConfigService.Load()
            .GroupBy(c => c.ChannelCode).ToDictionary(g => g.Key, g => g.First().ChannelName);
        _shipments = ChannelShipmentReconcileEngine.BuildShipments(_fileRows, lines, _shippingFeeLines.GetAllShippingKeys(), channelNames);
        var summary = ChannelShipmentReconcileEngine.Summarize(_shipments);

        _summaryGrid.DataSource = new BindingList<ChannelShipmentSummaryRow>(summary);
        var registered = _shipments.Count(s => s.IsRegistered);
        _summaryLabel.Text = $"파일 송장 {_shipments.Count:N0}건 — 이력 등록 {registered:N0}건 / 미등록 {_shipments.Count - registered:N0}건 · " +
                             $"배송비 청구 {_shipments.Count(s => s.IsCharged):N0}건 {_shipments.Sum(s => s.ChargedAmount):N0}원 · " +
                             $"미청구 {_shipments.Count(s => s.IsRegistered && !s.IsCharged):N0}건";

        var index = keepGroup == null ? -1 : summary.FindIndex(r => r.GroupName == keepGroup);
        if (index >= 0 && index < _summaryGrid.Rows.Count)
        {
            _summaryGrid.ClearSelection();
            _summaryGrid.CurrentCell = _summaryGrid.Rows[index].Cells[0];
            _summaryGrid.Rows[index].Selected = true;
        }
        RefreshDetail();
    }

    private ChannelShipmentSummaryRow? SelectedSummary => _summaryGrid.CurrentRow?.DataBoundItem as ChannelShipmentSummaryRow;

    private void RefreshDetail()
    {
        var summary = SelectedSummary;
        var rows = summary == null ? [] : _shipments
            .Where(s => s.GroupName == summary.GroupName && s.ChannelCode == summary.ChannelCode)
            .Where(s => !_chkUnchargedOnly.Checked || (s.IsRegistered && !s.IsCharged))
            .OrderBy(s => s.ReceivedAt).ThenBy(s => s.Recipient)
            .ToList();
        _detailGrid.DataSource = new BindingList<ChannelShipmentRow>(rows);
        RefreshAmountPresets(summary?.ChannelCode);
    }

    private string? _amountPresetChannel;

    /// <summary>채널이 바뀔 때만 청구액 선택지를 그 채널 설정(기본 금액 + 금액 선택지)으로 다시 채운다.</summary>
    private void RefreshAmountPresets(string? channelCode)
    {
        if (channelCode == _amountPresetChannel && _amountCombo.Items.Count > 0) return;
        _amountPresetChannel = channelCode;

        var config = string.IsNullOrEmpty(channelCode) ? null : _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelCode);
        var defaultAmount = config is { OfsShippingFeeAmount: > 0 } ? config.OfsShippingFeeAmount : 3000m;
        var presets = new List<decimal> { defaultAmount };
        presets.AddRange(ShippingFeeLineService.ParsePresets(config?.OfsShippingFeePresets ?? "3000,4500"));

        _amountCombo.Items.Clear();
        foreach (var p in presets.Distinct()) _amountCombo.Items.Add(p.ToString("N0"));
        _amountCombo.SelectedIndex = 0;
    }

    private void OnDetailCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _detailGrid.Rows[e.RowIndex].DataBoundItem is not ChannelShipmentRow row) return;
        _detailGrid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = !row.IsRegistered ? Color.Gray
            : row.IsCharged ? Color.DarkGreen : _detailGrid.DefaultCellStyle.ForeColor;
    }

    private List<ChannelShipmentRow> SelectedShipments() =>
        _detailGrid.SelectedRows.Cast<DataGridViewRow>()
            .Concat(_detailGrid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow))
            .Select(r => r.DataBoundItem as ChannelShipmentRow)
            .OfType<ChannelShipmentRow>()
            .Distinct()
            .ToList();

    /// <summary>마감확정된 달의 송장은 빼고 돌려준다(빠진 건수는 lockedCount로).</summary>
    private List<ChannelShipmentRow> ExcludeConfirmedPeriods(IEnumerable<ChannelShipmentRow> rows, out int lockedCount)
    {
        var cache = new Dictionary<(string, string), bool>();
        var result = new List<ChannelShipmentRow>();
        lockedCount = 0;
        foreach (var row in rows)
        {
            var key = (row.ChannelCode, row.Period);
            if (!cache.TryGetValue(key, out var confirmed))
                cache[key] = confirmed = !string.IsNullOrEmpty(row.Period) && _closingRepo.IsPeriodConfirmed(row.ChannelCode, row.Period);
            if (confirmed) lockedCount++;
            else result.Add(row);
        }
        return result;
    }

    private void OnChargeClick(object? sender, EventArgs e)
    {
        var selected = SelectedShipments();
        var unregistered = selected.Count(s => !s.IsRegistered);
        var targets = selected.Where(s => s.IsRegistered).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(unregistered > 0
                    ? "선택한 송장이 이력에 없습니다(미등록). 먼저 [OFS로 보내기]로 등록한 뒤 청구하세요."
                    : "배송비를 청구할 송장(행)을 아래 목록에서 선택하세요.",
                "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!decimal.TryParse(_amountCombo.Text.Replace(",", "").Replace("원", "").Trim(), out var amount) || amount <= 0)
        {
            MessageBox.Show("청구액을 0보다 큰 숫자로 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        targets = ExcludeConfirmedPeriods(targets, out var lockedCount);
        if (targets.Count == 0)
        {
            MessageBox.Show("선택한 송장은 모두 마감확정된 달의 송장입니다. 마감보드에서 [확정취소] 후 다시 하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var recharge = targets.Count(t => t.IsCharged);
        var confirmText = $"송장 {targets.Count}건에 배송비 {amount:N0}원씩(합계 {targets.Count * amount:N0}원) 청구하시겠습니까?" +
                          (recharge > 0 ? $"\n(이미 청구된 {recharge}건은 금액을 {amount:N0}원으로 바꿉니다.)" : "") +
                          (lockedCount > 0 ? $"\n(마감확정된 달의 {lockedCount}건은 제외)" : "") +
                          (unregistered > 0 ? $"\n(이력에 없는 {unregistered}건은 제외)" : "");
        if (MessageBox.Show(confirmText, "배송비 청구", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var channelNames = _channelConfigService.Load().GroupBy(c => c.ChannelCode).ToDictionary(g => g.Key, g => g.First().ChannelName);
        foreach (var byChannel in targets.GroupBy(t => t.ChannelCode))
        {
            var csku = _shippingFeeLines.EnsureShippingCsku(byChannel.Key, channelNames.GetValueOrDefault(byChannel.Key) ?? byChannel.Key, amount,
                "채널별 송장 대조 배송비 청구 — 배송비 CSKU 자동 생성");
            foreach (var shipment in byChannel)
            {
                // 송장번호로 붙은 배송비 라인이 묶음키가 다른 경우(예전 수동 추가)는 금액만 맞추려 해도 다른 키라
                // 새 줄이 생기므로, 기존 라인을 지우고 묶음키 기준으로 다시 넣는다.
                if (shipment.ChargeLineIds.Count > 0) _outboundRepo.DeleteByIds(shipment.ChargeLineIds);
                _outboundRepo.UpsertShippingFeeLine(shipment.Anchor!, csku.CskuCode, ShippingFeeLineService.DisplayName(csku), amount, "송장대조 배송비 청구");
            }
        }

        Reload();
        _summaryLabel.Text = $"송장 {targets.Count}건에 배송비 {targets.Count * amount:N0}원을 청구했습니다. — " + _summaryLabel.Text;
    }

    private void OnUnchargeClick(object? sender, EventArgs e)
    {
        var targets = SelectedShipments().Where(s => s.IsCharged).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("청구 해제할 송장(배송비 '청구' 표시된 행)을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        targets = ExcludeConfirmedPeriods(targets, out var lockedCount);
        if (targets.Count == 0)
        {
            MessageBox.Show("선택한 송장은 모두 마감확정된 달의 송장입니다. 마감보드에서 [확정취소] 후 다시 하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var total = targets.Sum(t => t.ChargedAmount);
        var confirmText = $"송장 {targets.Count}건의 배송비 청구({total:N0}원)를 해제하시겠습니까? 배송비 라인이 이력에서 삭제됩니다." +
                          (lockedCount > 0 ? $"\n(마감확정된 달의 {lockedCount}건은 제외)" : "");
        if (MessageBox.Show(confirmText, "청구 해제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _outboundRepo.DeleteByIds(targets.SelectMany(t => t.ChargeLineIds));
        Reload();
        _summaryLabel.Text = $"송장 {targets.Count}건의 배송비 청구 {total:N0}원을 해제했습니다. — " + _summaryLabel.Text;
    }
}
