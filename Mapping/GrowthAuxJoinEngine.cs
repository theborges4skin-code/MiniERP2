using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 기획서 5.4절 '보조소스 JOIN(growth_aux_sources)'의 키/값 결합 로직.
/// 메인 시트 외 보조 시트(입출고비/배송비 등)를 키 컬럼 기준으로 메인 데이터에 매칭한다.
/// 엑셀 파싱은 DataLoaders.SettlementLoader가 담당하고, 이 클래스는 그 결과를 받아 처리하는
/// 순수 로직만 다룬다.
/// </summary>
public static class GrowthAuxJoinEngine
{
    /// <summary>
    /// 보조 시트에서 읽은 (키, 값) 쌍들을 키 기준으로 합산하여 맵으로 만듭니다.
    /// 같은 키가 여러 행에 걸쳐 나타나는 경우(예: 옵션ID당 비용이 여러 줄로 나뉜 경우)를 대비해 합산합니다.
    /// </summary>
    public static Dictionary<string, decimal> BuildValueMap(IEnumerable<(string Key, decimal Value)> rows)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in rows)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            map[key] = map.TryGetValue(key, out var existing) ? existing + value : value;
        }
        return map;
    }

    /// <summary>
    /// JOIN으로 찾은 값을 대상 표준 필드에 맞는 SettlementData의 금액 필드에 반영합니다.
    /// 정산/배송비/입출고비 외의 필드(상품명 등)는 비용 JOIN 대상이 아니므로 무시합니다.
    /// </summary>
    public static void Apply(SettlementData data, StdField targetStdField, decimal value)
    {
        switch (targetStdField)
        {
            case StdField.SettlementAmount:
                data.Settlement = value;
                break;
            case StdField.ShippingFee:
                data.Shipping = value;
                break;
            case StdField.HandlingFee:
                data.Fee = value;
                break;
        }
    }
}
