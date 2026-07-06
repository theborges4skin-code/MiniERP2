using System.Diagnostics;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 온라인 정산 파일(엑셀)을 읽어 채널별 손익 공식을 적용한 결과로 변환하는 기능을 제공합니다.
/// 기획서 5.6절 '이익분석(자동)'.
/// </summary>
public class SettlementLoader
{
    /// <summary>
    /// 직전 LoadFromFileAsync 호출에서, 설정된 헤더 행에서 매핑된 표준 필드를 하나도 찾지 못했는지 여부입니다.
    /// 헤더 행이 비어있거나 셀이 병합되어 있을 때(설정 오류) 주로 발생하며, 호출 측에서 경고를 띄우는 데 사용합니다.
    /// </summary>
    public bool LastLoadHeaderRowLooksEmpty { get; private set; }

    /// <summary>
    /// 지정된 엑셀 파일 경로에서 정산 데이터를 비동기적으로 로드하고 이익을 계산합니다.
    /// </summary>
    /// <param name="skuMapper">SKU 매핑을 수행할 SkuMapper 인스턴스</param>
    /// <param name="itemRepository">제조원가 조회를 위한 Repository</param>
    /// <param name="channelConfig">데이터를 해석할 채널 설정</param>
    /// <param name="filePath">엑셀 파일 경로</param>
    /// <param name="password">암호로 보호된 파일일 경우의 비밀번호. 모르면 생략하고, 실패 시 EncryptedExcelFileException을 받아 재시도하세요.</param>
    /// <param name="channelSkuRepository">
    /// CSKU 코드 → 마스터SKU 변환을 위한 Repository. 매핑 규칙의 TargetSku가 CSKU 코드인 경우
    /// 원가 조회 전에 실제 마스터SKU로 바꿔야 한다. 생략하면 새로 생성한다.
    /// </param>
    public async Task<List<SettlementData>> LoadFromFileAsync(SkuMapper skuMapper, ItemRepository itemRepository, ChannelConfig channelConfig, string filePath, string? password = null, ChannelSkuRepository? channelSkuRepository = null)
    {
        LastLoadHeaderRowLooksEmpty = false;
        var rows = new List<SettlementData>();
        channelSkuRepository ??= new ChannelSkuRepository();
        var fileName = Path.GetFileName(filePath);

        await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            DiagnosticsLogger.Log($"[SettlementLoader] '{fileName}' 열기 시작 (채널={channelConfig.ChannelCode})");
            using var package = Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? CsvWorkbookReader.LoadAsPackage(filePath)
                : ExcelFileOpener.Open(filePath, password);
            DiagnosticsLogger.Log($"[SettlementLoader] '{fileName}' 엑셀 패키지 열기 완료 ({stopwatch.Elapsed.TotalSeconds:F2}s)");

            var firstValidMapping = channelConfig.SettlementFieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
            if (firstValidMapping == null)
            {
                throw new InvalidOperationException($"채널 '{channelConfig.ChannelName}'에 유효한 필드 매핑 설정이 없습니다.");
            }

            var sheetName = firstValidMapping.SheetName;
            var headerRow = firstValidMapping.HeaderRow;

            var worksheet = !string.IsNullOrEmpty(sheetName)
                ? package.Workbook.Worksheets[sheetName]
                : package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                throw new FileNotFoundException($"엑셀 파일에서 '{sheetName ?? "첫 번째"}' 시트를 찾을 수 없습니다.");
            }

            // HeaderRowDetectionColumn이 설정된 경우 지정 컬럼명이 있는 행을 헤더로 자동 탐지한다.
            // 아마존처럼 파일마다 메타 행 수가 달라 헤더 행 위치가 고정되지 않는 채널에 사용한다.
            if (!string.IsNullOrWhiteSpace(channelConfig.HeaderRowDetectionColumn) && worksheet?.Dimension != null)
            {
                var target = channelConfig.HeaderRowDetectionColumn;
                bool detected = false;
                for (int r = 1; r <= Math.Min(worksheet.Dimension.End.Row, 50) && !detected; r++)
                {
                    for (int c = 1; c <= worksheet.Dimension.End.Column && !detected; c++)
                    {
                        if (string.Equals(worksheet.Cells[r, c].Value?.ToString(), target, StringComparison.OrdinalIgnoreCase))
                        {
                            headerRow = r;
                            DiagnosticsLogger.Log($"[SettlementLoader] 헤더 행 자동 탐지 성공 — '{target}' 발견 → {r}행");
                            detected = true;
                        }
                    }
                }
                if (!detected)
                    DiagnosticsLogger.Log($"[SettlementLoader] 헤더 행 자동 탐지 실패 — '{target}'을(를) 50행 이내에서 찾지 못함, 설정값({headerRow}행) 사용");
            }

