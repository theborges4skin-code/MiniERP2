using System.Text.Json;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 채널 일괄등록(엑셀) 기획서 §4.3/§4.4. 엑셀 파일을 읽어 검증하고, 커밋 시 그대로 저장할 최종
/// <see cref="SalesChannel"/>/<see cref="ChannelConfig"/>/<see cref="DocParty"/> 객체까지 만들어
/// <see cref="ChannelImportChannelRow"/>에 담아 반환한다(미리보기와 커밋이 같은 값을 쓰도록).
/// </summary>
public class ChannelBulkImportLoader
{
    public ChannelBulkImportResult Load(string filePath, List<SalesChannel> existingChannels, List<ChannelConfig> existingConfigs, List<DocParty> existingParties)
    {
        var result = new ChannelBulkImportResult();
        using var package = ExcelFileOpener.Open(filePath);

        ValidateSchemaVersion(package, result);

        var channelSheet = package.Workbook.Worksheets[ChannelBulkImportSchema.ChannelSheet];
        if (channelSheet == null)
        {
            result.FileErrors.Add($"필수 시트 '{ChannelBulkImportSchema.ChannelSheet}'가 없습니다.");
            return result;
        }

        result.ChannelRows.AddRange(ParseChannelSheet(channelSheet));

        var orderSheet = package.Workbook.Worksheets[ChannelBulkImportSchema.OrderMappingSheet];
        if (orderSheet != null) result.MappingRows.AddRange(ParseMappingSheet(orderSheet, isSettlement: false));

        var settlementSheet = package.Workbook.Worksheets[ChannelBulkImportSchema.SettlementMappingSheet];
        if (settlementSheet != null) result.MappingRows.AddRange(ParseMappingSheet(settlementSheet, isSettlement: true));

        var partySheet = package.Workbook.Worksheets[ChannelBulkImportSchema.PartySheet];
        if (partySheet != null) result.PartyRows.AddRange(ParsePartySheet(partySheet));

        ValidateDuplicateChannelNames(result);
        ResolveChannelCodes(result, existingChannels);
        ResolveCopySources(result);
        ValidateChannelTypeRequired(result);
        ValidateMappingJoins(result, existingChannels, existingConfigs);
        ValidatePartyJoins(result, existingChannels);
        BuildFinalObjectsAndStatuses(result, existingChannels, existingConfigs, existingParties);

        return result;
    }

    // ── 파일 단위 검증 ───────────────────────────────────────────────────────

    private static void ValidateSchemaVersion(ExcelPackage package, ChannelBulkImportResult result)
    {
        var meta = package.Workbook.Worksheets[ChannelBulkImportSchema.MetaSheet];
        if (meta == null)
        {
            result.FileErrors.Add("이 파일은 채널 일괄등록 양식이 아닙니다(_META 시트 없음). '엑셀 다운로드'로 받은 양식을 사용하세요.");
            return;
        }

        var versionText = meta.Cells[1, 2].Text;
        if (!int.TryParse(versionText, out var version) || version != ChannelBulkImportSchema.SchemaVersion)
        {
            result.FileErrors.Add($"양식 버전이 맞지 않습니다(파일: '{versionText}', 필요: {ChannelBulkImportSchema.SchemaVersion}). 최신 양식을 다시 받아 작성하세요.");
        }
        result.SchemaVersion = version;
    }

    private static void ValidateDuplicateChannelNames(ChannelBulkImportResult result)
    {
        var duplicateGroups = result.ChannelRows
            .Where(r => !string.IsNullOrWhiteSpace(r.ChannelName))
            .GroupBy(r => r.ChannelName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            result.FileErrors.Add($"채널명이 파일 내에서 중복되었습니다: '{group.Key}' (행 {string.Join(", ", group.Select(r => r.RowNumber))})");
            foreach (var row in group) row.Errors.Add("채널명이 파일 내에서 중복되었습니다.");
        }
    }

    // ── 채널 시트 파싱 ───────────────────────────────────────────────────────

