using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Services;

/// <summary>
/// 월별 마감 자동화 파이프라인.
///
/// 흐름:
///   1. ScanFolder()  — 폴더 스캔 → ScannedFile 목록 (채널 자동 탐지)
///   2. 사용자가 미탐지 파일에 채널 수동 지정 (UI)
///   3. ProcessAsync() — 각 파일 로드 → 이익 계산 → 미매핑 수집 → DB 저장
///   4. 사용자가 UnmappedQueueForm에서 매핑
///   5. RecalcChannelAsync() — 매핑 후 해당 채널 재계산 → ProfitFactTable 저장
/// </summary>
public class ClosingOrchestrator
{
    private readonly ClosingRunRepository _runRepo = new();
    private readonly ProfitFactRepository _factRepo = new();
    private readonly ChannelConfigService _configService = new();
    private readonly MappingRepository _mappingRepo = new();
    private readonly ItemRepository _itemRepo = new();
    private readonly ChannelSkuRepository _cskuRepo = new();

    private static readonly string[] SupportedExtensions = [".xlsx", ".xls", ".csv"];

    // ─── 1. 폴더 스캔 ────────────────────────────────────────────────────────

    /// <summary>
    /// 폴더 내 지원 파일들을 스캔해 채널을 자동 탐지한다.
    /// _META 시트가 있으면 우선 사용, 없으면 ChannelConfig 헤더 시그니처로 시도.
    /// </summary>
    public List<ScannedFile> ScanFolder(string folderPath)
    {
        var channelConfigs = _configService.Load();
        var result = new List<ScannedFile>();

        var files = Directory.GetFiles(folderPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            var scanned = new ScannedFile { FilePath = file };

            // _META 시트 우선 탐지
            var meta = MetaSheetHelper.TryRead(file);
            if (meta != null)
            {
                scanned.Meta = meta;
                scanned.DetectedChannelCode = meta.ChannelCode;
                result.Add(scanned);
                continue;
            }

            // 헤더 시그니처 탐지: 각 채널 설정의 SettlementFieldMappings에서 헤더명을 읽어 비교
            scanned.DetectedChannelCode = TryDetectByHeaders(file, channelConfigs);
            result.Add(scanned);
        }

        return result;
    }

    private static string? TryDetectByHeaders(string filePath, List<ChannelConfig> configs)
    {
        try
        {
            using var package = ExcelFileOpener.Open(filePath);
            var sheet = package.Workbook.Worksheets.FirstOrDefault();
            if (sheet == null) return null;

            // 처음 5행을 합쳐 헤더 후보 집합 만들기
            var headerCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int row = 1; row <= Math.Min(5, sheet.Dimension?.End.Row ?? 0); row++)
            {
                for (int col = 1; col <= Math.Min(30, sheet.Dimension?.End.Column ?? 0); col++)
                {
                    var text = sheet.Cells[row, col].Text?.Trim();
                    if (!string.IsNullOrEmpty(text)) headerCandidates.Add(text);
                }
            }

            if (headerCandidates.Count == 0) return null;

            // 각 채널 설정의 SettlementFieldMappings 헤더명과 대조: 2개 이상 일치 시 채택
            ChannelConfig? bestMatch = null;
            int bestScore = 1;

            foreach (var cfg in configs)
            {
                var mappedHeaders = cfg.SettlementFieldMappings.Values
                    .Select(m => m.Column)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                if (mappedHeaders.Count == 0) continue;

                var score = mappedHeaders.Count(h => headerCandidates.Contains(h!));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = cfg;
                }
            }

