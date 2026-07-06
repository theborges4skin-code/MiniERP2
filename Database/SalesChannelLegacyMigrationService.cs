using System.Text.Json;
using System.Text.Json.Serialization;
using MiniERP2.Config;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Database;

/// <summary>
/// SalesManagerV2(레거시 Python)의 config/channels_config.json(채널별 정산 헤더 매핑/환율/채널유형/
/// 보조소스)을 MiniERP2의 채널 설정으로 이관합니다. 두 시스템의 표준 필드(STD_*  ↔ StdField)가
/// 거의 1:1로 대응되고, 쿠팡그로스 보조소스(GrowthAuxSource) 구조도 동일해 높은 정확도로 자동
/// 이식할 수 있습니다. 다만 레거시는 "정산서"만 다루므로(MiniERP2는 발주서/정산서를 분리) 이관
/// 대상은 항상 SettlementFieldMappings이며, 수취인/연락처 등 발주서 전용 필드는 레거시에 없어
/// 이관되지 않습니다(채널설정창에서 별도로 채워야 함).
/// </summary>
public class SalesChannelLegacyMigrationService
{
    private readonly SalesChannelRepository _salesChannelRepository;
    private readonly ChannelConfigService _channelConfigService;

    public SalesChannelLegacyMigrationService(SalesChannelRepository? salesChannelRepository = null, ChannelConfigService? channelConfigService = null)
    {
        _salesChannelRepository = salesChannelRepository ?? new SalesChannelRepository();
        _channelConfigService = channelConfigService ?? new ChannelConfigService();
    }

