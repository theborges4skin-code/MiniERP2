using System.Text.Json;

namespace MiniERP2.Utils;

/// <summary>
/// 택배사 출력 양식(엑셀 헤더 → OfsOrderItem 속성)의 한 줄입니다.
/// </summary>
public record HeaderMappingEntry(string Header, string PropertyName);

/// <summary>
/// CourierMaster.HeaderMappingJson을 읽고 쓴다. 샘플 양식에서 불러온 헤더 순서 그대로 출력해야
/// 택배사 프로그램이 그 파일을 인식할 수 있으므로(요청사항), 순서가 보장되는 JSON 배열로 저장한다
/// — JSON 객체(Dictionary)의 키 순서는 사양상 보장되지 않아 이전엔 순서가 어긋날 수 있었다.
/// 예전에 Dictionary 형식으로 저장된 데이터도 그대로 읽을 수 있게 폴백을 둔다.
/// </summary>
public static class CourierHeaderMapping
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static List<HeaderMappingEntry> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<HeaderMappingEntry>>(json, Options);
            if (list != null) return list;
        }
        catch (JsonException)
        {
            // 새 형식(배열)이 아니면 구버전 형식(Dictionary)으로 시도한다.
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options);
            if (dict != null) return dict.Select(kv => new HeaderMappingEntry(kv.Key, kv.Value)).ToList();
        }
        catch (JsonException)
        {
            // 손상된 데이터면 빈 목록으로 시작한다.
        }

        return [];
    }

    public static string Serialize(IEnumerable<HeaderMappingEntry> entries) => JsonSerializer.Serialize(entries.ToList(), Options);
}
