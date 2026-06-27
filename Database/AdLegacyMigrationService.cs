using System.Text.Json;
using System.Text.Json.Serialization;
using MiniERP2.Config;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// SalesManagerV2(레거시 Python 광고매핑 도구)의 config 폴더에 있는 JSON 규칙 파일
/// (ad_condition_rules.json/ad_exception_rules.json/ad_channels_config.json)을 MiniERP2의
/// 광고매핑 DB로 이관합니다. 레거시 조건부 규칙은 원본 헤더 텍스트(예: "광고집행 상품명")를
/// 그대로 들고 있어 표준 필드(AdStdField)로 바로 매칭되지 않으므로, ad_channels_config.json의
/// 채널별 헤더 매핑을 모두 모아 "원본 헤더 → AdStdField" 역방향 사전을 만들어 최대한 번역하고,
/// 사전에 없는 헤더는 ProductName으로 잠정 배정합니다(사용자가 '조건부 매핑(상세)' 탭에서
/// 드롭다운만 바로잡으면 되는 정도의 작업으로 줄이는 것이 목표 — 완전 자동화는 레거시 헤더가
/// 표준화되어 있지 않아 애초에 불가능합니다).
/// </summary>
public class AdLegacyMigrationService
{
    private readonly AdMappingRepository _adMappingRepository;
    private readonly SalesChannelRepository _salesChannelRepository;
    private readonly ChannelConfigService _channelConfigService;

    public AdLegacyMigrationService(AdMappingRepository? adMappingRepository = null, SalesChannelRepository? salesChannelRepository = null, ChannelConfigService? channelConfigService = null)
    {
        _adMappingRepository = adMappingRepository ?? new AdMappingRepository();
        _salesChannelRepository = salesChannelRepository ?? new SalesChannelRepository();
        _channelConfigService = channelConfigService ?? new ChannelConfigService();
    }

    /// <summary>
    /// configFolderPath의 ad_condition_rules.json/ad_exception_rules.json은 모두
    /// targetChannelCode 채널로 이관합니다(레거시 파일엔 채널코드가 없는 공용 규칙이기 때문 —
    /// 이관 후 데이터 관리창/광고매핑창에서 다른 채널로 복제하면 됩니다). ad_channels_config.json은
    /// channel_name이 정확히 일치하는 기존 채널에만 필드 매핑을 채워줍니다(없는 채널은 자동
    /// 생성하지 않고 결과에 안내만 남깁니다 — 채널 목록을 임의로 늘리지 않기 위함).
    /// </summary>
    public AdLegacyMigrationResult Migrate(string configFolderPath, string targetChannelCode)
    {
        var result = new AdLegacyMigrationResult();
        var headerToFieldMap = BuildHeaderToFieldMap(configFolderPath, result);

        MigrateConditionRules(configFolderPath, targetChannelCode, headerToFieldMap, result);
        MigrateExceptionRules(configFolderPath, targetChannelCode, result);
        MigrateChannelFieldMappings(configFolderPath, result);

        return result;
    }

    private void MigrateConditionRules(string configFolderPath, string targetChannelCode, Dictionary<string, AdStdField> headerToFieldMap, AdLegacyMigrationResult result)
    {
        var path = Path.Combine(configFolderPath, "ad_condition_rules.json");
        if (!File.Exists(path)) return;

        var legacyRules = JsonSerializer.Deserialize<List<LegacyAdConditionRule>>(File.ReadAllText(path)) ?? [];
        foreach (var legacy in legacyRules)
        {
            if (string.IsNullOrWhiteSpace(legacy.TargetGroup) || legacy.Conditions == null || legacy.Conditions.Count == 0) continue;

            var details = legacy.Conditions.Select(c => new AdConditionDetail
            {
                HeaderField = ResolveField(c.Header, headerToFieldMap, result),
                Operator = ResolveOperator(c.Op),
                TargetValue = c.Val ?? string.Empty,
                Logic = string.Equals(c.Logic, "OR", StringComparison.OrdinalIgnoreCase) ? ConditionLogic.Or : ConditionLogic.And,
            }).ToList();

            _adMappingRepository.AddConditionRuleWithDetails(targetChannelCode, legacy.TargetGroup, legacy.TargetGroup, details);
            result.ConditionRulesImported++;
        }
    }