    private static List<ChannelImportChannelRow> ParseChannelSheet(ExcelWorksheet sheet)
    {
        var headerMap = BuildHeaderMap(sheet);
        var rows = new List<ChannelImportChannelRow>();
        var lastRow = sheet.Dimension?.End.Row ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            if (IsRowBlank(sheet, r, headerMap)) continue;

            var row = new ChannelImportChannelRow { RowNumber = r };
            row.ChannelCodeInput = GetText(sheet, r, headerMap, "채널코드") ?? "";
            row.ChannelName = GetText(sheet, r, headerMap, "채널명") ?? "";
            row.GroupName = GetText(sheet, r, headerMap, "그룹");
            row.AutoOrderHints = GetText(sheet, r, headerMap, "자동발주채널힌트") ?? "";
            row.CopySourceChannelName = GetText(sheet, r, headerMap, "설정복사원본");

            foreach (var header in new[] { "채널유형", "그룹", "표시순서", "즐겨찾기", "매입", "매출", "환율", "누적발주서", "누적조회일수", "자동발주채널힌트" })
            {
                row.RawCells[header] = GetText(sheet, r, headerMap, header);
            }

            if (string.IsNullOrWhiteSpace(row.ChannelName))
            {
                row.Errors.Add("채널명은 필수입니다.");
            }

            var typeText = row.RawCells["채널유형"];
            if (!string.IsNullOrWhiteSpace(typeText))
            {
                var parsedType = ChannelTypeExtensions.TryParseKoreanLabel(typeText);
                if (parsedType == null)
                    row.Errors.Add($"알 수 없는 채널유형입니다: '{typeText}'");
                else
                    row.ResolvedChannelType = parsedType.Value;
            }

            row.DisplayOrder = ParseIntColumn(sheet, r, headerMap, "표시순서", 0, row.Errors);
            row.CumulativeOrderWindowDays = ParseIntColumn(sheet, r, headerMap, "누적조회일수", 5, row.Errors);
            row.ExchangeRate = ParseDecimalColumn(sheet, r, headerMap, "환율", 1m, row.Errors);

            row.IsFavorite = ChannelBulkImportSchema.ParseYn(GetText(sheet, r, headerMap, "즐겨찾기"), false);
            row.IsPurchase = ChannelBulkImportSchema.ParseYn(GetText(sheet, r, headerMap, "매입"), false);
            row.IsSales = ChannelBulkImportSchema.ParseYn(GetText(sheet, r, headerMap, "매출"), true);
            row.IsCumulativeOrderFile = ChannelBulkImportSchema.ParseYn(GetText(sheet, r, headerMap, "누적발주서"), false);

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// 채널유형은 원칙적으로 필수지만(§4.1), 설정복사원본을 지정한 행은 그 원본의 채널유형을
    /// 그대로 이어받을 수 있으므로 공란을 허용한다(§4.1.1 "공란인 열은 복사본 값 유지").
    /// </summary>
    private static void ValidateChannelTypeRequired(ChannelBulkImportResult result)
    {
        foreach (var row in result.ChannelRows)
        {
            var typeText = row.RawCells.GetValueOrDefault("채널유형");
            if (string.IsNullOrWhiteSpace(typeText) && string.IsNullOrWhiteSpace(row.CopySourceChannelName))
            {
                row.Errors.Add("채널유형은 필수입니다(설정복사원본을 지정하지 않는 한 비워둘 수 없습니다).");
            }
        }
    }

    private static void ResolveChannelCodes(ChannelBulkImportResult result, List<SalesChannel> existingChannels)
    {
        foreach (var row in result.ChannelRows)
        {
            if (string.IsNullOrWhiteSpace(row.ChannelName)) continue;

            var codeInput = row.ChannelCodeInput.Trim();
            if (!string.IsNullOrEmpty(codeInput))
            {
                var existingByCode = existingChannels.FirstOrDefault(c => c.ChannelCode == codeInput);
                if (existingByCode == null)
                {
                    row.Errors.Add($"채널코드 '{codeInput}'가 기존 채널 목록에 없습니다. 신규 채널은 채널코드를 비워두세요.");
                    continue;
                }

                var conflicting = existingChannels.FirstOrDefault(c => c.ChannelName == row.ChannelName && c.ChannelCode != codeInput);
                if (conflicting != null)
                {
                    row.Errors.Add($"채널명 '{row.ChannelName}'은 이미 다른 채널코드('{conflicting.ChannelCode}')에서 사용 중입니다. 모호하여 처리할 수 없습니다.");
                    continue;
                }

                row.ResolvedChannelCode = codeInput;
            }
            else
            {
                var matchedByName = existingChannels.FirstOrDefault(c => c.ChannelName == row.ChannelName);
                if (matchedByName != null)
                {
                    row.ResolvedChannelCode = matchedByName.ChannelCode;
                    row.Warnings.Add($"채널코드 없이 이름만으로 기존 채널 '{matchedByName.ChannelCode} - {matchedByName.ChannelName}'을 찾아 수정합니다. 의도한 채널이 맞는지 확인하세요.");
                }
                // else: 신규 채널 — 코드는 커밋 준비 단계(BuildFinalObjectsAndStatuses)에서 채번한다.
            }
        }
    }

    /// <summary>§4.1.1 설정복사원본 규칙(9/9-1/9-2). 이름만 검증하고, 실제 복사는 커밋 준비 단계에서 한다.</summary>
    private static void ResolveCopySources(ChannelBulkImportResult result)
    {
        foreach (var row in result.ChannelRows)
        {
            var sourceName = row.CopySourceChannelName?.Trim();
            if (string.IsNullOrEmpty(sourceName)) continue;

            if (sourceName == row.ChannelName)
            {
                row.Errors.Add("설정복사원본으로 자기 자신을 지정할 수 없습니다.");
                continue;
            }

            // 기존 채널 여부/미존재 오류(9번)는 실제 원본을 확정하는 커밋 준비 단계(ResolveCopyBaseConfig)에서 함께 처리한다.
            var sourceRowInFile = result.ChannelRows.FirstOrDefault(r => r != row && r.ChannelName == sourceName);
            if (sourceRowInFile != null && !string.IsNullOrWhiteSpace(sourceRowInFile.CopySourceChannelName))
            {
                row.Errors.Add($"설정복사원본 '{sourceName}'은 그 자체로 다른 채널을 복사한 행입니다(연쇄 복사는 지원하지 않습니다).");
            }
        }
    }

    // ── 매핑/거래처 시트 파싱 ────────────────────────────────────────────────

    private static List<ChannelImportMappingRow> ParseMappingSheet(ExcelWorksheet sheet, bool isSettlement)
    {
        var headerMap = BuildHeaderMap(sheet);
        var rows = new List<ChannelImportMappingRow>();
        var lastRow = sheet.Dimension?.End.Row ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            if (IsRowBlank(sheet, r, headerMap)) continue;

            var row = new ChannelImportMappingRow { RowNumber = r, IsSettlement = isSettlement };
            row.ChannelName = GetText(sheet, r, headerMap, "채널명") ?? "";
            row.StdFieldLabel = GetText(sheet, r, headerMap, "표준필드") ?? "";
            row.SheetName = GetText(sheet, r, headerMap, "시트이름");
            row.Column = GetText(sheet, r, headerMap, "열이름");
            row.FixedValue = GetText(sheet, r, headerMap, "고정값");
            row.HeaderRow = ParseIntColumn(sheet, r, headerMap, "헤더행", 1, row.Errors);

            if (string.IsNullOrWhiteSpace(row.ChannelName)) row.Errors.Add("채널명은 필수입니다.");
            if (row.HeaderRow < 1) row.Errors.Add("헤더행은 1 이상이어야 합니다.");

            var field = StdFieldLabels.TryParseLabel(row.StdFieldLabel);
            if (field == null)
                row.Errors.Add($"알 수 없는 표준필드 라벨입니다: '{row.StdFieldLabel}'");
            else
                row.ResolvedField = field;

            rows.Add(row);
        }

        return rows;
    }