            return bestMatch?.ChannelCode;
        }
        catch
        {
            return null;
        }
    }

    // ─── 3. 처리 ─────────────────────────────────────────────────────────────

    public record ProcessProgress(string FileName, string ChannelName, string Message, bool IsError = false);

    /// <summary>
    /// 지정된 run에 속한 staged 파일들을 순서대로 처리한다.
    /// 각 파일 처리 전후로 progress 콜백을 호출한다.
    /// </summary>
    public async Task ProcessRunAsync(
        long runId,
        List<ClosingStagedFile> files,
        string period,
        IProgress<ProcessProgress>? progress = null,
        CancellationToken ct = default)
    {
        foreach (var staged in files)
        {
            ct.ThrowIfCancellationRequested();

            if (!staged.IsChannelAssigned)
            {
                staged.Status = "skipped";
                _runRepo.UpdateStagedFile(staged);
                progress?.Report(new(staged.FileName, "", "채널 미지정 — 건너뜀", IsError: false));
                continue;
            }

            progress?.Report(new(staged.FileName, staged.ChannelName, "처리 중..."));

            try
            {
                var result = await AnalyzeFileAsync(staged, ct);

                staged.RowCount = result.Rows.Count;
                staged.UnmappedCount = result.Unmapped.Count;
                staged.Status = result.ErrorMessage == null ? "processed" : "error";
                staged.ErrorMessage = result.ErrorMessage;
                _runRepo.UpdateStagedFile(staged);

                if (result.ErrorMessage == null)
                {
                    // 미매핑 저장
                    if (result.Unmapped.Count > 0)
                        _runRepo.ReplaceUnmapped(runId, staged.ChannelCode, result.Unmapped);

                    // 이익 집계 → ProfitFactTable 저장
                    await SaveProfitFactsAsync(period, staged.ChannelCode, staged.ChannelName, result.Rows);

                    progress?.Report(new(staged.FileName, staged.ChannelName,
                        $"완료 — {result.Rows.Count}행, 미매핑 {result.Unmapped.Count}건"));
                }
                else
                {
                    progress?.Report(new(staged.FileName, staged.ChannelName,
                        $"오류: {result.ErrorMessage}", IsError: true));
                }
            }
            catch (Exception ex)
            {
                staged.Status = "error";
                staged.ErrorMessage = ex.Message;
                _runRepo.UpdateStagedFile(staged);
                progress?.Report(new(staged.FileName, staged.ChannelName, $"오류: {ex.Message}", IsError: true));
            }
        }

        _runRepo.UpdateRunStatus(runId, "draft");
    }

    private async Task<ChannelAnalysisResult> AnalyzeFileAsync(ClosingStagedFile staged, CancellationToken ct)
    {
        var channelConfigs = _configService.Load();
        var cfg = channelConfigs.FirstOrDefault(c => c.ChannelCode == staged.ChannelCode);
        if (cfg == null)
            return new ChannelAnalysisResult { ChannelCode = staged.ChannelCode, ErrorMessage = $"채널 설정을 찾을 수 없음: {staged.ChannelCode}" };

        var skuMapper = new SkuMapper(_mappingRepo, staged.ChannelCode, _cskuRepo);
        var loader = new SettlementLoader();

        var rows = await loader.LoadFromFileAsync(skuMapper, _itemRepo, cfg, staged.OriginalPath, channelSkuRepository: _cskuRepo);

        ct.ThrowIfCancellationRequested();

        // 미매핑 항목 추출: "매핑 실패" / "매핑 키 없음" 상태 행을 집계. 발주서에 매출액(Revenue)이
        // 매핑된 채널은 SkuMapper가 4필드(상품명+옵션명+수량+매출액) 1:1 매칭도 지원하므로, 같은
        // 상품명+옵션명이라도 가격이 다른 옵션이 한 미매핑 행으로 뭉치지 않도록 4필드로 집계한다
        // (매핑시스템 통합개편 기획서 §5/§10 — 판단 기준은 OrderFieldMappings에 Revenue 매핑 여부).
        var priceMapped = cfg.OrderFieldMappings.ContainsKey(StdField.Revenue);
        var unmapped = rows
            .Where(r => r.Status != null && (r.Status.Contains("매핑 실패") || r.Status.Contains("매핑 키 없음")))
            .GroupBy(r => priceMapped
                ? $"{r.ProductName ?? ""}|{r.OptionName ?? ""}|{r.Qty}|{r.Revenue}"
                : $"{r.ProductName ?? ""}|{r.OptionName ?? ""}")
            .Select(g => new ClosingUnmappedItem
            {
                RunId = staged.RunId,
                ChannelCode = staged.ChannelCode,
                ChannelName = cfg.ChannelName,
                SourceKey = g.Key,
                OccurrenceCount = g.Count(),
                SampleAmount = g.Max(r => r.Settlement),
                Quantity = priceMapped ? g.First().Qty : null,
                SampleRevenue = priceMapped ? g.Max(r => r.Revenue) : null,
            })
            .OrderByDescending(u => u.OccurrenceCount)
            .ToList();

        return new ChannelAnalysisResult
        {
            ChannelCode = staged.ChannelCode,
            ChannelName = cfg.ChannelName,
            Rows = rows,
            Unmapped = unmapped,
        };
    }

    // ─── 5. 매핑 후 재계산 ───────────────────────────────────────────────────

    /// <summary>
    /// 특정 채널의 파일을 재처리해 ProfitFactTable을 갱신한다.
    /// 미매핑 매핑 완료 후 호출.
    /// </summary>
    public async Task RecalcChannelAsync(long runId, string channelCode, string period, IProgress<ProcessProgress>? progress = null)
    {
        var files = _runRepo.GetStagedFiles(runId)
            .Where(f => f.ChannelCode == channelCode && f.Status == "processed")
            .ToList();

        foreach (var staged in files)
        {
            try
            {
                var result = await AnalyzeFileAsync(staged, CancellationToken.None);
                staged.RowCount = result.Rows.Count;
                staged.UnmappedCount = result.Unmapped.Count;
                _runRepo.UpdateStagedFile(staged);

                if (result.Unmapped.Count > 0)
                    _runRepo.ReplaceUnmapped(runId, channelCode, result.Unmapped);
                else
                    _runRepo.ReplaceUnmapped(runId, channelCode, []);

                await SaveProfitFactsAsync(period, channelCode, result.ChannelName, result.Rows);
                progress?.Report(new(staged.FileName, result.ChannelName, $"재계산 완료 — {result.Rows.Count}행"));
            }
            catch (Exception ex)
            {
                progress?.Report(new(staged.FileName, channelCode, $"재계산 오류: {ex.Message}", IsError: true));
            }
        }
    }

    // ─── 이익 집계 저장 ──────────────────────────────────────────────────────

    private async Task SaveProfitFactsAsync(string period, string channelCode, string channelName, List<SettlementData> rows)
    {
        await Task.Run(() =>
        {
            var channelConfigs = _configService.Load();
            var cfg = channelConfigs.FirstOrDefault(c => c.ChannelCode == channelCode);
            var channelType = cfg?.ChannelType;
            var exchangeRate = cfg?.ExchangeRate ?? 1m;

            var facts = rows
                .GroupBy(r => ResolveGroup(r, channelType))
                .Where(g => g.Key != "(합계)")
                .Select(g => new ProfitFactRow
                {
                    ProductGroup = g.Key,
                    Qty = g.Sum(r => r.Qty),
                    Revenue = ProfitCalculator.ToReportCurrency(channelType, g.Sum(r => r.Revenue), exchangeRate),
                    GrossProfit = ProfitCalculator.ToReportCurrency(channelType, g.Sum(r => r.Profit), exchangeRate),
                })
                .ToList();

            _factRepo.SaveProfitFacts(period, channelCode, channelName, facts);
        });
    }

    private static string ResolveGroup(SettlementData row, ChannelType? channelType)
    {
        if (channelType == ChannelType.AmazonUs || channelType == ChannelType.AmazonJp)
            return row.ProductGroup ?? row.Msku ?? "(미지정)";

        return row.ProductGroup ?? "(미지정)";
    }
}