    private void MigrateExceptionRules(string configFolderPath, string targetChannelCode, AdLegacyMigrationResult result)
    {
        var path = Path.Combine(configFolderPath, "ad_exception_rules.json");
        if (!File.Exists(path)) return;

        var legacyRules = JsonSerializer.Deserialize<List<LegacyAdExceptionRule>>(File.ReadAllText(path)) ?? [];
        foreach (var legacy in legacyRules)
        {
            if (legacy.Enabled == false) continue;
            if (string.IsNullOrWhiteSpace(legacy.Header) || string.IsNullOrWhiteSpace(legacy.Val)) continue;

            _adMappingRepository.AddExceptionRule(new AdExceptionRule
            {
                ChannelCode = targetChannelCode,
                HeaderField = ResolveAdFieldName(legacy.Header),
                Operator = ResolveOperator(legacy.Op),
                TargetValue = legacy.Val,
            });
            result.ExceptionRulesImported++;
        }
    }

    private void MigrateChannelFieldMappings(string configFolderPath, AdLegacyMigrationResult result)
    {
        var path = Path.Combine(configFolderPath, "ad_channels_config.json");
        if (!File.Exists(path)) return;

        var legacyChannels = JsonSerializer.Deserialize<List<LegacyAdChannelConfig>>(File.ReadAllText(path)) ?? [];
        var existingChannels = _salesChannelRepository.GetAll();
        var allConfigs = _channelConfigService.Load();
        var changed = false;

        foreach (var legacy in legacyChannels)
        {
            var matched = existingChannels.FirstOrDefault(c => string.Equals(c.ChannelName, legacy.ChannelName, StringComparison.OrdinalIgnoreCase));
            if (matched == null)
            {
                result.UnmatchedChannelNames.Add(legacy.ChannelName ?? "(이름 없음)");
                continue;
            }

            var config = allConfigs.FirstOrDefault(c => c.ChannelCode == matched.ChannelCode);
            if (config == null)
            {
                config = new ChannelConfig { ChannelCode = matched.ChannelCode, ChannelName = matched.ChannelName };
                allConfigs.Add(config);
            }

            foreach (var (adField, col) in EnumerateLegacyMapping(legacy))
            {
                if (string.IsNullOrWhiteSpace(col)) continue;
                config.AdFieldMappings[adField] = new FieldMapping { HeaderRow = legacy.HeaderRow ?? 1, Column = col };
            }

            result.ChannelFieldMappingsImported++;
            changed = true;
        }

        if (changed) _channelConfigService.Save(allConfigs);
    }

    /// <summary>모든 레거시 채널의 헤더 매핑을 모아 "원본 헤더 텍스트 → AdStdField" 역방향 사전을 만든다.</summary>
    private static Dictionary<string, AdStdField> BuildHeaderToFieldMap(string configFolderPath, AdLegacyMigrationResult result)
    {
        var map = new Dictionary<string, AdStdField>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(configFolderPath, "ad_channels_config.json");
        if (!File.Exists(path)) return map;

        try
        {
            var legacyChannels = JsonSerializer.Deserialize<List<LegacyAdChannelConfig>>(File.ReadAllText(path)) ?? [];
            foreach (var legacy in legacyChannels)
            {
                foreach (var (adField, col) in EnumerateLegacyMapping(legacy))
                {
                    if (!string.IsNullOrWhiteSpace(col)) map[col] = adField;
                }
            }
        }
        catch (JsonException)
        {
            result.Warnings.Add("ad_channels_config.json을 읽지 못해 헤더 자동번역 없이 진행합니다.");
        }
        return map;
    }

