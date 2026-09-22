using System.Globalization;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Database;

public class FbaTrackingImportResult
{
    public int AppliedCount { get; set; }

    /// <summary>고객주문번호가 FBA 형식([SEND] … 총 N박스중 M번째)인데 해당 박스를 찾지 못한 행.</summary>
    public List<string> UnmatchedRows { get; } = [];

    /// <summary>같은 박스에 서로 다른 운송장번호가 실려있거나, 박스를 특정할 수 없거나(동일
    /// Shipment ID·박스순번의 미출고 박스가 복수), 이미 다른 운송장번호가 등록돼 있는 경우.</summary>
    public List<string> InconsistentBoxes { get; } = [];

    /// <summary>이미 같은 운송장번호가 들어있어 건너뛴 행(같은 파일을 다시 불러와도 안전하도록).</summary>
    public List<string> AlreadyAppliedRows { get; } = [];

    /// <summary>고객주문번호가 FBA 형식이 아니라 무시한 행 수 — 하루치 택배 결과 파일에 섞여 있는
    /// FBO·일반 택배 건이다. 무시가 정상 동작이지만 몇 건을 건너뛰었는지는 알려준다.</summary>
    public int SkippedRowCount { get; set; }

    /// <summary>미매칭/불일치가 하나도 없어야 실제로 반영된다(§7.2 — 부분 반영 금지, FBO와 동일 원칙).</summary>
    public bool Success => UnmatchedRows.Count == 0 && InconsistentBoxes.Count == 0;

    /// <summary>실패 사유 안내문. 발주 작성 화면과 이력 화면이 같은 문구를 쓰도록 여기서 만든다.</summary>
    public string BuildFailureMessage()
    {
        var msg = "미매칭/불일치가 있어 아무것도 반영되지 않았습니다.\n\n";
        if (UnmatchedRows.Count > 0)
        {
            msg += $"[미매칭 — 해당 박스를 찾지 못함]\n{string.Join("\n", UnmatchedRows)}\n\n";
        }
        if (InconsistentBoxes.Count > 0)
        {
            msg += $"[불일치/특정불가]\n{string.Join("\n", InconsistentBoxes)}\n\n";
        }
        return msg.TrimEnd();
    }

    /// <summary>반영 성공 후 상태바에 쓸 한 줄 요약(건너뛴 행·이미 반영된 행도 함께 알려준다).</summary>
    public string BuildSuccessSummary()
    {
        var summary = $"운송장번호 {AppliedCount}건을 적용했습니다.";
        if (AlreadyAppliedRows.Count > 0) summary += $" (이미 반영된 {AlreadyAppliedRows.Count}건 제외)";
        if (SkippedRowCount > 0) summary += $" FBA 형식이 아닌 {SkippedRowCount}행은 건너뛰었습니다.";
        return summary;
    }
}

/// <summary>
/// 택배사(CJ) 운송장 결과 파일을 읽어 FBA 박스(FbaBox)의 운송장번호를 채운다(기획서 §7.2).
/// FboTrackingImporter를 복제하되 매칭 기준이 다르다 — FBA는 수취지가 1곳 고정이라 모든 박스의
/// 반품부성명이 동일해 매칭키로 쓸 수 없으므로, 대신 박스 단위로 고유한 고객주문번호
/// (FbaBox.MatchKey, §7.1)를 매칭키로 쓴다.
///
/// 매칭은 고객주문번호 문자열 전체를 비교하지 않고 거기서 뽑은 (Shipment ID, 박스순번)으로 한다
/// (FbaKeyGenerator.BuildLookupKey) — 택배사가 내려주는 "운송장출력데이터 상세" 파일에는 어떤
/// 운송장이 어떤 박스인지에 대한 단서가 고객주문번호 칸밖에 없고, 그 칸의 총박스수 부분은 박스
/// 추가·삭제로 재채번되어 파일 발행 이후 달라질 수 있기 때문이다. 덕분에 택배사 원본 파일을
/// 가공 없이 그대로 넣을 수 있다(FBA가 아닌 행은 자동으로 무시).
/// 미매칭/불일치가 하나라도 있으면 전체 반영을 취소한다.
/// </summary>
public class FbaTrackingImporter
{
    private static readonly string[] TrackingHeaderCandidates = ["운송장번호", "이송장번호", "운송장 번호"];
    private static readonly string[] MatchKeyHeaderCandidates = ["고객주문번호", "주문번호", "고객주문 번호"];

