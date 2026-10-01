using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace MiniERP2.Services;

/// <summary>
/// 하나은행 "평균환율 조회" 화면(https://www.kebhana.com/cms/rate/wpfxd651_06i.do)이 쓰는 조회 주소에
/// 월평균·최종 고시 기준으로 요청해 통화별 매매기준율을 읽는다. 2026-10-02 확인: 2026년 9월 USD = 1,359.20.
/// 은행 화면 구조가 바뀌면 실패할 수 있으므로 호출부는 항상 수동 입력으로 되돌아갈 수 있어야 한다.
/// </summary>
public static class HanaExchangeRateClient
{
    private const string Endpoint = "https://www.kebhana.com/cms/rate/wpfxd651_06i_01.do";
    private const string Referer = "https://www.kebhana.com/cms/rate/index.do?contentUrl=/cms/rate/wpfxd651_06i.do";

    /// <summary>표의 숫자 열 순서: 현찰 살때·팔때, 송금 보낼때·받을때, 외화수표 팔때, 매매기준율, 환가료율, 대미환산율.</summary>
    private const int BaseRateColumnIndex = 5;

    public static async Task<decimal> GetMonthlyAverageBaseRateAsync(int year, int month, string currency = "USD", CancellationToken cancellationToken = default)
    {
        var first = new DateTime(year, month, 1);
        var today = DateTime.Today;
        if (first > today) throw new InvalidOperationException("아직 오지 않은 달은 조회할 수 없습니다.");
        var last = first.Year == today.Year && first.Month == today.Month ? today : first.AddMonths(1).AddDays(-1);

        var form = new Dictionary<string, string>
        {
            ["ajax"] = "true",
            ["inqDvCd"] = "2",
            ["tmpInqStrDtY_m"] = first.ToString("yyyy", CultureInfo.InvariantCulture),
            ["tmpInqStrDtM_m"] = first.ToString("MM", CultureInfo.InvariantCulture),
            ["tmpPbldDvCd"] = "0",
            ["inqStrDt"] = first.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["inqEndDt"] = last.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["pbldDvCd"] = "0",
            ["requestTarget"] = "searchContentDiv",
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new FormUrlEncodedContent(form) };
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        request.Headers.Referrer = new Uri(Referer);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseBaseRate(html, currency);
    }

    /// <summary>응답 HTML에서 통화코드가 든 표 행을 찾아 매매기준율을 꺼낸다.</summary>
    public static decimal ParseBaseRate(string html, string currency)
    {
        foreach (Match row in Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(row.Groups[1].Value, @"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " ")).Trim())
                .ToList();
            var codeIndex = cells.FindIndex(c => Regex.IsMatch(c, $@"(^|\s){Regex.Escape(currency)}(\s|$)"));
            if (codeIndex < 0) continue;

            var numbers = cells.Skip(codeIndex + 1)
                .Select(c => decimal.TryParse(c.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (decimal?)v : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            if (numbers.Count > BaseRateColumnIndex && numbers[BaseRateColumnIndex] > 0)
                return numbers[BaseRateColumnIndex];
        }

        throw new InvalidOperationException($"하나은행 응답에서 {currency} 매매기준율을 찾지 못했습니다. 화면 구조가 바뀌었을 수 있으니 수동으로 입력하세요.");
    }
}