    private static List<ChannelImportPartyRow> ParsePartySheet(ExcelWorksheet sheet)
    {
        var headerMap = BuildHeaderMap(sheet);
        var rows = new List<ChannelImportPartyRow>();
        var lastRow = sheet.Dimension?.End.Row ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            if (IsRowBlank(sheet, r, headerMap)) continue;

            var row = new ChannelImportPartyRow
            {
                RowNumber = r,
                ChannelName = GetText(sheet, r, headerMap, "채널명") ?? "",
                RegNo = GetText(sheet, r, headerMap, "등록번호") ?? "",
                CompanyName = GetText(sheet, r, headerMap, "상호") ?? "",
                CeoName = GetText(sheet, r, headerMap, "대표자") ?? "",
                Address = GetText(sheet, r, headerMap, "주소") ?? "",
                BizType = GetText(sheet, r, headerMap, "업태") ?? "",
                BizItem = GetText(sheet, r, headerMap, "종목") ?? "",
                Tel = GetText(sheet, r, headerMap, "전화") ?? "",
                Email = GetText(sheet, r, headerMap, "이메일") ?? "",
            };

            if (string.IsNullOrWhiteSpace(row.ChannelName)) row.Errors.Add("채널명은 필수입니다.");

            rows.Add(row);
        }

