using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

/// <summary>
/// online_report_config.json(온라인 매출 종합보고서 설정)을 읽고 쓴다. 파일이 없으면 2026-10-02 사용자와
/// 확정한 기본값(블록 순서·품목 열·거래처·택배비 단가)으로 만들어 저장한다.
/// </summary>
public class OnlineReportConfigService
{
    private readonly string _filePath;

    public OnlineReportConfigService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.OnlineReportConfigFilePath;
    }

    public OnlineReportConfig Load()
    {
        if (!File.Exists(_filePath))
        {
            var created = CreateDefault();
            Save(created);
            return created;
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<OnlineReportConfig>(json) ?? CreateDefault();
    }

    public void Save(OnlineReportConfig config)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// 채널코드는 이 PC의 channels_config.json 기준(스마트 1F80429D, 쿠팡일반 443F05E4, 쿠팡로켓 4D4A484A,
    /// 쿠팡그로스 CH004, 기타_11번가 9A2C5962, ESM 275B7A29, 기타_수동 CH074, 오늘의집 CH066).
    /// </summary>
    public static OnlineReportConfig CreateDefault() => new()
    {
        Blocks =
        [
            new() { Name = "스마트스토어", ChannelCodes = ["1F80429D"] },
            new() { Name = "쿠팡", ChannelCodes = ["443F05E4"] },
            new() { Name = "쿠팡로켓", ChannelCodes = ["4D4A484A"], CountShipping = false, IsRocketGrowth = true },
            new() { Name = "쿠팡그로스", ChannelCodes = ["CH004"], CountShipping = false, IsRocketGrowth = true },
            new() { Name = "기타", ChannelCodes = ["9A2C5962", "275B7A29", "CH074"] },
            new() { Name = "오늘의집", ChannelCodes = ["CH066"] },
        ],
        GroupColumns =
        [
            new() { Number = 1, Header = "피마자유" },
            new() { Number = 2, Header = "비누베이스" },
            new() { Number = 3, Header = "보르피린" },
            new() { Number = 5, Header = "VG" },
            new() { Number = 10, Header = "프래그런스/DB", MemberNumbers = [8, 10] },
            new() { Number = 12, Header = "핸드워시" },
            new() { Number = 14, Header = "면도세정액" },
            new() { Number = 15, Header = "요석제거제" },
            new() { Number = 16, Header = "커피세정액" },
            new() { Number = 17, Header = "추출물" },
            new() { Number = 19, Header = "계면활성제", MemberNumbers = [19, 4] },
            new() { Number = 21, Header = "선물세트" },
            new() { Number = 22, Header = "기타" },
            new() { Number = 23, Header = "옵시" },
        ],
        FallbackColumnNumber = 22,
        Partners =
        [
            new() { DisplayName = "한결쇼핑", PartyNameKeywords = ["한결"] },
            new() { DisplayName = "이공인터", PartyNameKeywords = ["이공"] },
            new() { DisplayName = "펩투나인", PartyNameKeywords = ["펩투"] },
            new() { DisplayName = "툴스엠알오", PartyNameKeywords = ["툴스", "동아상사"] },
            new() { DisplayName = "투유", PartyNameKeywords = ["투유"] },
            new() { DisplayName = "푸디", PartyNameKeywords = ["푸디"] },
            new() { DisplayName = "진도그린", PartyNameKeywords = ["진도그린"] },
        ],
        CostItems = ["일용직", "포천", "마케팅"],
        ExportMarkets = ["아마존", "쇼피"],
        IgnoredChannelCodes = ["CH069"],
    };
}