    /// <summary>헤더가 1행이 아니라 제목/안내 행 아래에 있는 파일도 있으므로 앞쪽 몇 행을 훑는다.</summary>
    private const int HeaderSearchRowLimit = 10;

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

        var (headerRow, trackingCol, matchKeyCol) = FindHeader(worksheet);

        var result = new FbaTrackingImportResult();
        var rowsByLookupKey = ReadFileRows(worksheet, headerRow, trackingCol, matchKeyCol, result);
        var boxesByLookupKey = _repository.GetAllBoxesForTrackingMatch()
            .GroupBy(b => FbaKeyGenerator.BuildLookupKey(b.MatchKey), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var toApply = new List<(FbaBox Box, string TrackingNo)>();
        foreach (var (lookupKey, row) in rowsByLookupKey.OrderBy(kv => kv.Value.Label, StringComparer.Ordinal))
        {
            Resolve(lookupKey, row, boxesByLookupKey, result, toApply);
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

    /// <summary>파일 1행(=박스 1개)의 집계. 같은 박스로 여러 행이 나올 수 있어 운송장번호를 모은다.</summary>
    private sealed class FileRow
    {
        /// <summary>사용자에게 보여줄 원본 고객주문번호 표기.</summary>
        public string Label { get; init; } = string.Empty;
        public List<string> TrackingNos { get; } = [];
    }

    private static Dictionary<string, FileRow> ReadFileRows(
        ExcelWorksheet worksheet, int headerRow, int trackingCol, int matchKeyCol, FbaTrackingImportResult result)
    {
        var rows = new Dictionary<string, FileRow>(StringComparer.Ordinal);
        for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
        {
            var rawMatchKey = FbaKeyGenerator.NormalizeMatchKey(worksheet.Cells[row, matchKeyCol].Value?.ToString());
            var trackingNo = ReadCellAsText(worksheet.Cells[row, trackingCol].Value);
            if (rawMatchKey.Length == 0 && trackingNo.Length == 0) continue;

            // FBA 형식이 아닌 행(FBO 발주의 #FBO…, 일반 택배 건 등)은 이 파일에 섞여 있는 게
            // 정상이므로 미매칭으로 보고하지 않고 조용히 건너뛴다.
            if (!FbaKeyGenerator.TryParseMatchKey(rawMatchKey, out _))
            {
                result.SkippedRowCount++;
                continue;
            }
            // 여기까지 왔으면 FBA 박스를 가리키는 행인데 운송장번호 칸이 비어있다는 뜻 — 아직 송장이
            // 발행되지 않은 행이므로 반영 대상에서 빼되, 실패로 취급하지는 않는다.
            if (trackingNo.Length == 0)
            {
                result.SkippedRowCount++;
                continue;
            }

            var lookupKey = FbaKeyGenerator.BuildLookupKey(rawMatchKey);
            if (!rows.TryGetValue(lookupKey, out var fileRow))
            {
                fileRow = new FileRow { Label = rawMatchKey };
                rows[lookupKey] = fileRow;
            }
            fileRow.TrackingNos.Add(trackingNo);
        }
        return rows;
    }

    /// <summary>파일에서 모은 박스 1건을 실제 박스에 맞춰본다.</summary>
    private static void Resolve(
        string lookupKey,
        FileRow row,
        Dictionary<string, List<FbaBox>> boxesByLookupKey,
        FbaTrackingImportResult result,
        List<(FbaBox Box, string TrackingNo)> toApply)
    {
        var distinctTracking = row.TrackingNos.Distinct(StringComparer.Ordinal).ToList();
        if (distinctTracking.Count > 1)
        {
            result.InconsistentBoxes.Add($"{row.Label} (운송장 {string.Join("/", distinctTracking)})");
            return;
        }
        var trackingNo = distinctTracking[0];

        if (!boxesByLookupKey.TryGetValue(lookupKey, out var candidates) || candidates.Count == 0)
        {
            result.UnmatchedRows.Add($"{row.Label} ({trackingNo})");
            return;
        }

        // 같은 파일을 두 번 불러오거나, 이미 반영을 끝낸 과거 발주가 하루치 파일에 섞여 온 경우.
        if (candidates.Any(b => string.Equals(b.TrackingNo, trackingNo, StringComparison.Ordinal)))
        {
            result.AlreadyAppliedRows.Add($"{row.Label} ({trackingNo})");
            return;
        }

        var pending = candidates.Where(b => string.IsNullOrWhiteSpace(b.TrackingNo)).ToList();
        if (pending.Count == 0)
        {
            var existing = string.Join("/", candidates.Select(b => b.TrackingNo));
            result.InconsistentBoxes.Add($"{row.Label}: 이미 다른 운송장번호({existing})가 등록돼 있습니다. 파일값은 {trackingNo}입니다.");
            return;
        }
        // Shipment ID를 아직 입력하지 않은 발주가 여러 건 동시에 미출고로 남아있으면 (Shipment ID,
        // 박스순번) 조합이 겹칠 수 있다(§7.2) — 그 경우 특정 대신 불일치로 보고한다.
        if (pending.Count > 1)
        {
            var owners = string.Join(", ", pending.Select(b => $"{b.FbaNo} {b.BoxSeq}번박스"));
            result.InconsistentBoxes.Add($"{row.Label}: 같은 Shipment ID·박스순번의 미출고 박스가 {pending.Count}건({owners}) 있어 특정할 수 없습니다. 발주별 Shipment ID를 먼저 입력하세요.");
            return;
        }

        toApply.Add((pending[0], trackingNo));
    }

    private static (int HeaderRow, int TrackingCol, int MatchKeyCol) FindHeader(ExcelWorksheet worksheet)
    {
        var lastHeaderRow = Math.Min(worksheet.Dimension.End.Row, HeaderSearchRowLimit);
        for (int row = 1; row <= lastHeaderRow; row++)
        {
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = NormalizeHeader(worksheet.Cells[row, col].Value?.ToString());
                if (header.Length == 0 || columns.ContainsKey(header)) continue;
                columns[header] = col;
            }

            var trackingCol = FindColumn(columns, TrackingHeaderCandidates);
            var matchKeyCol = FindColumn(columns, MatchKeyHeaderCandidates);
            if (trackingCol is not null && matchKeyCol is not null) return (row, trackingCol.Value, matchKeyCol.Value);
        }

        throw new InvalidOperationException(
            $"필수 컬럼({string.Join("/", TrackingHeaderCandidates)}, {string.Join("/", MatchKeyHeaderCandidates)})을 " +
            $"결과 파일 앞 {lastHeaderRow}행에서 찾지 못했습니다. 택배사에서 내려받은 운송장 결과 파일인지 확인하세요.");
    }

    /// <summary>
    /// 운송장번호 칸을 문자열로 읽는다. "6004-0294-8523"처럼 하이픈이 섞여 있으면 문자열로 들어오지만
    /// 하이픈 없이 순수 숫자로 내려오는 파일은 엑셀이 double로 보관해 그냥 ToString()하면
    /// 지수표기(6.0040294E+12)가 되므로 정수 표기로 되돌린다.
    /// </summary>
    private static string ReadCellAsText(object? value) => value switch
    {
        null => string.Empty,
        double d => d == Math.Floor(d) && !double.IsInfinity(d)
            ? ((decimal)d).ToString("0", CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.############################", CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? string.Empty,
    };

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
