using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 쿠팡그로스 CFS(쿠팡풀필먼트서비스) 입출고비·배송비 파일을 읽어 옵션ID별 합산 결과를 반환합니다.
/// 파일 원본 금액은 VAT 별도이므로 로드 시 × 1.1하여 VAT포함 금액으로 변환합니다.
/// </summary>
public static class CfsFeeLoader
{
    /// <summary>열린 패키지가 CFS 파일인지 시트명으로 판별합니다.</summary>
    public static bool IsCfsFile(ExcelPackage package, GrowthCfsFeeConfig cfg)
        => package.Workbook.Worksheets[cfg.HandlingSheetName] != null
        || package.Workbook.Worksheets[cfg.ShippingSheetName] != null;

    /// <summary>
    /// 여러 CFS 파일을 로드하고 옵션ID별로 입출고비·배송비를 합산합니다.
    /// 같은 옵션ID가 여러 파일에 걸쳐 나타나면 합산됩니다(주차별 파일 통합).
    /// 암호화된 파일은 건너뜁니다.
    /// </summary>
    public static CfsFeeResult LoadAndMerge(IEnumerable<string> filePaths, GrowthCfsFeeConfig cfg)
    {
        var result = new CfsFeeResult();
        foreach (var path in filePaths)
        {
            try
            {
                using var pkg = ExcelFileOpener.Open(path);
                if (!IsCfsFile(pkg, cfg)) continue;
                result.CfsFileCount++;
                AccumulatePackage(pkg, cfg, result);
            }
            catch (EncryptedExcelFileException)
            {
                result.Warnings.Add($"{Path.GetFileName(path)}: 암호화 파일 — 건너뜀");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        result.HandlingTotal = result.HandlingByOptionId.Values.Sum();
        result.ShippingTotal = result.ShippingByOptionId.Values.Sum();
        return result;
    }

    private static void AccumulatePackage(ExcelPackage pkg, GrowthCfsFeeConfig cfg, CfsFeeResult result)
    {
        AccumulateSheet(pkg, cfg.HandlingSheetName, cfg.HandlingHeaderRow,
            cfg.CfsOptionIdHeader, cfg.HandlingFeeHeader, result.HandlingByOptionId);
        AccumulateSheet(pkg, cfg.ShippingSheetName, cfg.ShippingHeaderRow,
            cfg.CfsOptionIdHeader, cfg.ShippingFeeHeader, result.ShippingByOptionId);
    }

    private static void AccumulateSheet(
        ExcelPackage pkg, string sheetName, int headerRow,
        string optionIdHeader, string feeHeader,
        Dictionary<string, decimal> map)
    {
        var sheet = pkg.Workbook.Worksheets[sheetName];
        if (sheet?.Dimension == null) return;

        var hdrMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int col = 1; col <= sheet.Dimension.End.Column; col++)
        {
            var h = sheet.Cells[headerRow, col].Value?.ToString();
            if (!string.IsNullOrEmpty(h) && !hdrMap.ContainsKey(h)) hdrMap[h] = col;
        }
        if (!hdrMap.TryGetValue(optionIdHeader, out var keyCol) ||
            !hdrMap.TryGetValue(feeHeader, out var valCol)) return;

        for (int row = headerRow + 1; row <= sheet.Dimension.End.Row; row++)
        {
            var key = sheet.Cells[row, keyCol].Value?.ToString();
            if (string.IsNullOrWhiteSpace(key)) continue;

            var rawText = sheet.Cells[row, valCol].Value?.ToString()?.Replace(",", "");
            if (!decimal.TryParse(rawText, out var rawVal)) continue;

            var vatIncluded = rawVal * 1.1m;  // VAT 별도 → VAT포함
            map[key] = map.TryGetValue(key, out var existing) ? existing + vatIncluded : vatIncluded;
        }
    }
}

public class CfsFeeResult
{
    public int CfsFileCount { get; set; }
    public Dictionary<string, decimal> HandlingByOptionId { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, decimal> ShippingByOptionId { get; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal HandlingTotal { get; set; }
    public decimal ShippingTotal { get; set; }
    public List<string> Warnings { get; } = new();
}
