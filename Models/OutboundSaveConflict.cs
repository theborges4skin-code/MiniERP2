namespace MiniERP2.Models;

/// <summary>
/// OutboundRepository.SaveOutbound가 (ShipmentGroupKey, MskuCode) UNIQUE 충돌로 기존 행을
/// 덮어쓰기 직전에 감지한 경우 1건씩 담는다. 기존 행의 OrderNo와 새로 저장하려는 OrderNo가
/// 다르면 서로 다른 주문이 같은 키로 충돌한 것이므로(조용한 덮어쓰기 방지용 경고), 호출 측
/// (OfsForm 등)이 사용자에게 알릴 수 있게 반환한다.
/// </summary>
public class OutboundSaveConflict
{
    public string ShipmentGroupKey { get; set; } = string.Empty;
    public string MskuCode { get; set; } = string.Empty;
    public string ExistingOrderNo { get; set; } = string.Empty;
    public string NewOrderNo { get; set; } = string.Empty;
}
