using MiniERP2.DataLoaders;
using MiniERP2.Models;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// 누적발주서 송장번호 역기입 결과. 어느 행에 왜 못 썼는지까지 담는다 — "몇 건 썼다"만 알려주면
/// 거래처에 보낼 파일이 맞게 채워졌는지 확인할 방법이 없다.
/// </summary>
public sealed class CumulativeTrackingWriteResult
{
    public int FileDataRows { get; set; }
    public int WrittenRows { get; set; }
    public int CourierWrittenRows { get; set; }
    public int AlreadySameRows { get; set; }
    public int ConflictRows { get; set; }
    public int NoHistoryRows { get; set; }
    public int AmbiguousRows { get; set; }
    public List<string> ConflictSamples { get; } = [];
    public List<string> NoHistorySamples { get; } = [];
    public List<string> AmbiguousSamples { get; } = [];
    public string? BackupPath { get; set; }
    public string SheetName { get; set; } = "";
    public int HeaderRow { get; set; }
}

/// <summary>
/// 출고 이력(DB)에 들어 있는 송장번호를 누적발주서 엑셀 파일에 거꾸로 써넣는다.
/// "누적발주서 송장번호 입력"(파일 → DB)의 반대 방향으로, 택배사 결과 파일로 이력에 채운
/// 운송장번호를 거래처에 회신할 누적발주서에 반영하는 게 목적이다.
///
/// 행을 찾는 기준은 파일 → DB 방향과 똑같이 수령인+주소 일치(전화번호는 후보 좁히기용)로 두었다.
/// 한 수령인·주소에 서로 다른 송장번호가 여럿이면(같은 지점이 여러 번 주문) 품목명·수량으로 한 번 더
/// 좁히고, 그래도 하나로 못 좁히면 쓰지 않고 보고만 한다 — 회신용 파일에 남의 송장번호가 들어가는 건
/// 빈칸으로 남는 것보다 훨씬 나쁘다.
/// </summary>
public static class CumulativeOrderTrackingWriter
{
    /// <summary>비교 전용 정규화(공백 차이·대소문자 차이로 같은 값이 갈리지 않도록).</summary>
    private static string Norm(string? s) =>
        string.Join(" ", (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>송장번호는 하이픈 유무만 다른 경우가 흔해 숫자만 남겨 비교한다.</summary>
    private static string NormTracking(string? s) =>
        new((s ?? "").Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// 파일을 열어 매칭되는 행의 송장번호 칸을 채우고 저장한다. 저장 전 원본을 같은 폴더에 복사해
    /// 둔다(엑셀 파일을 프로그램이 다시 쓰는 작업이라 되돌릴 수단을 반드시 남긴다).
    /// </summary>
    /// <param name="filePath">누적발주서 파일 경로(.xlsx만 지원 — CSV/xls는 서식·암호를 보존해 되쓸 수 없다)</param>
    /// <param name="password">암호가 걸린 파일이면 그 비밀번호(저장할 때 같은 암호로 다시 건다)</param>
    /// <param name="channelConfig">파일을 해석할 채널설정(발주서매핑의 시트·헤더행·열 이름을 그대로 쓴다)</param>
    /// <param name="history">써넣을 출고 이력(송장번호가 있는 건만 쓰인다)</param>
    /// <param name="overwriteExisting">이미 다른 송장번호가 적힌 칸도 덮어쓸지 여부</param>
    public static CumulativeTrackingWriteResult Write(
        string filePath,
        string? password,
        ChannelConfig channelConfig,
        IReadOnlyList<OutboundDetail> history,
        bool overwriteExisting = false)
    {
        if (!Path.GetExtension(filePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("누적발주서 역기입은 .xlsx 파일만 지원합니다(CSV·xls는 원본 서식과 암호를 유지한 채 되쓸 수 없습니다).");

        var result = new CumulativeTrackingWriteResult();

        using (var package = ExcelFileOpener.Open(filePath, password))
        {
            var layout = OrderLoader.ResolveLayout(package, channelConfig);
            var sheet = layout.Worksheet;
            result.SheetName = sheet.Name;
            result.HeaderRow = layout.HeaderRow;

            if (!layout.ColumnByField.TryGetValue(StdField.Recipient, out var recipientCol))
                throw new InvalidOperationException(BuildMissingColumnMessage(channelConfig, layout, StdField.Recipient, "수령인"));
            if (!layout.ColumnByField.TryGetValue(StdField.TrackingNo, out var trackingCol))
                throw new InvalidOperationException(BuildMissingColumnMessage(channelConfig, layout, StdField.TrackingNo, "송장번호"));

            layout.ColumnByField.TryGetValue(StdField.Address, out var addressCol);
            // 주소가 두 열로 나뉜 발주서는 이력에도 합쳐진 주소가 저장돼 있으므로(OrderLoader.CombineAddress),
            // 매칭 키를 만들 때도 같은 방식으로 합쳐야 한 글자도 안 틀리고 맞는다.
            layout.ColumnByField.TryGetValue(StdField.AddressDetail, out var addressDetailCol);
            layout.ColumnByField.TryGetValue(StdField.Phone, out var phoneCol);
            layout.ColumnByField.TryGetValue(StdField.ProductName, out var productCol);
            layout.ColumnByField.TryGetValue(StdField.Quantity, out var qtyCol);
            var courierCol = ResolveWriteColumn(layout, channelConfig, StdField.CourierName);
            // 채널설정에 택배사 고정값(예: 푸디의 "CJ택배")이 있으면 그 값을 쓴다. 이력의 택배사명이
            // 비어 있는 채널이 대부분이라(택배사 결과 파일에서 번호만 받아오므로) 실질적으로 이쪽이
            // 거래처 회신 파일에 택배사를 채워주는 유일한 경로다.
            var fixedCourier = channelConfig.OrderFieldMappings.TryGetValue(StdField.CourierName, out var courierMapping)
                ? courierMapping.FixedValue
                : null;

            // 송장번호가 있는 이력만 수령인+주소로 묶어둔다(주소 열이 매핑돼 있지 않으면 이름만으로).
            var historyByKey = history
                .Where(d => !string.IsNullOrWhiteSpace(d.TrackingNo) && !string.IsNullOrWhiteSpace(d.Recipient))
                .GroupBy(d => (Name: Norm(d.Recipient), Addr: addressCol > 0 ? Norm(d.Address) : ""))
                .ToDictionary(g => g.Key, g => g.ToList());

            var lastRow = sheet.Dimension?.End.Row ?? layout.HeaderRow;
            for (int row = layout.HeaderRow + 1; row <= lastRow; row++)
            {
                var recipient = CellText(sheet, row, recipientCol);
                if (string.IsNullOrWhiteSpace(recipient)) continue;
                result.FileDataRows++;

                var existing = CellText(sheet, row, trackingCol);
                var address = addressCol > 0
                    ? OrderLoader.CombineAddress(
                        CellText(sheet, row, addressCol),
                        addressDetailCol > 0 ? CellText(sheet, row, addressDetailCol) : null) ?? ""
                    : "";
                var key = (Name: Norm(recipient), Addr: addressCol > 0 ? Norm(address) : "");

                if (!historyByKey.TryGetValue(key, out var candidates))
                {
                    // 이미 파일에 송장번호가 있으면 "이력에 없다"고 보고할 일이 아니다 — 지난달 주문처럼
                    // 이번 조회 범위 밖이라 이력 후보에 없을 뿐, 파일은 이미 완성돼 있는 행이다.
                    if (string.IsNullOrWhiteSpace(existing))
                    {
                        result.NoHistoryRows++;
                        if (result.NoHistorySamples.Count < 5)
                            result.NoHistorySamples.Add($"{row}행 [{recipient}]");
                    }
                    continue;
                }

                // 전화번호 → 품목명 → 수량 순으로 후보를 좁힌다(둘 중 한쪽이라도 값이 비어 있으면
                // 구분 정보가 없는 것이므로 그 단계는 건너뛴다).
                var narrowed = candidates;
                narrowed = NarrowBy(narrowed, phoneCol > 0 ? CellText(sheet, row, phoneCol) : null, d => d.Phone);
                if (DistinctTracking(narrowed).Count > 1)
                    narrowed = NarrowBy(narrowed, productCol > 0 ? CellText(sheet, row, productCol) : null, d => d.ProductName);
                if (DistinctTracking(narrowed).Count > 1 && qtyCol > 0)
                {
                    var fileQty = CellText(sheet, row, qtyCol);
                    if (int.TryParse(fileQty, out var q))
                    {
                        var byQty = narrowed.Where(d => d.Qty == q).ToList();
                        if (byQty.Count > 0) narrowed = byQty;
                    }
                }

                var trackings = DistinctTracking(narrowed);
                if (trackings.Count == 0)
                {
                    result.NoHistoryRows++;
                    if (result.NoHistorySamples.Count < 5) result.NoHistorySamples.Add($"{row}행 [{recipient}]");
                    continue;
                }

                // 이미 적힌 값이 후보 중 하나와 같으면 그 행은 끝난 행이다 — 후보가 여럿이라는 이유로
                // "확인 필요"에 세면, 지난 주문이 쌓인 누적발주서일수록 멀쩡한 행이 무더기로 경고에
                // 잡혀 정작 봐야 할 행이 묻힌다.
                if (!string.IsNullOrWhiteSpace(existing) &&
                    trackings.Any(t => NormTracking(t) == NormTracking(existing)))
                {
                    result.AlreadySameRows++;
                    // 송장번호는 이미 맞게 적혀 있어도 택배사 칸은 비어 있을 수 있다(이 기능이 생기기
                    // 전에 채운 행). 이력과 맞는 행으로 확인된 이상 택배사도 마저 채워준다.
                    FillCourierIfEmpty(sheet, row, courierCol, narrowed, fixedCourier, result);
                    continue;
                }

                if (trackings.Count > 1)
                {
                    result.AmbiguousRows++;
                    if (result.AmbiguousSamples.Count < 5)
                        result.AmbiguousSamples.Add($"{row}행 [{recipient}] → 후보 {string.Join(", ", trackings.Take(4))}");
                    continue;
                }

                var trackingNo = trackings[0];
                if (!string.IsNullOrWhiteSpace(existing) && !overwriteExisting)
                {
                    result.ConflictRows++;
                    if (result.ConflictSamples.Count < 5)
                        result.ConflictSamples.Add($"{row}행 [{recipient}] 파일 \"{existing}\" ↔ 이력 \"{trackingNo}\"");
                    continue;
                }

                // 하이픈이 섞인 송장번호를 엑셀이 수식·날짜로 해석하지 않도록 문자열로 넣는다.
                sheet.Cells[row, trackingCol].Value = trackingNo;
                result.WrittenRows++;

                FillCourierIfEmpty(sheet, row, courierCol, narrowed, fixedCourier, result);
            }

            if (result.WrittenRows == 0 && result.CourierWrittenRows == 0) return result;

            result.BackupPath = CreateBackup(filePath);

            // 암호가 걸린 파일로 열었으면 같은 암호로 다시 걸어 저장한다 — 명시하지 않으면 거래처와
            // 주고받는 파일의 암호가 소리 없이 풀린 채 저장될 위험이 있다.
            if (!string.IsNullOrEmpty(password))
            {
                package.Encryption.IsEncrypted = true;
                package.Encryption.Password = password;
            }
            package.Save();
        }

        return result;
    }

    /// <summary>
    /// 택배사 칸이 비어 있을 때만 채운다(거래처가 직접 적어둔 값은 건드리지 않는다). 값은 이력의
    /// 택배사명을 먼저 쓰고, 없으면 채널설정의 택배사 고정값을 쓴다.
    /// </summary>
    private static void FillCourierIfEmpty(ExcelWorksheet sheet, int row, int courierCol,
        List<OutboundDetail> candidates, string? fixedCourier, CumulativeTrackingWriteResult result)
    {
        if (courierCol <= 0 || !string.IsNullOrWhiteSpace(CellText(sheet, row, courierCol))) return;

        var courier = candidates.Select(d => d.CourierName).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        if (string.IsNullOrWhiteSpace(courier)) courier = fixedCourier;
        if (string.IsNullOrWhiteSpace(courier)) return;

        sheet.Cells[row, courierCol].Value = courier;
        result.CourierWrittenRows++;
    }

    /// <summary>
    /// 쓸 열을 찾는다. ResolveLayout은 고정값이 설정된 필드의 열을 잡아두지 않는데(읽을 땐 열이
    /// 필요 없으니까), 쓸 때는 "무엇을 쓸지(고정값)"와 "어디에 쓸지(열)"가 둘 다 필요해서 여기서
    /// 한 번 더 찾는다. 못 찾으면 0(해당 열 없음).
    /// </summary>
    private static int ResolveWriteColumn(OrderSheetLayout layout, ChannelConfig config, StdField field)
    {
        if (layout.ColumnByField.TryGetValue(field, out var col)) return col;

        var column = config.OrderFieldMappings.TryGetValue(field, out var mapping) ? mapping.Column : null;
        return !string.IsNullOrWhiteSpace(column) && layout.HeadersInFile.TryGetValue(column, out var byHeader) ? byHeader : 0;
    }

    private static List<OutboundDetail> NarrowBy(List<OutboundDetail> candidates, string? fileValue, Func<OutboundDetail, string?> selector)
    {
        if (candidates.Count <= 1 || string.IsNullOrWhiteSpace(fileValue)) return candidates;
        var target = Norm(fileValue);
        var narrowed = candidates.Where(d =>
        {
            var v = Norm(selector(d));
            return v.Length == 0 || v == target;
        }).ToList();
        return narrowed.Count > 0 ? narrowed : candidates;
    }

    private static List<string> DistinctTracking(List<OutboundDetail> candidates) =>
        candidates
            .Where(d => !string.IsNullOrWhiteSpace(d.TrackingNo))
            .GroupBy(d => NormTracking(d.TrackingNo))
            .Select(g => g.First().TrackingNo!)
            .ToList();

    private static string CellText(ExcelWorksheet sheet, int row, int col)
    {
        var cell = sheet.Cells[row, col];
        return (cell.Value is string s ? s : cell.Text) ?? "";
    }

    private static string CreateBackup(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath)!;
        var backup = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(filePath)}.bak-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(filePath)}");
        File.Copy(filePath, backup, overwrite: true);
        return backup;
    }

    /// <summary>
    /// "헤더를 못 찾았다"는 오류에서 가장 답답한 건 파일에 실제로 어떤 열이 있는지 안 보이는 점이라,
    /// 채널설정에 적힌 열 이름과 파일의 실제 헤더 목록을 나란히 보여준다.
    /// </summary>
    private static string BuildMissingColumnMessage(ChannelConfig config, OrderSheetLayout layout, StdField field, string label)
    {
        var configured = config.OrderFieldMappings.TryGetValue(field, out var m) ? m.Column : null;
        var headers = layout.HeadersInFile.Count > 0 ? string.Join(", ", layout.HeadersInFile.Keys) : "(헤더 행이 비어 있음)";
        return string.IsNullOrWhiteSpace(configured)
            ? $"채널 '{config.ChannelName}'의 발주서매핑에 '{label}' 열이 지정돼 있지 않습니다.\n채널설정 > 발주서 매핑 탭에서 먼저 지정하세요.\n\n" +
              $"파일 '{layout.Worksheet.Name}' 시트 {layout.HeaderRow}행의 실제 헤더: {headers}"
            : $"'{label}' 열로 지정된 \"{configured}\"을(를) 파일에서 찾지 못했습니다.\n" +
              $"시트 '{layout.Worksheet.Name}'의 {layout.HeaderRow}행을 헤더로 읽었습니다.\n\n" +
              $"파일의 실제 헤더: {headers}\n\n" +
              "채널설정 > 발주서 매핑에서 열 이름(또는 헤더 행 번호·시트)을 파일과 똑같이 맞춰주세요.";
    }
}
