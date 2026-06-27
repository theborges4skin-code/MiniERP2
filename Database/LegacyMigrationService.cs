using System.Text.Json;
using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 구버전 MiniERP(V3, C#/SQLite)의 운영 DB에 쌓여있던 채널 설정·매핑 규칙·마스터SKU·
/// 채널별 납품가를 MiniERP2 스키마로 1회성 이전합니다. 이 DB는 과거 FormLegacyMigrator가
/// Python SalesManagerV2의 exact_rules.json 등을 이미 이관해둔 결과물이라, 사실상
/// "파이썬에서 만든 매핑조건"의 최종 산출물이다.
/// </summary>
public class LegacyMigrationService
{
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();

    public LegacyMigrationResult Migrate(string legacyDbPath)
    {
        var result = new LegacyMigrationResult();
        using var connection = new SqliteConnection($"Data Source={legacyDbPath};Mode=ReadOnly");
        connection.Open();

        var channelConfigs = _channelConfigService.Load();

        MigrateChannels(connection, channelConfigs, result);
        MigrateItems(connection, result);
        MigrateChannelSkus(connection, result);
        MigrateExactAndExceptionRules(connection, MappingRuleType.Exact, "RuleExactTable", result);
        MigrateExactAndExceptionRules(connection, MappingRuleType.Exception, "RuleExceptionTable", result);
        MigrateTempSkus(connection, result);
        MigrateConditionRules(connection, result);

        _channelConfigService.Save(channelConfigs);
        return result;
    }

    // ===================== 채널 + 필드매핑 + 보조소스 =====================

    private void MigrateChannels(SqliteConnection connection, List<ChannelConfig> channelConfigs, LegacyMigrationResult result)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SalesChannelTable";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var channelCode = reader.GetString(reader.GetOrdinal("ChannelCode"));
            var channelName = reader.GetString(reader.GetOrdinal("ChannelName"));

            _salesChannelRepository.Upsert(new SalesChannel { ChannelCode = channelCode, ChannelName = channelName });

            var config = channelConfigs.FirstOrDefault(c => c.ChannelCode == channelCode);
            if (config == null)
            {
                config = new ChannelConfig { ChannelCode = channelCode, ChannelName = channelName };
                channelConfigs.Add(config);
            }

            config.ChannelType = GuessChannelType(GetStringOrNull(reader, "ChannelTypeLabel") ?? GetStringOrNull(reader, "ChannelType"));
            config.ExchangeRate = GetDecimalOrDefault(reader, "ExchangeRate", 1m);

            var globalHeaderRow = GetIntOrDefault(reader, "GlobalHeaderRow", 1);
            var mappingJson = GetStringOrNull(reader, "MappingJson");
            var (orderMappings, settlementMappings) = ParseMappingJson(mappingJson, globalHeaderRow);

            ApplyFixedOrHeaderColumn(orderMappings, StdField.Recipient, GetStringOrNull(reader, "ReceiverName"), GetStringOrNull(reader, "HeaderReceiver"), globalHeaderRow);
            ApplyFixedOrHeaderColumn(orderMappings, StdField.Phone, GetStringOrNull(reader, "Phone"), GetStringOrNull(reader, "HeaderPhone"), globalHeaderRow);
            ApplyFixedOrHeaderColumn(orderMappings, StdField.Address, GetStringOrNull(reader, "Address"), GetStringOrNull(reader, "HeaderAddress"), globalHeaderRow);

            config.OrderFieldMappings = orderMappings;
            config.SettlementFieldMappings = settlementMappings;
            config.GrowthAuxSources = ParseGrowthAuxSourcesJson(GetStringOrNull(reader, "GrowthAuxSourcesJson"));

            result.ChannelsImported++;
        }
    }

    private static void ApplyFixedOrHeaderColumn(Dictionary<StdField, FieldMapping> mappings, StdField field, string? fixedValue, string? headerColumn, int headerRow)
    {
        if (!string.IsNullOrWhiteSpace(fixedValue))
        {
            mappings[field] = new FieldMapping { FixedValue = fixedValue };
        }
        else if (!string.IsNullOrWhiteSpace(headerColumn))
        {
            mappings[field] = new FieldMapping { HeaderRow = headerRow, Column = headerColumn };
        }
    }

    /// <summary>
    /// 레거시는 발주서/정산서 매핑을 구분하지 않고 STD_* 필드 하나에 다 담아뒀으므로,
    /// 주문 관련 필드는 발주서 매핑에, 정산 관련 필드는 정산서 매핑에 동시에 채워준다.
    /// </summary>
    private static (Dictionary<StdField, FieldMapping> Order, Dictionary<StdField, FieldMapping> Settlement) ParseMappingJson(string? json, int defaultHeaderRow)
    {
        var order = new Dictionary<StdField, FieldMapping>();
        var settlement = new Dictionary<StdField, FieldMapping>();
        if (string.IsNullOrWhiteSpace(json)) return (order, settlement);

        using var doc = JsonDocument.Parse(json);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!TryTranslateStdField(property.Name, out var stdField, out var isOrderField, out var isSettlementField)) continue;

            var col = property.Value.TryGetProperty("col", out var colEl) ? colEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(col)) continue;

            // "보조소스 사용" 등 실제 헤더명이 아닌 안내문구는 컬럼 매핑 대상이 아니다(보조소스 JOIN으로 채워짐).
            if (col.Contains("보조소스")) continue;

            var sheetName = property.Value.TryGetProperty("sheet_name", out var sheetEl) && sheetEl.ValueKind == JsonValueKind.String ? sheetEl.GetString() : null;
            var headerRow = property.Value.TryGetProperty("header_row", out var rowEl) && rowEl.ValueKind == JsonValueKind.Number ? rowEl.GetInt32() : defaultHeaderRow;

            var mapping = new FieldMapping { SheetName = sheetName, HeaderRow = headerRow, Column = col };
            if (isOrderField) order[stdField] = mapping;
            if (isSettlementField) settlement[stdField] = mapping;
        }

        return (order, settlement);
    }

    /// <summary>
    /// 레거시 STD_* 필드 이름을 MiniERP2의 StdField로 변환합니다.
    /// 대응되는 필드가 없는 항목(STD_AMT, STD_TAX_*, STD_DATE, STD_EXTRA* 등)은 변환하지 않고 버립니다.
    /// </summary>
    private static bool TryTranslateStdField(string legacyName, out StdField stdField, out bool isOrderField, out bool isSettlementField)
    {
        switch (legacyName)
        {
            case "STD_PRODUCT_NAME": stdField = StdField.ProductName; isOrderField = true; isSettlementField = true; return true;
            case "STD_OPTION": stdField = StdField.OptionName; isOrderField = true; isSettlementField = true; return true;
            case "STD_QTY": stdField = StdField.Quantity; isOrderField = true; isSettlementField = true; return true;
            case "STD_ORDER_ID": stdField = StdField.ProductNo; isOrderField = true; isSettlementField = false; return true; // OrderLoader가 ProductNo를 주문번호로 사용
            case "STD_SETTLEMENT": stdField = StdField.SettlementAmount; isOrderField = false; isSettlementField = true; return true;
            case "STD_SHIPPING": stdField = StdField.ShippingFee; isOrderField = false; isSettlementField = true; return true;
            case "STD_FEE": stdField = StdField.HandlingFee; isOrderField = false; isSettlementField = true; return true;
            default:
                stdField = default;
                isOrderField = false;
                isSettlementField = false;
                return false;
        }
    }

    private static List<GrowthAuxSource> ParseGrowthAuxSourcesJson(string? json)
    {
        var sources = new List<GrowthAuxSource>();
        if (string.IsNullOrWhiteSpace(json)) return sources;

        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var targetFieldName = element.TryGetProperty("target_std_field", out var t) ? t.GetString() : null;
            if (targetFieldName == null || !TryTranslateStdField(targetFieldName, out var stdField, out _, out _)) continue;

            sources.Add(new GrowthAuxSource
            {
                Enabled = element.TryGetProperty("enabled", out var en) && string.Equals(en.GetString(), "Y", StringComparison.OrdinalIgnoreCase),
                TargetStdField = stdField,
                SheetName = element.TryGetProperty("sheet_name", out var sn) ? sn.GetString() : null,
                HeaderRow = element.TryGetProperty("header_row", out var hr) && hr.ValueKind == JsonValueKind.Number ? hr.GetInt32() : 1,
                KeyHeader = element.TryGetProperty("key_header", out var kh) ? kh.GetString() : null,
                ValueHeader = element.TryGetProperty("value_header", out var vh) ? vh.GetString() : null,
                OutCol = element.TryGetProperty("out_col", out var oc) ? oc.GetString() : null,
            });
        }

        return sources;
    }

    private static ChannelType GuessChannelType(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return ChannelType.General;

        if (label.Contains("그로스")) return ChannelType.CoupangGrowth;
        if (label.Contains("로켓")) return ChannelType.CoupangRocket;
        if (label.Contains("쿠팡")) return ChannelType.CoupangGeneral;
        if (label.Contains("11번가") || label.Contains("11st", StringComparison.OrdinalIgnoreCase)) return ChannelType.ElevenStreet;
        if (label.Contains("일본") && label.Contains("아마존")) return ChannelType.AmazonJp;
        if (label.Contains("아마존") || label.Contains("amazon", StringComparison.OrdinalIgnoreCase)) return ChannelType.AmazonUs;
        if (label.Contains("거래처") || label.Contains("위탁")) return ChannelType.Partner;
        return ChannelType.General;
    }

    // ===================== 마스터SKU =====================

    private void MigrateItems(SqliteConnection connection, LegacyMigrationResult result)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SKU, ItemName, CostPrice, Extra1, Extra2, Extra3 FROM ItemTable";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var sku = GetStringOrNull(reader, "SKU");
            if (string.IsNullOrWhiteSpace(sku)) continue;

            _itemRepository.Upsert(new ItemModel
            {
                Sku = sku,
                ItemName = GetStringOrNull(reader, "ItemName") ?? sku,
                CostPrice = GetDecimalOrDefault(reader, "CostPrice", 0m),
                ProductGroup = GetStringOrNull(reader, "Extra1"),
                Reserve1 = GetStringOrNull(reader, "Extra2"),
                Reserve2 = GetStringOrNull(reader, "Extra3"),
            });
            result.ItemsImported++;
        }
    }

    private void MigrateTempSkus(SqliteConnection connection, LegacyMigrationResult result)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TempSku, ItemGroup, ItemName, CostPrice FROM RuleTempSkuTable";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var sku = GetStringOrNull(reader, "TempSku");
            if (string.IsNullOrWhiteSpace(sku)) continue;

            // 레거시의 "임시 SKU"는 매핑 규칙이 아니라 임시로 등록해둔 마스터 품목 그 자체였다.
            _itemRepository.Upsert(new ItemModel
            {
                Sku = sku,
                ItemName = GetStringOrNull(reader, "ItemName") ?? sku,
                CostPrice = GetDecimalOrDefault(reader, "CostPrice", 0m),
                ProductGroup = GetStringOrNull(reader, "ItemGroup"),
            });
            result.TempSkusImported++;
        }
    }

    // ===================== 채널별 납품가 =====================

    private void MigrateChannelSkus(SqliteConnection connection, LegacyMigrationResult result)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, SkuName, SupplyPrice FROM ChannelSkuTable";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var channelCode = GetStringOrNull(reader, "ChannelCode");
            var skuName = GetStringOrNull(reader, "SkuName");
            if (string.IsNullOrWhiteSpace(channelCode) || string.IsNullOrWhiteSpace(skuName)) continue;

            _channelSkuRepository.Upsert(new ChannelSkuModel
            {
                ChannelCode = channelCode,
                CskuCode = skuName, // 레거시 DB에는 CSKU 코드 개념이 없었으므로 마스터SKU를 그대로 코드로 사용
                Msku = skuName,
                SupplyPrice = GetDecimalOrDefault(reader, "SupplyPrice", 0m),
            });
            result.ChannelSkusImported++;
        }
    }

    // ===================== 매핑 규칙(1:1, 예외) =====================

    private void MigrateExactAndExceptionRules(SqliteConnection connection, MappingRuleType ruleType, string legacyTableName, LegacyMigrationResult result)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ChannelCode, LegacyKey, TargetSku FROM {legacyTableName}";
        using var reader = command.ExecuteReader();

        var rulesByChannel = new Dictionary<string, List<MappingRule>>();

        while (reader.Read())
        {
            var channelCode = GetStringOrNull(reader, "ChannelCode");
            var legacyKey = GetStringOrNull(reader, "LegacyKey");
            var targetSku = GetStringOrNull(reader, "TargetSku");
            if (string.IsNullOrWhiteSpace(channelCode) || string.IsNullOrWhiteSpace(legacyKey) || targetSku == null) continue;

            // LegacyKey는 "채널코드::실제키" 형태이므로 채널코드 접두사를 떼어낸다.
            var prefix = channelCode + "::";
            var key = legacyKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? legacyKey[prefix.Length..] : legacyKey;
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (!rulesByChannel.TryGetValue(channelCode, out var list))
            {
                list = new List<MappingRule>();
                rulesByChannel[channelCode] = list;
            }
            list.Add(new MappingRule { Key = key, TargetSku = targetSku });
            result.RulesImported++;
        }

        foreach (var (channelCode, rules) in rulesByChannel)
        {
            // 기존에 이미 같은 채널에 저장된 규칙이 있다면 병합한다(덮어쓰지 않음).
            var existing = _mappingRepository.GetRules(ruleType, channelCode);
            var merged = existing.Concat(rules)
                .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();
            _mappingRepository.SaveRules(ruleType, channelCode, merged);
        }
    }

    // ===================== 조건부 매핑(다중 AND/OR) =====================

    /// <summary>
    /// 레거시 RuleConditionTable+RuleConditionDetailTable(485건+상세750건)을 이관합니다.
    /// 레거시 RuleId는 문자열("9A2C5962_COND_00001")이라 두 테이블을 문자열 RuleId로 직접 조인한다.
    /// 1회성 도구라 재실행하면 중복으로 추가될 수 있으니, 같은 DB로 두 번 가져오지 않도록 주의.
    /// </summary>
    private void MigrateConditionRules(SqliteConnection connection, LegacyMigrationResult result)
    {
        using var ruleCommand = connection.CreateCommand();
        ruleCommand.CommandText = "SELECT RuleId, ChannelCode, TargetSku FROM RuleConditionTable";
        using var ruleReader = ruleCommand.ExecuteReader();

        var legacyRules = new List<(string LegacyRuleId, string ChannelCode, string TargetSku)>();
        while (ruleReader.Read())
        {
            var legacyRuleId = GetStringOrNull(ruleReader, "RuleId");
            var channelCode = GetStringOrNull(ruleReader, "ChannelCode");
            var targetSku = GetStringOrNull(ruleReader, "TargetSku");
            if (string.IsNullOrWhiteSpace(legacyRuleId) || string.IsNullOrWhiteSpace(channelCode) || targetSku == null) continue;

            legacyRules.Add((legacyRuleId, channelCode, targetSku));
        }
        ruleReader.Close();

        foreach (var (legacyRuleId, channelCode, targetSku) in legacyRules)
        {
            using var detailCommand = connection.CreateCommand();
            detailCommand.CommandText = "SELECT HeaderName, Operator, TargetValue, Logic FROM RuleConditionDetailTable WHERE RuleId = $ruleId";
            detailCommand.Parameters.AddWithValue("$ruleId", legacyRuleId);
            using var detailReader = detailCommand.ExecuteReader();

            var details = new List<MappingConditionDetail>();
            var summaryParts = new List<string>();
            while (detailReader.Read())
            {
                var headerName = GetStringOrNull(detailReader, "HeaderName");
                var op = GetStringOrNull(detailReader, "Operator");
                var targetValue = GetStringOrNull(detailReader, "TargetValue");
                var logic = GetStringOrNull(detailReader, "Logic");
                if (headerName == null || op == null || targetValue == null) continue;
                if (!TryTranslateLegacyHeaderName(headerName, out var stdField)) continue;
                if (!TryTranslateOperator(op, out var conditionOperator)) continue;

                details.Add(new MappingConditionDetail
                {
                    HeaderField = stdField,
                    Operator = conditionOperator,
                    TargetValue = targetValue,
                    Logic = string.Equals(logic, "OR", StringComparison.OrdinalIgnoreCase) ? ConditionLogic.Or : ConditionLogic.And,
                });
                summaryParts.Add($"{headerName} {op} {targetValue}");
            }

            if (details.Count == 0) continue; // 번역 가능한 조건이 하나도 없으면 의미 없는 규칙이므로 건너뛴다.

            var summaryKey = string.Join(" / ", summaryParts);
            _mappingRepository.AddConditionRuleWithDetails(channelCode, summaryKey, targetSku, details);
            result.ConditionRulesImported++;
        }
    }

    /// <summary>
    /// 레거시 조건 상세의 HeaderName(엑셀 헤더 텍스트)을 StdField로 변환합니다.
    /// 대응되는 표준 필드가 없으면(예: 알 수 없는 헤더) false를 반환해 해당 조건을 건너뛰게 합니다.
    /// </summary>
    private static bool TryTranslateLegacyHeaderName(string headerName, out StdField stdField)
    {
        if (headerName.Contains("옵션")) { stdField = StdField.OptionName; return true; }
        if (headerName.Contains("상품명") || headerName.Contains("품목명")) { stdField = StdField.ProductName; return true; }
        if (headerName.Contains("수량")) { stdField = StdField.Quantity; return true; }
        if (headerName.Contains("주문")) { stdField = StdField.ProductNo; return true; }
        if (headerName.Contains("수령") || headerName.Contains("받는")) { stdField = StdField.Recipient; return true; }
        if (headerName.Contains("연락처") || headerName.Contains("전화")) { stdField = StdField.Phone; return true; }
        if (headerName.Contains("주소")) { stdField = StdField.Address; return true; }

        stdField = default;
        return false;
    }

    private static bool TryTranslateOperator(string legacyOperator, out ConditionOperator conditionOperator)
    {
        switch (legacyOperator)
        {
            case "contains": conditionOperator = ConditionOperator.Contains; return true;
            case "not_contains": conditionOperator = ConditionOperator.NotContains; return true;
            case "==": conditionOperator = ConditionOperator.Equals; return true;
            default:
                conditionOperator = default;
                return false;
        }
    }

    // ===================== 유틸 =====================

    private static string? GetStringOrNull(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static decimal GetDecimalOrDefault(SqliteDataReader reader, string columnName, decimal defaultValue)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? defaultValue : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static int GetIntOrDefault(SqliteDataReader reader, string columnName, int defaultValue)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal)) return defaultValue;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            long l => (int)l,
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }
}

/// <summary>
/// 레거시 마이그레이션 1회 실행 결과 요약입니다.
/// </summary>
public class LegacyMigrationResult
{
    public int ChannelsImported { get; set; }
    public int ItemsImported { get; set; }
    public int ChannelSkusImported { get; set; }
    public int RulesImported { get; set; }
    public int TempSkusImported { get; set; }
    public int ConditionRulesImported { get; set; }
}
