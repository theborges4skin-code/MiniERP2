using System.Text.Json.Serialization;

namespace MiniERP2.Models;

/// <summary>
/// Drive /pending/manifest.json 스키마(01_자동발주처리_외부자동화(AppsScript)_설계.md §6).
/// MiniERP2는 이 파일을 읽기 전용으로만 소비한다.
/// </summary>
public class AutoOrderManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("generated_at")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("items")]
    public List<AutoOrderManifestItem> Items { get; set; } = [];
}

public class AutoOrderManifestItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("received_at")]
    public DateTime ReceivedAt { get; set; }

    [JsonPropertyName("xlsx_path")]
    public string XlsxPath { get; set; } = string.Empty;

    [JsonPropertyName("xlsx_sha256")]
    public string XlsxSha256 { get; set; } = string.Empty;

    [JsonPropertyName("row_count")]
    public int RowCount { get; set; }

    /// <summary>ok | partial | failed.</summary>
    [JsonPropertyName("parse_status")]
    public string ParseStatus { get; set; } = "ok";

    [JsonPropertyName("parser")]
    public string Parser { get; set; } = string.Empty;
}
