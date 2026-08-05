using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Database;

public class FbaTrackingImportResult
{
    public int AppliedCount { get; set; }

    /// <summary>결과 파일에는 있지만 발주 박스에서 찾지 못한 고객주문번호(운송장번호) 목록.</summary>
    public List<string> UnmatchedRows { get; } = [];

    /// <summary>같은 고객주문번호(=박스)인데 운송장번호가 서로 다르거나, 동일 고객주문번호의 미출고
    /// 박스가 여러 건이라 특정할 수 없는 경우(§7.2 — Shipment ID 미입력 상태로 여러 발주가 동시에
    /// 미출고면 고객주문번호가 중복될 수 있음).</summary>
    public List<string> InconsistentBoxes { get; } = [];

    /// <summary>미매칭/불일치가 하나도 없어야 실제로 반영된다(§7.2 — 부분 반영 금지, FBO와 동일 원칙).</summary>
    public bool Success => UnmatchedRows.Count == 0 && InconsistentBoxes.Count == 0;
}

/// <summary>
/// 택배사(CJ) 운송장 결과 파일을 읽어 FBA 박스(FbaBox)의 운송장번호를 채운다(기획서 §7.2).
/// FboTrackingImporter를 복제하되 매칭 기준이 다르다 — FBA는 수취지가 1곳 고정이라 모든 박스의
/// 반품부성명이 동일해 매칭키로 쓸 수 없으므로, 대신 박스 단위로 고유한 고객주문번호
/// (FbaBox.MatchKey, §7.1)를 매칭키로 쓴다. 미매칭/불일치가 하나라도 있으면 전체 반영을 취소한다.
/// </summary>
public class FbaTrackingImporter
{
    private static readonly string[] TrackingHeaderCandidates = ["운송장번호", "이송장번호"];
    private static readonly string[] MatchKeyHeaderCandidates = ["고객주문번호", "주문번호"];

    private readonly FbaOrderRepository _repository;

    public FbaTrackingImporter(FbaOrderRepository? repository = null)
    {
        _repository = repository ?? new FbaOrderRepository();
    }

    public FbaTrackingImportResult Import(string filePath)
    {
        ExcelLicense.Ensure();
        using var package = Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? CsvWorkbookReader.LoadAsPackage(filePath)
            : new ExcelPackage(new FileInfo(filePath));
        var worksheet = package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("엑셀 파일에서 시트를 찾을 수 없습니다.");
        if (worksheet.Dimension == null)
        {
            throw new InvalidOperationException("엑셀 파일에서 데이터를 찾을 수 없습니다.");
        }

        var columns = new Dictionary<string, int>();
        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var header = NormalizeHeader(worksheet.Cells[1, col].Value?.ToString());
            if (header.Length == 0 || columns.ContainsKey(header)) continue;
            columns[header] = col;
        }

        var trackingCol = FindColumn(columns, TrackingHeaderCandidates);
        var matchKeyCol = FindColumn(columns, MatchKeyHeaderCandidates);
        if (trackingCol is null || matchKeyCol is null)
        {
            var missing = new List<string>();
            if (trackingCol is null) missing.Add(string.Join("/", TrackingHeaderCandidates));
            if (matchKeyCol is null) missing.Add(string.Join("/", MatchKeyHeaderCandidates));
            throw new InvalidOperationException($"필수 컬럼({string.Join(", ", missing)})을(를) 결과 파일 헤더에서 찾지 못했습니다.");
        }

        // 같은 고객주문번호(=박스)로 여러 행이 나올 수 있으므로 정규화된 값으로 모은다.
        var rowsByMatchKey = new Dictionary<string, List<string>>();
        for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
        {
            var matchKey = FbaKeyGenerator.NormalizeMatchKey(worksheet.Cells[row, matchKeyCol.Value].Value?.ToString());
            var trackingNo = worksheet.Cells[row, trackingCol.Value].Value?.ToString()?.Trim();
            if (matchKey.Length == 0 || string.IsNullOrWhiteSpace(trackingNo)) continue;

            if (!rowsByMatchKey.TryGetValue(matchKey, out var list))
            {
                list = [];
                rowsByMatchKey[matchKey] = list;
            }
            list.Add(trackingNo);
        }

        var result = new FbaTrackingImportResult();
        // 고객주문번호가 발주 안에서는 유일하지만, Shipment ID를 아직 입력하지 않은 발주가 여러 건
        // 동시에 미출고 상태로 남아있으면 우연히 겹칠 수 있다(§7.2) — 그 경우 특정 대신 불일치로 보고한다.
        var boxesByMatchKey = _repository.GetPendingBoxes()
            .GroupBy(b => FbaKeyGenerator.NormalizeMatchKey(b.MatchKey), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var toApply = new List<(FbaBox Box, string TrackingNo)>();
        foreach (var (matchKey, trackingNos) in rowsByMatchKey)
        {
            var distinctTracking = trackingNos.Distinct().ToList();
            if (distinctTracking.Count > 1)
            {
                result.InconsistentBoxes.Add($"{matchKey} (운송장 {string.Join("/", distinctTracking)})");
                continue;
            }

            if (!boxesByMatchKey.TryGetValue(matchKey, out var candidates) || candidates.Count == 0)
            {
                result.UnmatchedRows.Add($"{matchKey} ({distinctTracking[0]})");
                continue;
            }
            if (candidates.Count > 1)
            {
                result.InconsistentBoxes.Add($"{matchKey}: 동일한 고객주문번호를 가진 미출고 박스가 {candidates.Count}건 있어 특정할 수 없습니다.");
                continue;
            }

            toApply.Add((candidates[0], distinctTracking[0]));
        }

        // 부분 반영 금지 — 미매칭/불일치가 하나라도 있으면 아무것도 적용하지 않는다.
        if (!result.Success) return result;

        foreach (var (box, trackingNo) in toApply)
        {
            _repository.ApplyTracking(box.FbaNo, box.BoxSeq, trackingNo);
            result.AppliedCount++;
        }
        return result;
    }

    private static string NormalizeHeader(string? header)
        => (header ?? string.Empty).Replace("\r\n", string.Empty).Replace("\n", string.Empty).Trim();

    private static int? FindColumn(Dictionary<string, int> columns, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (columns.TryGetValue(candidate, out var col)) return col;
        }
        return null;
    }
}