            DiagnosticsLogger.Log($"[SettlementLoader] 시트='{worksheet.Name}' 헤더행={headerRow} 전체범위={worksheet.Dimension?.Address} " +
                $"(보고된 행수={worksheet.Dimension?.End.Row}, 열수={worksheet.Dimension?.End.Column}) ({stopwatch.Elapsed.TotalSeconds:F2}s)");

            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrEmpty(header) && !headerToIndexMap.ContainsKey(header))
                {
                    headerToIndexMap[header] = col;
                }
            }

            var stdFieldToIndexMap = new Dictionary<StdField, int>();
            var fixedValues = new Dictionary<StdField, string>();
            foreach (var (stdField, mapping) in channelConfig.SettlementFieldMappings)
            {
                if (!string.IsNullOrEmpty(mapping.FixedValue))
                {
                    fixedValues[stdField] = mapping.FixedValue;
                }
                else if (!string.IsNullOrEmpty(mapping.Column) && headerToIndexMap.TryGetValue(mapping.Column, out var index))
                {
                    stdFieldToIndexMap[stdField] = index;
                }
            }

            // 설정된 매핑이 하나도 헤더 행과 맞지 않으면(헤더 행이 비어있거나 셀이 병합된 경우 등)
            // 이후 모든 값이 공란으로 나오게 되므로, 호출 측이 경고할 수 있도록 표시해둔다.
            LastLoadHeaderRowLooksEmpty = stdFieldToIndexMap.Count == 0 && fixedValues.Count == 0;

            // 기획서 5.4절: 보조소스(GrowthAuxSource) JOIN 준비.
            // 보조 시트별로 (대상 표준필드 -> 키값맵)을 만들고, 메인 시트에서 같은 이름의 키 컬럼을 찾아둔다.
            var auxValueMaps = new Dictionary<StdField, Dictionary<string, decimal>>();
            var auxMainKeyColumns = new Dictionary<StdField, int>();

            // CFS 모드 활성화 시 GrowthAuxSource의 HandlingFee/ShippingFee를 건너뜁니다.
            // CFS 로더가 VAT포함 금액으로 직접 할당하므로 중복 적용을 막기 위함입니다.
            var cfsFeeActive = channelConfig.GrowthCfsFee != null
                && channelConfig.ChannelType == ChannelType.CoupangGrowth;

            if (cfsFeeActive)
                DiagnosticsLogger.Log($"[SettlementLoader] CFS 모드 활성화 — ShippingFee·HandlingFee 보조소스를 건너뜁니다 (배송비·입출고비는 0으로 표시됨, 수익은 CFS 공식으로 계산).");

            foreach (var auxSource in channelConfig.GrowthAuxSources.Where(a => a.Enabled
                && !(cfsFeeActive && a.TargetStdField is StdField.HandlingFee or StdField.ShippingFee)))
            {
                if (string.IsNullOrEmpty(auxSource.SheetName) || string.IsNullOrEmpty(auxSource.KeyHeader) || string.IsNullOrEmpty(auxSource.ValueHeader))
                {
                    DiagnosticsLogger.Log($"[SettlementLoader] 보조소스({auxSource.TargetStdField}) 건너뜀 — SheetName·KeyHeader·ValueHeader 중 하나 이상이 비어 있음.");
                    continue;
                }
                if (!headerToIndexMap.TryGetValue(auxSource.KeyHeader, out var mainKeyCol))
                {
                    DiagnosticsLogger.Log($"[SettlementLoader] 보조소스({auxSource.TargetStdField}) 건너뜀 — 키 헤더 '{auxSource.KeyHeader}'을(를) 메인 시트에서 찾을 수 없음. 실제 헤더 목록: [{string.Join(", ", headerToIndexMap.Keys.Take(20))}]");
                    continue;
                }

                var auxSheet = package.Workbook.Worksheets[auxSource.SheetName];
                if (auxSheet?.Dimension == null)
                {
                    var sheetNames = string.Join(", ", package.Workbook.Worksheets.Select(s => s.Name));
                    DiagnosticsLogger.Log($"[SettlementLoader] 보조소스({auxSource.TargetStdField}) 건너뜀 — 시트명 '{auxSource.SheetName}'을(를) 파일에서 찾을 수 없음. 실제 시트 목록: [{sheetNames}]");
                    continue;
                }

                var auxHeaderToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= auxSheet.Dimension.End.Column; col++)
                {
                    var header = auxSheet.Cells[auxSource.HeaderRow, col].Value?.ToString();
                    if (!string.IsNullOrEmpty(header) && !auxHeaderToIndex.ContainsKey(header))
                    {
                        auxHeaderToIndex[header] = col;
                    }
                }

                if (!auxHeaderToIndex.TryGetValue(auxSource.KeyHeader, out var auxKeyCol) || !auxHeaderToIndex.TryGetValue(auxSource.ValueHeader, out var auxValueCol))
                {
                    DiagnosticsLogger.Log($"[SettlementLoader] 보조소스({auxSource.TargetStdField}) 건너뜀 — 시트 '{auxSource.SheetName}' 헤더 행({auxSource.HeaderRow})에서 키 '{auxSource.KeyHeader}' 또는 값 '{auxSource.ValueHeader}'을(를) 찾을 수 없음. 실제 헤더 목록: [{string.Join(", ", auxHeaderToIndex.Keys.Take(20))}]");
                    continue;
                }

                var pairs = new List<(string Key, decimal Value)>();
                for (int auxRow = auxSource.HeaderRow + 1; auxRow <= auxSheet.Dimension.End.Row; auxRow++)
                {
                    var key = auxSheet.Cells[auxRow, auxKeyCol].Value?.ToString();
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    var value = decimal.TryParse(auxSheet.Cells[auxRow, auxValueCol].Value?.ToString(), out var parsedValue) ? parsedValue : 0m;
                    pairs.Add((key, value));
                }

                auxValueMaps[auxSource.TargetStdField] = GrowthAuxJoinEngine.BuildValueMap(pairs);
                auxMainKeyColumns[auxSource.TargetStdField] = mainKeyCol;
                DiagnosticsLogger.Log($"[SettlementLoader] 보조소스({auxSource.TargetStdField}) 준비 완료 — 시트='{auxSource.SheetName}', 키={pairs.Count}건 로드됨.");
            }

            // 엑셀 파일에 실제 데이터보다 훨씬 뒤까지 서식(테두리/배경색 등)이 적용되어 있으면,
            // EPPlus의 worksheet.Dimension이 그 서식 범위까지를 "데이터 범위"로 보고해 실제로는
            // 비어있는 수만~수십만 행을 끝까지 순회하게 된다(파일에 보이는 데이터 양과 무관하게
            // 로드가 느려지는 가장 흔한 원인). 상품명/옵션명이 모두 빈 행이 일정 수 이상 연속되면
            // 더 이상 실제 데이터가 없다고 보고 그 지점에서 멈춘다.
            const int maxConsecutiveBlankRows = 200;
            var consecutiveBlankRows = 0;

            DiagnosticsLogger.Log($"[SettlementLoader] 행 순회 시작 ({stopwatch.Elapsed.TotalSeconds:F2}s)");

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                if (row % 500 == 0)
                {
                    DiagnosticsLogger.Log($"[SettlementLoader] {row}행째 처리 중... (지금까지 {rows.Count}건 적재, {stopwatch.Elapsed.TotalSeconds:F2}s)");
                }

                var productName = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.ProductName);
                var optionName = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.OptionName);

                if (string.IsNullOrWhiteSpace(productName) && string.IsNullOrWhiteSpace(optionName))
                {
                    if (++consecutiveBlankRows >= maxConsecutiveBlankRows) break;
                    continue;
                }
                consecutiveBlankRows = 0;

                var qty = int.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Quantity), out var qtyValue) ? qtyValue : 0;
                var settlement = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.SettlementAmount), out var settlementValue) ? settlementValue : 0m;
                var shipping = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.ShippingFee), out var shippingValue) ? shippingValue : 0m;
                var fee = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.HandlingFee), out var feeValue) ? feeValue : 0m;
                var revenue = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Revenue), out var revenueValue) ? revenueValue : 0m;
                var trackingNo = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.TrackingNo);
                var orderNo = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.OrderNo);
                var taxNo = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.TaxNo);
                var eventType = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.EventType);

                var settlementData = new SettlementData
                {
                    ChannelCode = channelConfig.ChannelCode,
                    ProductName = productName,
                    OptionName = optionName,
                    Qty = qty,
                    Settlement = settlement,
                    Shipping = shipping,
                    Fee = fee,
                    Revenue = revenue,
                    TrackingNo = trackingNo,
                    OrderNo = orderNo,
                    TaxNo = taxNo,
                    EventType = eventType,
                    RawValues = headerToIndexMap.ToDictionary(
                        kv => kv.Key,
                        kv => worksheet.Cells[row, kv.Value].Value?.ToString() ?? string.Empty),
                };

                foreach (var (targetStdField, valueMap) in auxValueMaps)
                {
                    var joinKey = worksheet.Cells[row, auxMainKeyColumns[targetStdField]].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(joinKey) && valueMap.TryGetValue(joinKey, out var joinedValue))
                    {
                        GrowthAuxJoinEngine.Apply(settlementData, targetStdField, joinedValue);
                    }
                }

                ApplyMappingAndProfit(settlementData, skuMapper, itemRepository, channelConfig, channelSkuRepository);
                rows.Add(settlementData);
            }

            DiagnosticsLogger.Log($"[SettlementLoader] 행 순회 완료 — {rows.Count}건 적재 ({stopwatch.Elapsed.TotalSeconds:F2}s)");

            // 기획서 5.6절 특수 규칙: 채널별 행 필터 및 배송비 후처리
            ProfitCalculator.ApplyCoupangRocketFilter(channelConfig.ChannelType, rows);
            ProfitCalculator.ApplyElevenStreetFilter(channelConfig.ChannelType, rows);
            ProfitCalculator.ApplyCoupangGeneralShippingAggregation(channelConfig.ChannelType, rows);
            ProfitCalculator.ApplyAmazonTransferFilter(channelConfig.ChannelType, rows, channelConfig.AmazonTransferTypeValue);
            DiagnosticsLogger.Log($"[SettlementLoader] '{fileName}' 전체 완료 ({stopwatch.Elapsed.TotalSeconds:F2}s)");
        });

        return rows;
    }

    /// <summary>
    /// SKU 매핑(우선순위 평가)과 손익 계산을 한 행에 적용한다. 정산파일을 읽을 때 행마다 호출되지만,
    /// 마감/이익분석 창에서 미매핑 행에 사용자가 즉석으로 매핑 규칙을 추가했을 때 그 한 행만 다시
    /// 계산하는 용도로도 재사용한다(public).
    /// </summary>
    /// <param name="itemCache">
    /// 마스터SKU→ItemModel 캐시(선택). 없으면 매번 <paramref name="itemRepository"/>로 DB를 새로
    /// 조회한다(행 하나만 다시 계산할 때는 무시할 수 있는 비용). 정산 데이터 전체를 한꺼번에
    /// 다시 매핑할 때(<c>SettlementForm.ReapplyMappingForAllRows</c>)는 행마다 매번 새 SQLite
    /// 연결을 여는 비용이 누적돼 수천 건이면 "저장 후 반응 없음"으로 보일 만큼 느려질 수 있어서
    /// 호출 쪽에서 한 번만 만든 캐시를 넘겨 받는다.
    /// </param>
    /// <param name="cskuCache">위와 같은 이유로 채널SKU(CSKU)→마스터SKU 변환도 캐시할 수 있게 한다.</param>
    public static void ApplyMappingAndProfit(
        SettlementData data,
        SkuMapper skuMapper,
        ItemRepository itemRepository,
        ChannelConfig channelConfig,
        ChannelSkuRepository channelSkuRepository,
        IReadOnlyDictionary<string, ItemModel>? itemCache = null,
        IReadOnlyDictionary<string, ChannelSkuModel>? cskuCache = null)
    {
        var orderItem = new OfsOrderItem
        {
            ProductName = data.ProductName,
            OptionName = data.OptionName,
        };
        skuMapper.ApplyMapping(orderItem);
        data.Msku = orderItem.MappedSku;
        data.Status = orderItem.Status;

        if (string.IsNullOrWhiteSpace(data.Msku))
        {
            data.Profit = 0m;
            data.ProductGroup = null;
            return;
        }

        // data.Msku는 매핑 규칙의 TargetSku 값이며, CSKU로 등록되어 있다면 그 자체는 마스터SKU가
        // 아니라 채널 전용 코드일 수 있다. 원가는 항상 실제 마스터SKU 기준이어야 하므로 변환한다.
        string masterSku;
        if (cskuCache != null)
        {
            masterSku = cskuCache.TryGetValue(data.Msku, out var csku) ? csku.Msku : data.Msku;
        }
        else
        {
            masterSku = channelSkuRepository.ResolveMasterSku(channelConfig.ChannelCode, data.Msku);
        }

        var item = itemCache != null
            ? itemCache.GetValueOrDefault(masterSku)
            : itemRepository.GetBySku(masterSku);
        data.ProductGroup = item?.ProductGroup;
        if (item == null)
        {
            data.Status = "원가 정보 없음";
            data.Profit = 0m;
            return;
        }

        var cfsMode = channelConfig.GrowthCfsFee != null && channelConfig.ChannelType == ChannelType.CoupangGrowth;
        data.Profit = ProfitCalculator.Calculate(channelConfig.ChannelType, data.Settlement, item.CostPrice, data.Qty, data.Shipping, data.Fee, channelConfig.ExchangeRate, cfsMode);
    }

    /// <summary>
    /// 쿠팡로켓 계산서발행내역 파일에서 세금계산서번호 → 계산서발행일 맵을 읽습니다.
    /// 입고상세내역 파일과 함께 로드할 때 JOIN 키 기반으로 각 행에 발행일을 붙이는 데 사용합니다.
    /// </summary>
    /// <param name="filePath">계산서발행내역 엑셀 파일 경로</param>
    /// <param name="keyHeader">세금계산서번호 열의 헤더명</param>
    /// <param name="dateHeader">계산서발행일 열의 헤더명</param>
    /// <param name="headerRow">헤더 행 번호 (기본값 1)</param>
    public async Task<Dictionary<string, string>> BuildRocketInvoiceDateMapAsync(
        string filePath, string keyHeader, string dateHeader, int headerRow = 1)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() =>
        {
            using var package = ExcelFileOpener.Open(filePath);
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null) return;

            int? keyCol = null, dateCol = null;
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (string.Equals(header, keyHeader, StringComparison.OrdinalIgnoreCase)) keyCol = col;
                if (string.Equals(header, dateHeader, StringComparison.OrdinalIgnoreCase)) dateCol = col;
            }
            if (keyCol == null || dateCol == null) return;

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var key = NormalizeInvoiceKey(worksheet.Cells[row, keyCol.Value].Value?.ToString());
                var date = worksheet.Cells[row, dateCol.Value].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(date) && !map.ContainsKey(key))
                    map[key] = date;
            }
        });
        return map;
    }

    /// <summary>
    /// 계산서번호 JOIN 키 정규화: 엑셀 숫자 셀이 "29855741.0"으로 읽힐 때 소수점 이하를 제거하고
    /// 콤마/공백 등 숫자가 아닌 문자를 제거한다. 빌링 파일과 입고상세 파일 양쪽에 동일하게 적용해야 한다.
    /// </summary>
    public static string NormalizeInvoiceKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();
        var dotIdx = s.IndexOf('.');
        if (dotIdx >= 0) s = s[..dotIdx];
        return string.Concat(s.Where(char.IsDigit));
    }

    private string? GetValue(ExcelWorksheet worksheet, int row, Dictionary<StdField, int> map, Dictionary<StdField, string> fixedValues, StdField field)
    {
        if (fixedValues.TryGetValue(field, out var fixedValue)) return fixedValue;

        return map.TryGetValue(field, out var colIndex)
            ? worksheet.Cells[row, colIndex].Value?.ToString()
            : null;
    }
}
