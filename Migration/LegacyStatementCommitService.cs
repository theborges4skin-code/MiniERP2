using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Migration;

public class LegacyStatementCommitResult
{
    public int NewPartiesCreated { get; set; }
    public int ExistingPartiesReused { get; set; }
    public int StatementsSaved { get; set; }
    public int StatementsSkipped { get; set; }
}

/// <summary>
/// 파싱된 거래명세표 시트를 실제 DB에 반영한다(거래명세표_DB이식_개발스펙.md §2.1 정책).
/// 거래처 식별: 등록번호 있으면 DocPartyTable 전체(활성 포함)에서 매칭·재사용, 없으면(익명 거래처)
/// 이름 기반 병합을 시도하지 않고 매 시트마다 개별 비활성 레코드로 생성한다.
/// </summary>
public class LegacyStatementCommitService
{
    private readonly DocPartyRepository _partyRepo;
    private readonly DocStatementRepository _statementRepo;

    public LegacyStatementCommitService(DocPartyRepository? partyRepo = null, DocStatementRepository? statementRepo = null)
    {
        _partyRepo = partyRepo ?? new DocPartyRepository();
        _statementRepo = statementRepo ?? new DocStatementRepository();
    }

    public LegacyStatementCommitResult Commit(IEnumerable<ParsedStatementSheet> sheets)
    {
        var result = new LegacyStatementCommitResult();

        foreach (var sheet in sheets)
        {
            if (sheet.Lines.Count == 0)
            {
                result.StatementsSkipped++;
                continue;
            }

            int? existingStatementPartyId = _statementRepo.FindPartyIdBySource(sheet.SourceFileName, sheet.SourceSheetName);
            int partyId = ResolveOrCreateParty(sheet.Buyer, existingStatementPartyId, result);
            var statement = BuildStatement(sheet, partyId);
            _statementRepo.Upsert(statement);
            result.StatementsSaved++;
        }

        return result;
    }

    private int ResolveOrCreateParty(ParsedPartyInfo? buyer, int? existingStatementPartyId, LegacyStatementCommitResult result)
    {
        string regNo = buyer?.RegNo ?? "";
        if (!string.IsNullOrWhiteSpace(regNo))
        {
            var existing = _partyRepo.FindByRegNo(regNo);
            if (existing != null)
            {
                result.ExistingPartiesReused++;
                return existing.Id;
            }
        }
        else if (existingStatementPartyId.HasValue)
        {
            // 등록번호 없는 익명 거래처를 같은 (파일명,시트명)으로 재커밋하는 경우 — 서로 다른 시트끼리는
            // 여전히 병합하지 않지만(§2.1), 같은 발행건의 재실행이 매번 고아 거래처를 새로 만들면 안 된다.
            result.ExistingPartiesReused++;
            return existingStatementPartyId.Value;
        }

        var party = new DocParty
        {
            RegNo = regNo,
            CompanyName = buyer?.CompanyName ?? "",
            CeoName = buyer?.CeoName ?? "",
            Address = buyer?.Address ?? "",
            BizType = buyer?.BizType ?? "",
            BizItem = buyer?.BizItem ?? "",
            ProfileName = !string.IsNullOrWhiteSpace(buyer?.CompanyName) ? buyer!.CompanyName : "(식별불가)",
        };
        _partyRepo.Save(party); // ChannelCode가 없으므로 IsActive는 자동으로 false(비활성)로 저장된다.
        result.NewPartiesCreated++;
        return party.Id;
    }

    private static DocStatement BuildStatement(ParsedStatementSheet sheet, int partyId)
    {
        var statement = new DocStatement
        {
            PartyId = partyId,
            IssueDate = sheet.IssueDate,
            IssueYearMonth = sheet.IssueDate?.ToString("yyyy-MM") ?? "",
            TotalSupply = sheet.TotalsRowSupply ?? sheet.Lines.Sum(l => l.SupplyAmount),
            TotalTax = sheet.TotalsRowTax ?? sheet.Lines.Sum(l => l.Tax),
            TotalAmount = sheet.TotalsRowTotal ?? sheet.Lines.Sum(l => l.Total),
            TotalQty = sheet.TotalsRowQty ?? sheet.Lines.Sum(l => l.Qty),
            TemplateSignature = sheet.TemplateSignature,
            StatusFlags = string.Join(",", sheet.Flags),
            SourceFileName = sheet.SourceFileName,
            SourceSheetName = sheet.SourceSheetName,
        };

        foreach (var line in sheet.Lines)
        {
            DateTime? lineDate = null;
            if (line.Year > 0 && line.Month > 0 && line.Day > 0)
            {
                try { lineDate = new DateTime(2000 + line.Year, line.Month, line.Day); }
                catch (ArgumentOutOfRangeException) { /* 잘못된 날짜 — 헤더 발행일만 사용 */ }
            }

            statement.Lines.Add(new DocStatementLine
            {
                LineDate = lineDate,
                ItemName = line.ItemName,
                Spec = line.Spec,
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                UnitPriceVatIncluded = line.UnitPriceVatIncluded,
                SupplyAmount = line.SupplyAmount,
                Tax = line.Tax,
                Total = line.Total,
                Note = line.Note,
            });
        }

        return statement;
    }
}