    /// <summary>channels_config.json에서 채널명 목록만 읽어 반환합니다. 선택 다이얼로그 표시용.</summary>
    public IReadOnlyList<string> ReadChannelNames(string configFolderPath)
    {
        var path = Path.Combine(configFolderPath, "channels_config.json");
        if (!File.Exists(path)) return [];
        try
        {
            var channels = JsonSerializer.Deserialize<List<LegacyChannel>>(File.ReadAllText(path)) ?? [];
            return channels.Where(c => !string.IsNullOrWhiteSpace(c.ChannelName)).Select(c => c.ChannelName!).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// configFolderPath의 channels_config.json을 읽어 채널명이 일치하는 기존 채널은 설정을
    /// 갱신하고, 없는 채널명은 새 채널을 만들어(ChannelCodeGenerator로 "CH숫자" 코드 부여) 이식합니다.
    /// </summary>
    public SalesChannelLegacyMigrationResult Migrate(string configFolderPath)
        => MigrateCore(configFolderPath, null);

    /// <summary>channelNames에 포함된 채널명만 선택적으로 이관합니다.</summary>
    public SalesChannelLegacyMigrationResult MigrateSelected(string configFolderPath, IReadOnlySet<string> channelNames)
        => MigrateCore(configFolderPath, channelNames);

    private SalesChannelLegacyMigrationResult MigrateCore(string configFolderPath, IReadOnlySet<string>? filter)
    {
        var result = new SalesChannelLegacyMigrationResult();
        var path = Path.Combine(configFolderPath, "channels_config.json");
        if (!File.Exists(path))
        {
            result.Warnings.Add("channels_config.json을 찾을 수 없습니다.");
            return result;
        }

        List<LegacyChannel> legacyChannels;
        try
        {
            legacyChannels = JsonSerializer.Deserialize<List<LegacyChannel>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException ex)
        {
            result.Warnings.Add($"channels_config.json 형식을 읽지 못했습니다: {ex.Message}");
            return result;
        }

        var existingChannels = _salesChannelRepository.GetAll();
        var allConfigs = _channelConfigService.Load();

        foreach (var legacy in legacyChannels)
        {
            if (string.IsNullOrWhiteSpace(legacy.ChannelName)) continue;
            if (filter != null && !filter.Contains(legacy.ChannelName)) continue;

            var matched = existingChannels.FirstOrDefault(c => string.Equals(c.ChannelName, legacy.ChannelName, StringComparison.OrdinalIgnoreCase));
            string channelCode;
            if (matched != null)
            {
                channelCode = matched.ChannelCode;
                result.UpdatedChannels.Add(legacy.ChannelName);
            }
            else
            {
                channelCode = ChannelCodeGenerator.GenerateNext(existingChannels.Select(c => c.ChannelCode));
                var newChannel = new SalesChannel { ChannelCode = channelCode, ChannelName = legacy.ChannelName };
                _salesChannelRepository.Upsert(newChannel);
                existingChannels.Add(newChannel);
                result.CreatedChannels.Add(legacy.ChannelName);
            }

            var config = allConfigs.FirstOrDefault(c => c.ChannelCode == channelCode);
            if (config == null)
            {
                config = new ChannelConfig { ChannelCode = channelCode, ChannelName = legacy.ChannelName };
                allConfigs.Add(config);
            }
            else
            {
                config.ChannelName = legacy.ChannelName;
            }

            config.ChannelType = ResolveChannelType(legacy.ChannelType);
            config.ExchangeRate = legacy.ExchangeRate ?? 1m;

            ApplySettlementFieldMappings(legacy, config, result);
            ApplyGrowthAuxSources(legacy, config);
        }

        _channelConfigService.Save(allConfigs);
        return result;
    }

    private static void ApplySettlementFieldMappings(LegacyChannel legacy, ChannelConfig config, SalesChannelLegacyMigrationResult result)
    {
        if (legacy.Mapping == null) return;
        var headerRow = legacy.GlobalHeaderRow ?? 1;

        void SetField(StdField field, string legacyKey)
        {
            if (!legacy.Mapping.TryGetValue(legacyKey, out var entry)) return;

            // 레거시의 "조건부 값 추출"(예: 옵션ID에 '배송' 포함 시 다른 열 값을 가져오는 규칙)은
            // MiniERP2의 FieldMapping이 지원하지 않는 기능이라 건너뛰고 안내에 남긴다 — 드문
            // 케이스라 사용자가 채널설정창에서 직접 확인/대체하면 된다. Col이 비어있어도(레거시는
            // 조건부 규칙이 있을 때 Col을 비워두는 경우가 많음) 먼저 확인해야 한다.
            if (entry.Conditions is { Count: > 0 })
            {
                result.UnsupportedConditionalFields.Add($"{legacy.ChannelName}/{legacyKey}");
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Col)) return;
            config.SettlementFieldMappings[field] = new FieldMapping { HeaderRow = headerRow, Column = entry.Col };
        }

        SetField(StdField.ProductName, "STD_PRODUCT_NAME");
        SetField(StdField.OptionName, "STD_OPTION");
        SetField(StdField.Quantity, "STD_QTY");
        SetField(StdField.SettlementAmount, "STD_SETTLEMENT");
        SetField(StdField.ShippingFee, "STD_SHIPPING");
        SetField(StdField.HandlingFee, "STD_FEE");
    }

    private static void ApplyGrowthAuxSources(LegacyChannel legacy, ChannelConfig config)
    {
        if (legacy.GrowthAuxSources == null || legacy.GrowthAuxSources.Count == 0) return;

        config.GrowthAuxSources = legacy.GrowthAuxSources.Select(a => new GrowthAuxSource
        {
            Enabled = string.Equals(a.Enabled, "Y", StringComparison.OrdinalIgnoreCase),
            TargetStdField = ResolveAuxField(a.TargetStdField),
            SheetName = a.SheetName,
            HeaderRow = a.HeaderRow ?? 1,
            KeyHeader = a.KeyHeader,
            ValueHeader = a.ValueHeader,
            OutCol = a.OutCol,
        }).ToList();
    }

    private static StdField ResolveAuxField(string? legacyField) => legacyField switch
    {
        "STD_SHIPPING" => StdField.ShippingFee,
        "STD_FEE" => StdField.HandlingFee,
        "STD_SETTLEMENT" => StdField.SettlementAmount,
        _ => StdField.HandlingFee,
    };

    private static ChannelType ResolveChannelType(string? legacyType) => legacyType switch
    {
        "COUPANG_GROWTH" => ChannelType.CoupangGrowth,
        "COUPANG_NORMAL" => ChannelType.CoupangGeneral,
        "COUPANG_ROCKET" => ChannelType.CoupangRocket,
        "ETC_11ST" => ChannelType.ElevenStreet,
        "AMAZON_US" => ChannelType.AmazonUs,
        "AMAZON_JP" => ChannelType.AmazonJp,
        "GENERAL" => ChannelType.General,
        _ => ChannelType.Other,
    };

    private class LegacyChannel
    {
        [JsonPropertyName("channel_name")] public string? ChannelName { get; set; }
        [JsonPropertyName("channel_type")] public string? ChannelType { get; set; }
        [JsonPropertyName("exchange_rate")] public decimal? ExchangeRate { get; set; }
        [JsonPropertyName("global_header_row")] public int? GlobalHeaderRow { get; set; }
        [JsonPropertyName("mapping")] public Dictionary<string, LegacyFieldEntry>? Mapping { get; set; }
        [JsonPropertyName("growth_aux_sources")] public List<LegacyGrowthAuxSource>? GrowthAuxSources { get; set; }
    }

    private class LegacyFieldEntry
    {
        [JsonPropertyName("col")] public string? Col { get; set; }
        [JsonPropertyName("conditions")] public List<JsonElement>? Conditions { get; set; }
    }

    private class LegacyGrowthAuxSource
    {
        [JsonPropertyName("enabled")] public string? Enabled { get; set; }
        [JsonPropertyName("target_std_field")] public string? TargetStdField { get; set; }
        [JsonPropertyName("sheet_name")] public string? SheetName { get; set; }
        [JsonPropertyName("header_row")] public int? HeaderRow { get; set; }
        [JsonPropertyName("key_header")] public string? KeyHeader { get; set; }
        [JsonPropertyName("value_header")] public string? ValueHeader { get; set; }
        [JsonPropertyName("out_col")] public string? OutCol { get; set; }
    }
}

public class SalesChannelLegacyMigrationResult
{
    public List<string> CreatedChannels { get; set; } = [];
    public List<string> UpdatedChannels { get; set; } = [];
    public List<string> UnsupportedConditionalFields { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
