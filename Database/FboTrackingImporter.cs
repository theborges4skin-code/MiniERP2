using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Database;

public class FboTrackingImportResult
{
    public int AppliedCount { get; set; }

    /// <summary>결과 파일에는 있지만 발주 박스에서 찾지 못한 반품부명(이송장번호) 목록.</summary>
    public List<string> UnmatchedRows { get; } = [];

    /// <summary>같은 반품부명(=박스)인데 이송장번호가 서로 다르거나, 동일 반품부명의 미출고 박스가
    /// 여러 건이라 특정할 수 없는 경우 — 합포장/식별 판단 실패.</summary>
    public List<string> InconsistentBoxes { get; } = [];

    /// <summary>미매칭/불일치가 하나도 없어야 실제로 반영된다(기획서 §5.3 — 부분 반영 금지).</summary>
    public bool Success => UnmatchedRows.Count == 0 && InconsistentBoxes.Count == 0;
}

/// <summary>
/// 풀필먼트사(CJ대한통운) 이송장 결과 파일을 읽어 FBO 박스(FboBox)의 이송장번호를 채운다
/// (기획서 §6). 매칭 단위는 박스(반품부명)이다 — "고객주문번호"는 사람이 결과를 눈으로 확인하는
/// 용도로만 쓰고(사용자 결정), 박스마다 고유한 내부 매칭키(FboBox.MatchKey)는 DB에만 남기고
/// 결과파일 왕복에는 의존하지 않는다. 같은 박스에 여러 행이 나와도(합포장 결과) 이송장번호가
/// 전부 같아야 정상 처리한다. 미매칭/불일치가 하나라도 있으면 전체 반영을 취소한다
/// — <see cref="OutboundHistoryForm"/>의 수령인 매칭과 달리 부분 적용을 허용하지 않는다.
/// 기본적으로 OFS(<see cref="OutboundHistoryForm"/>)의 운송장번호 불러오기와 같은 형식을
/// 전제한다 — "상태" 같은 별도 확정 표시 컬럼을 요구하지 않고(실제 CJ 결과 파일에 그런 컬럼이
/// 없었음, 있으면 무시), 수취인/운송장번호 컬럼명도 흔한 이표기를 함께 인식한다.
/// </summary>
public class FboTrackingImporter
{
    private static readonly string[] TrackingHeaderCandidates = ["이송장번호", "운송장번호"];
    private static readonly string[] ReceiverHeaderCandidates = ["반품부", "반품부성명", "수취인", "받는분", "받는분명"];

    private readonly FboOrderRepository _repository;

    public FboTrackingImporter(FboOrderRepository? repository = null)
    {
        _repository = repository ?? new FboOrderRepository();
    }

    public FboTrackingImportResult Import(string filePath)
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
        var receiverCol = FindColumn(columns, ReceiverHeaderCandidates);
        if (trackingCol is null || receiverCol is null)
        {
            var missing = new List<string>();
            if (trackingCol is null) missing.Add(string.Join("/", TrackingHeaderCandidates));
            if (receiverCol is null) missing.Add(string.Join("/", ReceiverHeaderCandidates));
            throw new InvalidOperationException($"필수 컬럼({string.Join(", ", missing)})을(를) 결과 파일 헤더에서 찾지 못했습니다.");
        }

        // 같은 반품부명(=박스)으로 여러 행(합포장 결과)이 나올 수 있으므로 정규화된 이름으로 모은다.
        var rowsByReceiver = new Dictionary<string, List<string>>();
        for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
        {
            var receiver = worksheet.Cells[row, receiverCol.Value].Value?.ToString()?.Trim() ?? string.Empty;
            var trackingNo = worksheet.Cells[row, trackingCol.Value].Value?.ToString()?.Trim();
            if (receiver.Length == 0 || string.IsNullOrWhiteSpace(trackingNo)) continue;

            if (!rowsByReceiver.TryGetValue(receiver, out var list))
            {
                list = [];
                rowsByReceiver[receiver] = list;
            }
            list.Add(trackingNo);
        }

        var result = new FboTrackingImportResult();
        // 반품부명이 채널 안에서(발주별 박스번호 기반) 유일하다는 전제인데, 서로 다른 발주가 동시에
        // 미출고 상태로 남아있으면 우연히 겹칠 수 있다 — 그 경우 특정 대신 불일치로 보고한다.
        var boxesByReceiver = _repository.GetPendingBoxes()
            .GroupBy(b => b.ReceiverDisplayName.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var toApply = new List<(FboBox Box, string TrackingNo)>();
        foreach (var (receiver, trackingNos) in rowsByReceiver)
        {
            var distinctTracking = trackingNos.Distinct().ToList();
            if (distinctTracking.Count > 1)
            {
                result.InconsistentBoxes.Add($"{receiver} (이송장 {string.Join("/", distinctTracking)})");
                continue;
            }

            if (!boxesByReceiver.TryGetValue(receiver, out var candidates) || candidates.Count == 0)
            {
                result.UnmatchedRows.Add($"{receiver} ({distinctTracking[0]})");
                continue;
            }
            if (candidates.Count > 1)
            {
                result.InconsistentBoxes.Add($"{receiver}: 동일한 반품부명을 가진 미출고 박스가 {candidates.Count}건 있어 특정할 수 없습니다.");
                continue;
            }

            toApply.Add((candidates[0], distinctTracking[0]));
        }

        // 부분 반영 금지 — 미매칭/불일치가 하나라도 있으면 아무것도 적용하지 않는다.
        if (!result.Success) return result;

        foreach (var (box, trackingNo) in toApply)
        {
            _repository.ApplyTracking(box.FboNo, box.BoxSeq, trackingNo);
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
