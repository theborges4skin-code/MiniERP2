using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>엑셀에서 읽은 CSKU 수정 한 줄(현재 DB와 비교하기 전 원본). 열 구성은
/// ChannelCskuForm의 [엑셀로 내보내기]와 동일해야 왕복 편집이 성립한다. OriginalCskuCode는
/// "CSKU 코드(변경 전)" 열(수정 금지, 매칭 전용 식별자)이고 NewCskuCode는 사용자가 실제로
/// 편집하는 "CSKU 코드" 열이다 — 두 값이 다르면 코드 자체를 바꾸는(이름변경) 행이다.</summary>
public record ChannelCskuImportRow(
    string OriginalCskuCode, string NewCskuCode, string Msku, string? InvoiceDisplayName,
    decimal SupplyPrice, string Unit, string? Packing, string? Note);

/// <summary>기존 값과 하나 이상 달라 반영 여부를 선택해야 하는 한 줄.</summary>
public class ChannelCskuUpdateRow
{
    public required ChannelSkuModel Existing { get; init; }
    public string NewCskuCode { get; init; } = string.Empty;
    public string NewMsku { get; init; } = string.Empty;
    public string? NewInvoiceDisplayName { get; init; }
    public decimal NewSupplyPrice { get; init; }
    public string NewUnit { get; init; } = "kg";
    public string? NewPacking { get; init; }
    public string? NewNote { get; init; }

    public bool CodeChanged => !string.Equals(Existing.CskuCode, NewCskuCode, StringComparison.Ordinal);
    public bool MskuChanged => !string.Equals(Existing.Msku, NewMsku, StringComparison.Ordinal);
    public bool InvoiceDisplayNameChanged => !string.Equals(Existing.InvoiceDisplayName ?? string.Empty, NewInvoiceDisplayName ?? string.Empty, StringComparison.Ordinal);
    public bool SupplyPriceChanged => Existing.SupplyPrice != NewSupplyPrice;
    public bool UnitChanged => !string.Equals(Existing.Unit, NewUnit, StringComparison.Ordinal);
    public bool PackingChanged => !string.Equals(Existing.Packing ?? string.Empty, NewPacking ?? string.Empty, StringComparison.Ordinal);
    public bool NoteChanged => !string.Equals(Existing.Note ?? string.Empty, NewNote ?? string.Empty, StringComparison.Ordinal);

    public bool HasAnyChange => CodeChanged || MskuChanged || InvoiceDisplayNameChanged || SupplyPriceChanged || UnitChanged || PackingChanged || NoteChanged;

    /// <summary>선택된 행을 그대로 ChannelSkuRepository.Upsert(코드 불변)나 RenameCsku(코드 변경)에
    /// 넘길 수 있는 모델로 변환한다. CostPriceOverride/UpdatedAt처럼 이 가져오기가 다루지 않는
    /// 필드는 기존 값을 그대로 보존한다.</summary>
    public ChannelSkuModel ToUpdatedModel() => new()
    {
        ChannelCode = Existing.ChannelCode,
        CskuCode = NewCskuCode,
        Msku = NewMsku,
        SupplyPrice = NewSupplyPrice,
        InvoiceDisplayName = NewInvoiceDisplayName,
        Note = NewNote,
        Unit = NewUnit,
        Packing = NewPacking,
        CostPriceOverride = Existing.CostPriceOverride,
        UpdatedAt = Existing.UpdatedAt,
    };
}

public class ChannelCskuBulkUpdatePlan
{
    public List<ChannelCskuUpdateRow> Changed { get; } = new();
    public int UnchangedCount { get; set; }
    public int NotFoundCount { get; set; }
    public int DuplicateCskuCount { get; set; }
    public int InvalidMskuCount { get; set; }
    public int CodeConflictCount { get; set; }
}

/// <summary>
/// 엑셀로 다시 가져온 CSKU 목록과 현재 DB(선택된 거래처의 CSKU)를 비교해, 화면에 검토용으로
/// 보여줄 변경 목록을 만든다. "CSKU 코드(변경 전)" 열로 행을 식별하므로 "CSKU 코드" 열 자체를
/// 고쳐도(이름변경) 정상적으로 매칭된다. 새 CSKU 등록은 이 흐름의 대상이 아니다(원본 코드가 현재
/// 거래처에 없으면 NotFoundCount로 세고 건너뛴다 — 신규 등록은 [CSKU 추가] 전용 흐름을 쓴다).
/// </summary>
public static class ChannelCskuBulkUpdatePlanner
{
    public static ChannelCskuBulkUpdatePlan Build(
        IEnumerable<ChannelCskuImportRow> imported,
        IReadOnlyDictionary<string, ChannelSkuModel> existingByCskuCode,
        IReadOnlyDictionary<string, ItemModel> masterBySku)
    {
        var plan = new ChannelCskuBulkUpdatePlan();
        var seenOriginalCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedNewCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in imported)
        {
            if (string.IsNullOrWhiteSpace(row.OriginalCskuCode)) continue;

            // 엑셀 안에서 같은(변경 전) CSKU 코드가 여러 번 나오면 첫 값만 채택하고 나머지는 건너뜀
            if (!seenOriginalCodes.Add(row.OriginalCskuCode))
            {
                plan.DuplicateCskuCount++;
                continue;
            }

            if (!existingByCskuCode.TryGetValue(row.OriginalCskuCode, out var existing))
            {
                plan.NotFoundCount++;
                continue;
            }

            var newCskuCode = string.IsNullOrWhiteSpace(row.NewCskuCode) ? row.OriginalCskuCode : row.NewCskuCode;
            var codeChanged = !string.Equals(existing.CskuCode, newCskuCode, StringComparison.Ordinal);

            // 새 코드가 이미 다른(이 파일에서 함께 이름변경되지 않는) 기존 CSKU가 쓰고 있거나,
            // 이 파일 안에서 다른 행과 같은 새 코드로 겹치면 반영할 수 없다(기본키 충돌).
            // 한 파일 안에서 A→B, B→C처럼 코드를 순환/연쇄로 맞바꾸는 경우는 지원하지 않는다
            // (단일행 [마스터SKU 지정/변경]과 동일한 제약 — RenameCsku가 새 코드 존재 시 예외를 던짐).
            var codeOwnedByOther = codeChanged && existingByCskuCode.TryGetValue(newCskuCode, out var codeOwner)
                && !string.Equals(codeOwner.CskuCode, existing.CskuCode, StringComparison.Ordinal);
            if (codeOwnedByOther || !claimedNewCodes.Add(newCskuCode))
            {
                plan.CodeConflictCount++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Msku) || !masterBySku.ContainsKey(row.Msku))
            {
                plan.InvalidMskuCount++;
                continue;
            }

            var candidate = new ChannelCskuUpdateRow
            {
                Existing = existing,
                NewCskuCode = newCskuCode,
                NewMsku = row.Msku,
                NewInvoiceDisplayName = row.InvoiceDisplayName,
                NewSupplyPrice = row.SupplyPrice,
                NewUnit = string.IsNullOrWhiteSpace(row.Unit) ? "kg" : row.Unit,
                NewPacking = row.Packing,
                NewNote = row.Note,
            };

            if (!candidate.HasAnyChange)
            {
                plan.UnchangedCount++;
                continue;
            }

            plan.Changed.Add(candidate);
        }

        return plan;
    }
}