        return rows;
    }

    private static void ValidateMappingJoins(ChannelBulkImportResult result, List<SalesChannel> existingChannels, List<ChannelConfig> existingConfigs)
    {
        var namesInFile = result.ChannelRows.Select(r => r.ChannelName).ToHashSet(StringComparer.Ordinal);
        var existingNames = existingChannels.Select(c => c.ChannelName).ToHashSet(StringComparer.Ordinal);

        foreach (var row in result.MappingRows)
        {
            if (string.IsNullOrWhiteSpace(row.ChannelName)) continue;

            if (!namesInFile.Contains(row.ChannelName) && !existingNames.Contains(row.ChannelName))
            {
                row.Errors.Add($"채널명 '{row.ChannelName}'을 '{ChannelBulkImportSchema.ChannelSheet}' 시트나 기존 채널에서 찾을 수 없습니다.");
                continue;
            }

            if (row.IsSettlement && row.ResolvedField.HasValue)
            {
                var channelType = ResolveChannelTypeForMappingRow(row.ChannelName, result, existingChannels, existingConfigs);
                if (channelType.HasValue)
                {
                    var allowed = ChannelFieldSets.ResolveSettlementMappingFields(channelType.Value);
                    if (!allowed.Contains(row.ResolvedField.Value))
                    {
                        row.Warnings.Add($"표준필드 '{row.StdFieldLabel}'은 채널유형 '{channelType.Value.ToKoreanLabel()}'에서 허용되지 않습니다. 이 매핑 행은 반영에서 제외됩니다.");
                    }
                }
            }
        }
    }

    private static ChannelType? ResolveChannelTypeForMappingRow(string channelName, ChannelBulkImportResult result, List<SalesChannel> existingChannels, List<ChannelConfig> existingConfigs)
    {
        var fileRow = result.ChannelRows.FirstOrDefault(r => r.ChannelName == channelName);
        if (fileRow != null) return fileRow.ResolvedChannelType;

        var existingChannel = existingChannels.FirstOrDefault(c => c.ChannelName == channelName);
        var existingConfig = existingChannel != null ? existingConfigs.FirstOrDefault(c => c.ChannelCode == existingChannel.ChannelCode) : null;
        return existingConfig?.ChannelType;
    }

    private static void ValidatePartyJoins(ChannelBulkImportResult result, List<SalesChannel> existingChannels)
    {
        var namesInFile = result.ChannelRows.Select(r => r.ChannelName).ToHashSet(StringComparer.Ordinal);
        var existingNames = existingChannels.Select(c => c.ChannelName).ToHashSet(StringComparer.Ordinal);

        foreach (var row in result.PartyRows)
        {
            if (string.IsNullOrWhiteSpace(row.ChannelName)) continue;
            if (!namesInFile.Contains(row.ChannelName) && !existingNames.Contains(row.ChannelName))
            {
                row.Errors.Add($"채널명 '{row.ChannelName}'을 '{ChannelBulkImportSchema.ChannelSheet}' 시트나 기존 채널에서 찾을 수 없습니다.");
            }
        }
    }

    // ── 최종 객체 조립 + 상태(신규/수정/변경없음/오류) 판정 ─────────────────

    private static void BuildFinalObjectsAndStatuses(ChannelBulkImportResult result, List<SalesChannel> existingChannels, List<ChannelConfig> existingConfigs, List<DocParty> existingParties)
    {
        var reservedCodes = new HashSet<string>(existingChannels.Select(c => c.ChannelCode), StringComparer.OrdinalIgnoreCase);

        // 1단계: 신규 행에 채널코드를 미리 채번해둔다(미리보기에 실제 코드를 보여주기 위해).
        foreach (var row in result.ChannelRows)
        {
            if (row.HasErrors) continue;
            if (string.IsNullOrEmpty(row.ResolvedChannelCode))
            {
                var newCode = ChannelCodeGenerator.GenerateNext(reservedCodes);
                reservedCodes.Add(newCode);
                row.ResolvedChannelCode = newCode;
            }
        }

        foreach (var row in result.ChannelRows)
        {
            if (row.HasErrors)
            {
                row.Status = ChannelImportRowStatus.Error;
                continue;
            }

            var existingChannelForRow = existingChannels.FirstOrDefault(c => c.ChannelCode == row.ResolvedChannelCode);
            var existingConfigForRow = existingConfigs.FirstOrDefault(c => c.ChannelCode == row.ResolvedChannelCode);

            var (baseConfig, copySourceMissingErr) = ResolveCopyBaseConfig(row, result, existingChannels, existingConfigs);
            if (copySourceMissingErr != null)
            {
                row.Errors.Add(copySourceMissingErr);
                row.Status = ChannelImportRowStatus.Error;
                continue;
            }

            var baseChannel = existingChannelForRow != null ? CloneChannel(existingChannelForRow) : new SalesChannel();
            ApplyChannelBasicFields(row, baseChannel, baseConfig);

            baseChannel.ChannelCode = row.ResolvedChannelCode!;
            baseConfig.ChannelCode = row.ResolvedChannelCode!;
            baseConfig.ChannelName = row.ChannelName;

            baseConfig.OrderFieldMappings = ResolveEffectiveMappings(row.ChannelName, false, baseConfig.OrderFieldMappings, result.MappingRows, row.ResolvedChannelType);
            baseConfig.SettlementFieldMappings = ResolveEffectiveMappings(row.ChannelName, true, baseConfig.SettlementFieldMappings, result.MappingRows, row.ResolvedChannelType);

            var partyRow = result.PartyRows.FirstOrDefault(p => p.ChannelName == row.ChannelName && !p.HasErrors);
            DocParty? finalParty = null;
            if (partyRow != null)
            {
                var existingParty = existingParties.FirstOrDefault(p => p.ChannelCode == row.ResolvedChannelCode);
                finalParty = new DocParty
                {
                    Id = existingParty?.Id ?? 0,
                    ProfileName = row.ChannelName,
                    RegNo = partyRow.RegNo,
                    CompanyName = partyRow.CompanyName,
                    CeoName = partyRow.CeoName,
                    Address = partyRow.Address,
                    BizType = partyRow.BizType,
                    BizItem = partyRow.BizItem,
                    Tel = partyRow.Tel,
                    Email = partyRow.Email,
                    ChannelCode = row.ResolvedChannelCode!,
                };
            }

            if (baseConfig.IsCumulativeOrderFile && !baseConfig.OrderFieldMappings.ContainsKey(StdField.OrderDate))
            {
                row.Warnings.Add("누적발주서가 체크되어 있지만 발주서 매핑에 '발주일'이 없습니다. 발주일을 매핑하지 않으면 이 옵션은 동작하지 않습니다.");
            }
            if (!baseConfig.ChannelType.IsMarketplace() && baseConfig.SettlementFieldMappings.Count > 0)
            {
                row.Warnings.Add("채널유형이 마켓플레이스가 아닌데(거래처/B2B 거래처/기타) 정산서 매핑이 설정되어 있습니다.");
            }

            row.FinalChannel = baseChannel;
            row.FinalConfig = baseConfig;
            row.FinalParty = finalParty;
            row.ResolvedChannelType = baseConfig.ChannelType;

            if (existingChannelForRow == null)
            {
                row.Status = ChannelImportRowStatus.New;
            }
            else
            {
                var existingPartyForRow = existingParties.FirstOrDefault(p => p.ChannelCode == row.ResolvedChannelCode);
                row.Status = HasChanges(baseChannel, existingChannelForRow, baseConfig, existingConfigForRow, finalParty, existingPartyForRow)
                    ? ChannelImportRowStatus.Update
                    : ChannelImportRowStatus.Unchanged;
            }
        }
    }

    /// <summary>
    /// §4.1.1: 설정복사원본이 없으면 기존 설정(수정) 또는 빈 설정(신규)을 기반으로 하고,
    /// 있으면 원본 채널의 ChannelConfig를 깊은 복사해 기반으로 삼는다. 원본이 자동발주(표준)
    /// 프리셋이면 배타 플래그가 중복되지 않도록 복사본에서는 리셋한다(규칙 15).
    /// </summary>
    private static (ChannelConfig Config, string? Error) ResolveCopyBaseConfig(
        ChannelImportChannelRow row, ChannelBulkImportResult result, List<SalesChannel> existingChannels, List<ChannelConfig> existingConfigs)
    {
        var sourceName = row.CopySourceChannelName?.Trim();
        if (string.IsNullOrEmpty(sourceName))
        {
            var existingConfigForRow = existingConfigs.FirstOrDefault(c => c.ChannelCode == row.ResolvedChannelCode);
            return (existingConfigForRow != null ? DeepCloneConfig(existingConfigForRow) : new ChannelConfig(), null);
        }

        var sourceExistingChannel = existingChannels.FirstOrDefault(c => c.ChannelName == sourceName);
        if (sourceExistingChannel != null)
        {
            var sourceConfig = existingConfigs.FirstOrDefault(c => c.ChannelCode == sourceExistingChannel.ChannelCode);
            var cloned = DeepCloneConfig(sourceConfig ?? new ChannelConfig());
            if (cloned.IsAutoOrderStandardPreset)
            {
                cloned.IsAutoOrderStandardPreset = false;
                row.Warnings.Add($"복사원본 '{sourceName}' 채널은 자동발주(표준) 프리셋 채널이라, 복사본에는 이 플래그를 복제하지 않고 해제했습니다.");
            }
            return (cloned, null);
        }

        var sourceRowInFile = result.ChannelRows.FirstOrDefault(r => r != row && r.ChannelName == sourceName);
        if (sourceRowInFile != null)
        {
            row.Warnings.Add($"설정복사원본 '{sourceName}'은 이 파일에서 함께 새로 만들어지는 채널이라, 보조소스/광고비 레이아웃 등 복사할 기존 설정이 없습니다.");
            return (new ChannelConfig(), null);
        }

        return (new ChannelConfig(), $"설정복사원본 '{sourceName}'을 찾을 수 없습니다(기존 채널도, 파일 내 다른 행도 아닙니다).");
    }

    /// <summary>
    /// SalesChannel 쪽 열(그룹/표시순서/즐겨찾기/매입/매출)은 복사 여부와 무관하게 항상 이 행의
    /// 값을 그대로 적용한다(기존 단건 등록 흐름도 SalesChannel은 복사하지 않음). ChannelConfig 쪽
    /// 열(채널유형/환율/누적발주서/누적조회일수/자동발주채널힌트)은 §4.1.1에 따라 공란이면 복사본
    /// 값을 유지한다.
    /// </summary>
    private static void ApplyChannelBasicFields(ChannelImportChannelRow row, SalesChannel channel, ChannelConfig config)
    {
        channel.ChannelName = row.ChannelName;
        channel.GroupName = row.GroupName;
        channel.DisplayOrder = row.DisplayOrder;
        channel.IsFavorite = row.IsFavorite;
        channel.IsPurchase = row.IsPurchase;
        channel.IsSales = row.IsSales;

        var isCopyFlow = !string.IsNullOrWhiteSpace(row.CopySourceChannelName);

        if (!isCopyFlow || row.RawCells.GetValueOrDefault("채널유형") != null)
            config.ChannelType = row.ResolvedChannelType;
        if (!isCopyFlow || row.RawCells.GetValueOrDefault("환율") != null)
            config.ExchangeRate = row.ExchangeRate;
        if (!isCopyFlow || row.RawCells.GetValueOrDefault("누적발주서") != null)
            config.IsCumulativeOrderFile = row.IsCumulativeOrderFile;
        if (!isCopyFlow || row.RawCells.GetValueOrDefault("누적조회일수") != null)
            config.CumulativeOrderWindowDays = row.CumulativeOrderWindowDays;
        if (!isCopyFlow || row.RawCells.GetValueOrDefault("자동발주채널힌트") != null)
            config.AutoOrderHints = row.AutoOrderHints;
    }

    private static Dictionary<StdField, FieldMapping> ResolveEffectiveMappings(
        string channelName, bool isSettlement, Dictionary<StdField, FieldMapping> baseDict,
        List<ChannelImportMappingRow> allMappingRows, ChannelType channelType)
    {
        var applicable = allMappingRows
            .Where(m => m.IsSettlement == isSettlement && m.ChannelName == channelName && !m.HasErrors && m.ResolvedField.HasValue)
            .Where(m => !isSettlement || ChannelFieldSets.ResolveSettlementMappingFields(channelType).Contains(m.ResolvedField!.Value))
            .ToList();

        // §4.1.1 "매핑 반영 단위": 시트에 해당 채널의 행이 하나도 없으면 기존/복사본 값을 그대로 둔다.
        if (applicable.Count == 0) return baseDict;

        var replaced = new Dictionary<StdField, FieldMapping>();
        foreach (var m in applicable)
        {
            replaced[m.ResolvedField!.Value] = new FieldMapping
            {
                SheetName = m.SheetName,
                HeaderRow = m.HeaderRow,
                Column = m.Column,
                FixedValue = m.FixedValue,
            };
        }
        return replaced;
    }

    private static bool HasChanges(SalesChannel newChannel, SalesChannel oldChannel, ChannelConfig newConfig, ChannelConfig? oldConfig, DocParty? newParty, DocParty? oldParty)
    {
        if (newChannel.ChannelName != oldChannel.ChannelName) return true;
        if (newChannel.GroupName != oldChannel.GroupName) return true;
        if (newChannel.DisplayOrder != oldChannel.DisplayOrder) return true;
        if (newChannel.IsFavorite != oldChannel.IsFavorite) return true;
        if (newChannel.IsPurchase != oldChannel.IsPurchase) return true;
        if (newChannel.IsSales != oldChannel.IsSales) return true;

        var oc = oldConfig ?? new ChannelConfig();
        if (newConfig.ChannelType != oc.ChannelType) return true;
        if (newConfig.ExchangeRate != oc.ExchangeRate) return true;
        if (newConfig.IsCumulativeOrderFile != oc.IsCumulativeOrderFile) return true;
        if (newConfig.CumulativeOrderWindowDays != oc.CumulativeOrderWindowDays) return true;
        if (newConfig.AutoOrderHints != oc.AutoOrderHints) return true;
        if (!MappingDictEquals(newConfig.OrderFieldMappings, oc.OrderFieldMappings)) return true;
        if (!MappingDictEquals(newConfig.SettlementFieldMappings, oc.SettlementFieldMappings)) return true;

        return !PartyEquals(newParty, oldParty);
    }

    private static bool MappingDictEquals(Dictionary<StdField, FieldMapping> a, Dictionary<StdField, FieldMapping> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (field, mapping) in a)
        {
            if (!b.TryGetValue(field, out var other)) return false;
            if (mapping.SheetName != other.SheetName || mapping.HeaderRow != other.HeaderRow ||
                mapping.Column != other.Column || mapping.FixedValue != other.FixedValue) return false;
        }
        return true;
    }

    /// <summary>파일이 이 채널의 거래처정보를 지정하지 않았으면(newParty == null) 기존 상태를 건드리지 않으므로 "변경 없음"으로 취급한다.</summary>
    private static bool PartyEquals(DocParty? newParty, DocParty? oldParty)
    {
        if (newParty == null) return true;
        if (oldParty == null) return false;
        return newParty.RegNo == oldParty.RegNo && newParty.CompanyName == oldParty.CompanyName &&
               newParty.CeoName == oldParty.CeoName && newParty.Address == oldParty.Address &&
               newParty.BizType == oldParty.BizType && newParty.BizItem == oldParty.BizItem &&
               newParty.Tel == oldParty.Tel && newParty.Email == oldParty.Email;
    }

    private static SalesChannel CloneChannel(SalesChannel source) => new()
    {
        ChannelCode = source.ChannelCode,
        ChannelName = source.ChannelName,
        GroupName = source.GroupName,
        IsFavorite = source.IsFavorite,
        DisplayOrder = source.DisplayOrder,
        LastUsedDate = source.LastUsedDate,
        IsPurchase = source.IsPurchase,
        IsSales = source.IsSales,
    };

    private static ChannelConfig DeepCloneConfig(ChannelConfig source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<ChannelConfig>(json) ?? new ChannelConfig();
    }

    // ── 셀 읽기 헬퍼 ─────────────────────────────────────────────────────────

    private static Dictionary<string, int> BuildHeaderMap(ExcelWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastCol = sheet.Dimension?.End.Column ?? 0;
        for (var col = 1; col <= lastCol; col++)
        {
            var header = sheet.Cells[1, col].Text?.Trim();
            if (!string.IsNullOrEmpty(header) && !map.ContainsKey(header)) map[header] = col;
        }
        return map;
    }

    private static bool IsRowBlank(ExcelWorksheet sheet, int row, Dictionary<string, int> headerMap)
    {
        foreach (var col in headerMap.Values)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Cells[row, col].Text)) return false;
        }
        return true;
    }

    private static string? GetText(ExcelWorksheet sheet, int row, Dictionary<string, int> headerMap, string header)
    {
        if (!headerMap.TryGetValue(header, out var col)) return null;
        var text = sheet.Cells[row, col].Text;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int ParseIntColumn(ExcelWorksheet sheet, int row, Dictionary<string, int> headerMap, string header, int defaultValue, List<string> errors)
    {
        var text = GetText(sheet, row, headerMap, header);
        if (text == null) return defaultValue;
        if (int.TryParse(text, out var value)) return value;
        errors.Add($"'{header}' 값이 숫자가 아닙니다: '{text}'");
        return defaultValue;
    }

    private static decimal ParseDecimalColumn(ExcelWorksheet sheet, int row, Dictionary<string, int> headerMap, string header, decimal defaultValue, List<string> errors)
    {
        var text = GetText(sheet, row, headerMap, header);
        if (text == null) return defaultValue;
        if (decimal.TryParse(text, out var value)) return value;
        errors.Add($"'{header}' 값이 숫자가 아닙니다: '{text}'");
        return defaultValue;
    }
}
