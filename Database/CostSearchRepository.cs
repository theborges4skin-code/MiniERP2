using Microsoft.Data.Sqlite;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Database;

/// <summary>
/// 메인 화면 "빠른 원가검색" 전용 조회. 마스터SKU(ItemTable)와 채널별 CSKU(ChannelSkuTable)를
/// 한 번에 훑어 적용 원가와 그 원가의 최종 수정일까지 묶어 돌려준다.
/// 타이핑할 때마다 호출되므로 화면에 뿌릴 만큼만(LIMIT) 잘라 가져온다.
/// </summary>
public class CostSearchRepository
{
    /// <summary>CSKU 개별원가 변경을 ChannelSkuFieldHistory에 남길 때 쓰는 필드명(ChannelSkuRepository와 동일해야 한다).</summary>
    private const string CostOverrideFieldName = "제조원가(개별관리)";

    public const int DefaultLimit = 60;

    public List<CostSearchResult> Search(string query, int limit = DefaultLimit)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return new List<CostSearchResult>();

        using var connection = SqliteConnectionFactory.OpenConnection();
        // LIKE로는 초성 검색을 못 하므로 화면 검색창과 같은 판정(KoreanSearch)을 SQL 함수로 등록해 쓴다.
        connection.CreateFunction("kmatch", (string? text, string? q) => KoreanSearch.Matches(text, q), isDeterministic: true);
        var results = new List<CostSearchResult>();
        results.AddRange(SearchMasterSkus(connection, query, limit));
        results.AddRange(SearchCskus(connection, query, limit));

        // 두 표에서 따로 가져온 결과를 "얼마나 정확히 맞았는지"로 다시 세운다. 코드 완전일치 →
        // 코드 앞부분 일치 → 나머지 순이고, 같은 점수면 마스터SKU를 CSKU보다 앞에 둔다.
        return results
            .OrderBy(r => MatchRank(r, query))
            .ThenBy(r => r.IsCsku ? 1 : 0)
            .ThenBy(r => r.Code, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static int MatchRank(CostSearchResult result, string query)
    {
        if (result.Code.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (result.Code.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (result.Code.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static List<CostSearchResult> SearchMasterSkus(SqliteConnection connection, string query, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.Sku, i.ItemName, i.CostPrice, i.Unit,
                   (SELECT MAX(h.ChangedAt) FROM ItemCostHistory h WHERE h.Sku = i.Sku)
            FROM ItemTable i
            WHERE kmatch(i.Sku, $query)
               OR kmatch(i.ItemName, $query)
            ORDER BY i.Sku
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<CostSearchResult>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CostSearchResult
            {
                IsCsku = false,
                Code = reader.GetString(0),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                CostPrice = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                Unit = reader.IsDBNull(3) ? "kg" : reader.GetString(3),
                CostChangedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            });
        }
        return results;
    }

    private static List<CostSearchResult> SearchCskus(SqliteConnection connection, string query, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.CskuCode, c.Msku, c.CostPriceOverride, c.Unit, c.InvoiceDisplayName, c.UpdatedAt,
                   ch.ChannelName, c.ChannelCode,
                   i.CostPrice, i.ItemName,
                   (SELECT MAX(fh.ChangedAt) FROM ChannelSkuFieldHistory fh
                     WHERE fh.ChannelCode = c.ChannelCode AND fh.CskuCode = c.CskuCode
                       AND fh.FieldName = $costFieldName),
                   (SELECT MAX(h.ChangedAt) FROM ItemCostHistory h WHERE h.Sku = c.Msku)
            FROM ChannelSkuTable c
            LEFT JOIN SalesChannelTable ch ON ch.ChannelCode = c.ChannelCode
            LEFT JOIN ItemTable i ON i.Sku = c.Msku
            WHERE kmatch(c.CskuCode, $query)
               OR kmatch(c.Msku, $query)
               OR kmatch(c.InvoiceDisplayName, $query)
               OR kmatch(i.ItemName, $query)
            ORDER BY c.CskuCode
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$costFieldName", CostOverrideFieldName);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<CostSearchResult>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var overrideCost = reader.IsDBNull(2) ? (decimal?)null : reader.GetDecimal(2);
            var invoiceName = reader.IsDBNull(4) ? null : reader.GetString(4);
            var updatedAt = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
            var channelName = reader.IsDBNull(6) ? null : reader.GetString(6);
            var channelCode = reader.GetString(7);
            var masterCost = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8);
            var masterName = reader.IsDBNull(9) ? null : reader.GetString(9);
            var overrideChangedAt = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10);
            var masterChangedAt = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11);

            results.Add(new CostSearchResult
            {
                IsCsku = true,
                Code = reader.GetString(0),
                Msku = reader.GetString(1),
                Name = !string.IsNullOrWhiteSpace(masterName) ? masterName! : (invoiceName ?? string.Empty),
                ChannelName = string.IsNullOrWhiteSpace(channelName) ? channelCode : channelName,
                // 발주/마감의 원가 우선순위와 같은 규칙(CostResolver) — 여기서는 매입처 매입가가
                // 개입하지 않는 "품목 자체의 원가"만 보므로 개별원가 > 마스터 원가 두 단계다.
                CostPrice = overrideCost ?? masterCost,
                IsCostOverride = overrideCost.HasValue,
                // 개별원가면 그 값이 바뀐 시점(이력이 없으면 = 등록 이후 그대로이므로 최종 저장일),
                // 마스터 연동이면 마스터SKU 원가가 바뀐 시점이 곧 이 CSKU 원가가 바뀐 시점이다.
                CostChangedAt = overrideCost.HasValue ? (overrideChangedAt ?? updatedAt) : masterChangedAt,
                Unit = reader.IsDBNull(3) ? "kg" : reader.GetString(3),
            });
        }
        return results;
    }
}