    private static IEnumerable<(AdStdField Field, string? Column)> EnumerateLegacyMapping(LegacyAdChannelConfig legacy)
    {
        if (legacy.Mapping == null) yield break;
        if (legacy.Mapping.TryGetValue("AD_PRODUCT_NAME", out var name)) yield return (AdStdField.ProductName, name.Col);
        if (legacy.Mapping.TryGetValue("AD_PRODUCT_ID", out var id)) yield return (AdStdField.ProductId, id.Col);
        if (legacy.Mapping.TryGetValue("AD_OPTION", out var option)) yield return (AdStdField.OptionName, option.Col);
        if (legacy.Mapping.TryGetValue("AD_COST", out var cost)) yield return (AdStdField.Cost, cost.Col);
        if (legacy.Mapping.TryGetValue("AD_EXTRA1", out var extra1)) yield return (AdStdField.Extra1, extra1.Col);
        if (legacy.Mapping.TryGetValue("AD_EXTRA2", out var extra2)) yield return (AdStdField.Extra2, extra2.Col);
    }

    private static AdStdField ResolveField(string? legacyHeader, Dictionary<string, AdStdField> headerToFieldMap, AdLegacyMigrationResult result)
    {
        if (!string.IsNullOrWhiteSpace(legacyHeader) && headerToFieldMap.TryGetValue(legacyHeader, out var field)) return field;

        result.UntranslatedHeaders.Add(legacyHeader ?? "(빈 헤더)");
        return AdStdField.ProductName;
    }

    /// <summary>예외규칙의 헤더는 이미 표준 필드명(AD_PRODUCT_ID 등)이라 직접 매칭한다.</summary>
    private static AdStdField ResolveAdFieldName(string header) => header.ToUpperInvariant() switch
    {
        "AD_PRODUCT_NAME" => AdStdField.ProductName,
        "AD_PRODUCT_ID" => AdStdField.ProductId,
        "AD_OPTION" => AdStdField.OptionName,
        "AD_COST" => AdStdField.Cost,
        "AD_EXTRA1" => AdStdField.Extra1,
        "AD_EXTRA2" => AdStdField.Extra2,
        _ => AdStdField.ProductName,
    };

    private static AdConditionOperator ResolveOperator(string? legacyOp) => legacyOp switch
    {
        "contains" => AdConditionOperator.Contains,
        "not_contains" => AdConditionOperator.NotContains,
        "==" => AdConditionOperator.Equals,
        "!=" => AdConditionOperator.NotEquals,
        ">" => AdConditionOperator.GreaterThan,
        "<" => AdConditionOperator.LessThan,
        ">=" => AdConditionOperator.GreaterOrEqual,
        "<=" => AdConditionOperator.LessOrEqual,
        "is_zero" => AdConditionOperator.IsZero,
        _ => AdConditionOperator.Contains,
    };

    private class LegacyAdConditionRule
    {
        [JsonPropertyName("target_group")] public string? TargetGroup { get; set; }
        [JsonPropertyName("conditions")] public List<LegacyAdCondition>? Conditions { get; set; }
    }

    private class LegacyAdCondition
    {
        [JsonPropertyName("header")] public string? Header { get; set; }
        [JsonPropertyName("op")] public string? Op { get; set; }
        [JsonPropertyName("val")] public string? Val { get; set; }
        [JsonPropertyName("logic")] public string? Logic { get; set; }
    }

    private class LegacyAdExceptionRule
    {
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("header")] public string? Header { get; set; }
        [JsonPropertyName("op")] public string? Op { get; set; }
        [JsonPropertyName("val")] public string? Val { get; set; }
    }

    private class LegacyAdChannelConfig
    {
        [JsonPropertyName("channel_name")] public string? ChannelName { get; set; }
        [JsonPropertyName("header_row")] public int? HeaderRow { get; set; }
        [JsonPropertyName("mapping")] public Dictionary<string, LegacyAdFieldSource>? Mapping { get; set; }
    }

    private class LegacyAdFieldSource
    {
        [JsonPropertyName("col")] public string? Col { get; set; }
    }
}

public class AdLegacyMigrationResult
{
    public int ConditionRulesImported { get; set; }
    public int ExceptionRulesImported { get; set; }
    public int ChannelFieldMappingsImported { get; set; }
    public List<string> UnmatchedChannelNames { get; set; } = [];
    public List<string> UntranslatedHeaders { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
