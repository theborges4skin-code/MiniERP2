using System.Text.RegularExpressions;
using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 운송장 결과 파일의 각 줄을 분류 라벨로 표시만 해주는 판별기입니다(운송장파일_미매칭건_OFS역등록_검토_rev2.md
/// §2). 등록 여부를 결정하지 않습니다 — 라벨은 필터·정렬용이고, 최종 등록 여부는 항상 사용자가
/// 판단합니다. 판별 키워드는 이 클래스 상단에 모아둬 물류센터명이 늘어나도 여기만 고치면 되게 했다.
/// </summary>
public static class TrackingLabelClassifier
{
    public const string OnlineOrder = "온라인주문";
    public const string CoupangRocket = "쿠팡로켓";
    public const string NaverFulfillment = "네이버풀필";
    public const string AmazonFba = "아마존FBA";
    public const string Partner = "거래처";
    public const string Other = "기타";

    private static readonly string[] RocketProductKeywords = ["로켓", "쿠팡풀필"];
    private static readonly string[] RocketCenterNames = ["동탄", "인천", "창원", "대구", "경기광주", "전라광주"];
    private static readonly Regex RocketCenterPattern = new(
        $"(?:{string.Join("|", RocketCenterNames)})\\s*\\d", RegexOptions.Compiled);

    private static readonly string[] NaverMemoKeywords = ["네이버풀필먼트"];
    private static readonly Regex NaverMemoPattern = new(@"\d*풀필먼트", RegexOptions.Compiled);

    private static readonly string[] FbaProductKeywords = ["_SEND_", "FBA"];
    private static readonly string[] FbaRecipientKeywords = ["인천센터"];

    public static string Classify(TrackingBackfillRow row)
    {
        var memo = row.OrderNoMemo ?? string.Empty;
        var productName = row.ProductName ?? string.Empty;
        var recipient = row.Recipient ?? string.Empty;

        if (memo.TrimStart().StartsWith('#')) return OnlineOrder;

        if (RocketProductKeywords.Any(k => productName.Contains(k, StringComparison.OrdinalIgnoreCase))
            || RocketCenterPattern.IsMatch(recipient))
        {
            return CoupangRocket;
        }

        if (NaverMemoKeywords.Any(k => memo.Contains(k, StringComparison.OrdinalIgnoreCase))
            || NaverMemoPattern.IsMatch(memo))
        {
            return NaverFulfillment;
        }

        if (FbaProductKeywords.All(k => productName.Contains(k, StringComparison.OrdinalIgnoreCase))
            || FbaRecipientKeywords.Any(k => recipient.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return AmazonFba;
        }

        if (string.IsNullOrWhiteSpace(memo)) return Other;

        return Partner;
    }
}
