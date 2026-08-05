using Microsoft.Data.Sqlite;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Services;

/// <summary>
/// 이력채널 이관(샘플발송이력관리_개발기획서.md §5) — 샘플등 채널에 쌓인 비매출 이력을 신규 정식
/// 거래처 채널로 옮긴다. 정상 거래(LineKind='') 라인은 이관 대상이 아니다(호출 측이 걸러야 함) —
/// 이 서비스는 그 가드를 다시 검사하지 않고, 넘어온 라인을 그대로 처리한다.
/// </summary>
public class ChannelTransferService
{
    private readonly OutboundRepository _outboundRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly PartnerClosingRepository _closingRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();

    public ChannelTransferResult TransferChannel(List<OutboundDetail> selectedLines, ChannelTransferRequest request)
    {
        var channelNames = _channelRepo.GetAll().ToDictionary(c => c.ChannelCode, c => c.ChannelName);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var result = new ChannelTransferResult();

        // §5.5 가드: 이미 마감확정된 귀속월의 라인은 소급 반영 사고를 막기 위해 이관 대상에서
        // 제외한다(사용자 확인 완료). 귀속월 판정은 기존 마감보드와 동일 규칙(ClosingPeriod 우선,
        // 없으면 ConfirmedAt/CreatedAt의 연월).
        var toTransfer = new List<OutboundDetail>();
        var alreadyClosed = new List<OutboundDetail>();
        foreach (var line in selectedLines)
        {
            var period = !string.IsNullOrEmpty(line.ClosingPeriod)
                ? line.ClosingPeriod
                : (line.ConfirmedAt ?? line.CreatedAt).ToString("yyyy-MM");
            if (_closingRepo.IsPeriodConfirmed(line.ChannelCode, period)) alreadyClosed.Add(line);
            else toTransfer.Add(line);
        }

        foreach (var line in toTransfer)
        {
            var originChannelName = channelNames.GetValueOrDefault(line.ChannelCode, line.ChannelCode);
            var csku = !string.IsNullOrEmpty(line.CskuCode) ? line.CskuCode : line.MskuCode;
            var masterSku = _channelSkuRepo.ResolveMasterSku(line.ChannelCode, csku);

            // 대상 채널에 동일 마스터SKU의 CSKU가 있으면 그것으로 매핑, 없으면 자동 생성한다(§2 D4).
            var existingTargetCsku = _channelSkuRepo.GetAllByChannel(request.TargetChannelCode)
                .FirstOrDefault(c => c.Msku.Equals(masterSku, StringComparison.OrdinalIgnoreCase));

            string targetCskuCode;
            decimal? newSupplyPrice = null;
            if (existingTargetCsku != null)
            {
                targetCskuCode = existingTargetCsku.CskuCode;
                if (request.UpdateSupplyPriceFromTarget) newSupplyPrice = existingTargetCsku.SupplyPrice;
            }
            else
            {
                targetCskuCode = CskuCodeGenerator.BuildDefault(request.TargetChannelName, masterSku);
                _channelSkuRepo.CreateIfNew(request.TargetChannelCode, targetCskuCode, masterSku, 0m, line.ProductName);
            }

            if (request.ManualSupplyPrice.HasValue) newSupplyPrice = request.ManualSupplyPrice;

            var remark = $"[이관 {today} {originChannelName}→{request.TargetChannelName}]";
            if (alreadyClosed.Count > 0)
                remark += $" 이전 이력(마감확정분)은 {originChannelName} 채널에 있음";

            try
            {
                _outboundRepo.TransferLineToChannel(
                    line.Id, request.TargetChannelCode, targetCskuCode,
                    newSupplyPrice,
                    request.ConvertToSaleTransaction ? "" : null,
                    request.ForcedClosingPeriod,
                    remark);
                result.TransferredCount++;
            }
            catch (SqliteException)
            {
                // UNIQUE(ShipmentGroupKey, MskuCode) 충돌 — 대상 채널에 같은 그룹의 같은 CSKU 라인이
                // 이미 있다. 전량 차단은 과하므로 이 건만 스킵하고 사유를 결과창에 알린다.
                result.ConflictSkipped.Add((line.Id, $"{line.ProductName} — 대상 채널에 같은 발송그룹의 동일 CSKU가 이미 있음"));
            }
        }

        if (alreadyClosed.Count > 0)
        {
            var note = $" [이관참고 {today}] 마감확정건이라 {request.TargetChannelName}으로 이관되지 않음 — 이후 이력은 {request.TargetChannelName} 채널 참조";
            foreach (var line in alreadyClosed) _outboundRepo.AppendRemark(line.Id, note);
            result.AlreadyClosedSkipped = alreadyClosed.Count;
        }

        return result;
    }
}

public class ChannelTransferRequest
{
    public required string TargetChannelCode { get; init; }
    public required string TargetChannelName { get; init; }
    public bool UpdateSupplyPriceFromTarget { get; init; }
    public decimal? ManualSupplyPrice { get; init; }
    public bool ConvertToSaleTransaction { get; init; }
    public string? ForcedClosingPeriod { get; init; }
}

public class ChannelTransferResult
{
    public int TransferredCount { get; set; }
    public int AlreadyClosedSkipped { get; set; }
    public List<(long Id, string Reason)> ConflictSkipped { get; set; } = [];
}
