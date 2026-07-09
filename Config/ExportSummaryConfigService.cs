using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

/// <summary>
/// export_summary_config.json을 읽고 쓴다. 파일이 없으면 기획서 내용을 바탕으로 한 기본값을 만들어
/// 저장해두므로, 처음 실행해도 바로 쓸 수 있다. 이후에는 이 파일을 코드 재빌드 없이 직접 고쳐서
/// 헤더 문구 수정/신규 마켓 추가에 대응한다.
/// </summary>
public class ExportSummaryConfigService
{
    private readonly string _filePath;

    public ExportSummaryConfigService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.ExportSummaryConfigFilePath;
    }

    public ExportSummaryConfig Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaultConfig = CreateDefault();
            Save(defaultConfig);
            return defaultConfig;
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<ExportSummaryConfig>(json) ?? CreateDefault();
    }

    public void Save(ExportSummaryConfig config)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// 기획서(export_summary_report_기획.md) 4-2/4-4/5/6절 내용을 옮긴 기본값. 쇼피 SG/MY/PH는
    /// 원문 헤더가 영문이라 그대로 옮겼지만, TH/TW/VN/BR은 기획서 원문 자체가 인코딩 손상으로
    /// 읽을 수 없었던 상태라("Notion Korean text corruption"과 동일한 유형의 손상) 실제 헤더 문구를
    /// 확인할 수 없어 자리표시자(TODO...)로 남겨두었다. 각 마켓의 실제 판매 파일을 열어 헤더 행의
    /// 정확한 문구로 이 값을 바꿔야 해당 마켓 파싱이 동작한다.
    /// </summary>
    private static ExportSummaryConfig CreateDefault()
    {
        var config = new ExportSummaryConfig
        {
            Markets = new List<ExportSummaryMarket>
            {
                new() { MarketCode = "SG", MarketName = "쇼피-싱가포르", Currency = "SGD" },
                new() { MarketCode = "MY", MarketName = "쇼피-말레이시아", Currency = "MYR" },
                new() { MarketCode = "PH", MarketName = "쇼피-필리핀", Currency = "PHP" },
                new() { MarketCode = "TH", MarketName = "쇼피-태국", Currency = "THB" },
                new() { MarketCode = "TW", MarketName = "쇼피-대만", Currency = "TWD" },
                new() { MarketCode = "VN", MarketName = "쇼피-베트남", Currency = "VND" },
                new() { MarketCode = "BR", MarketName = "쇼피-브라질", Currency = "BRL" },
                new() { MarketCode = "MX", MarketName = "쇼피-멕시코", Currency = "MXN" },
                new() { MarketCode = "US", MarketName = "아마존-미국", Currency = "USD" },
                new() { MarketCode = "JP", MarketName = "아마존-일본", Currency = "JPY" },
                new() { MarketCode = "WISH", MarketName = "위시몰", Currency = "USD" },
                new() { MarketCode = "ZZ", MarketName = "직거래(Wise)", Currency = "USD" },
                // USD 신고건 중 마켓 미분리분(아마존US/위시몰/직거래 공통) — 신고 합계만 표시.
                new() { MarketCode = "USD_UNMATCHED", MarketName = "USD(미분리)", Currency = "USD" },
            },
            DeclarationTrack = new DeclarationTrackConfig
            {
                HeaderRow = 1,
                DateColumn = "신고일자",
                AmountColumn = "결제금액",
                CurrencyColumn = "통화코드",
                CurrencyToMarket = new Dictionary<string, string>
                {
                    ["SGD"] = "SG",
                    ["MYR"] = "MY",
                    ["PHP"] = "PH",
                    ["THB"] = "TH",
                    ["TWD"] = "TW",
                    ["VND"] = "VN",
                    ["BRL"] = "BR",
                    ["MXN"] = "MX",
                    ["JPY"] = "JP",
                    // USD: 아마존US/위시몰/직거래가 공유 → 통화만으로 마켓 분리 불가.
                    // USD_UNMATCHED로 묶어 합계만 표시하고, Excel 상세 시트에서 개별 확인.
                    ["USD"] = "USD_UNMATCHED",
                },
            },
            SalesMarkets = new List<SalesMarketMapping>
            {
                new() { MarketCode = "SG", HeaderRow = 1, DateColumn = "Order Paid Time", AmountColumn = "Deal Price",
                    FileNamePatterns = new() { "SG_", "_SG_", "_SG.", "singapore" } },
                new() { MarketCode = "MY", HeaderRow = 1, DateColumn = "Order Paid Time", AmountColumn = "Deal Price",
                    FileNamePatterns = new() { "MY_", "_MY_", "_MY.", "malaysia" } },
                new() { MarketCode = "PH", HeaderRow = 1, DateColumn = "Order Paid Time", AmountColumn = "Products' Price Paid by Buyer (PHP)",
                    FileNamePatterns = new() { "PH_", "_PH_", "_PH.", "philippines" } },
                // 아래 4개 마켓은 기획서 원문의 헤더 문구 자체가 인코딩 손상으로 판독 불가능했다.
                // 해당 마켓 판매 파일을 열어 헤더 행의 정확한 문구로 바꿔야 한다.
                new() { MarketCode = "TH", HeaderRow = 1, DateColumn = "TODO: 태국 판매파일 결제일 헤더", AmountColumn = "TODO: 태국 판매파일 판매액 헤더",
                    FileNamePatterns = new() { "TH_", "_TH_", "_TH.", "thailand" } },
                new() { MarketCode = "TW", HeaderRow = 1, DateColumn = "TODO: 대만 판매파일 결제일 헤더", AmountColumn = "TODO: 대만 판매파일 판매액 헤더",
                    FileNamePatterns = new() { "TW_", "_TW_", "_TW.", "taiwan" } },
                new() { MarketCode = "VN", HeaderRow = 1, DateColumn = "TODO: 베트남 판매파일 결제일 헤더", AmountColumn = "TODO: 베트남 판매파일 판매액 헤더",
                    FileNamePatterns = new() { "VN_", "_VN_", "_VN.", "vietnam" } },
                new() { MarketCode = "BR", HeaderRow = 1, DateColumn = "TODO: 브라질 판매파일 결제일 헤더", AmountColumn = "TODO: 브라질 판매파일 판매액 헤더",
                    FileNamePatterns = new() { "BR_", "_BR_", "_BR.", "brazil" } },
                // 위시몰은 Download.CSV 한 파일에 Gross/Net이 함께 있고, 기획서 5장 기준 판매액=Gross.
                new() { MarketCode = "WISH", HeaderRow = 1, DateColumn = "Date", AmountColumn = "Gross",
                    FileNamePatterns = new() { "wish", "Wish" } },
            },
            RemittanceTrack = new RemittanceTrackConfig
            {
                HeaderRow = 1,
                DateColumn = "Date",
                AmountColumn = "Amount",
                DescriptionColumn = "Description",
                ExcludeContains = new List<string> { "Withdrawal to Woori" },
                Rules = new List<RemittanceRule>
                {
                    new() { Contains = new List<string> { "Shopee", "Singapore" }, MarketCode = "SG" },
                    new() { Contains = new List<string> { "Shopee", "Malaysia" }, MarketCode = "MY" },
                    new() { Contains = new List<string> { "Shopee", "Philippines" }, MarketCode = "PH" },
                    new() { Contains = new List<string> { "Shopee", "Thailand" }, MarketCode = "TH" },
                    new() { Contains = new List<string> { "Shopee", "Taiwan" }, MarketCode = "TW" },
                    new() { Contains = new List<string> { "Shopee", "Vietnam" }, MarketCode = "VN" },
                    new() { Contains = new List<string> { "Shopee", "Brazil" }, MarketCode = "BR" },
                    new() { Contains = new List<string> { "Shopee", "Mexico" }, MarketCode = "MX" },
                    new() { Contains = new List<string> { "Amazon", "559199" }, MarketCode = "US" },
                    new() { Contains = new List<string> { "Amazon", "770668" }, MarketCode = "JP" },
                    new() { Contains = new List<string> { "WISE" }, MarketCode = "ZZ" },
                },
            },
        };

        return config;
    }
}
