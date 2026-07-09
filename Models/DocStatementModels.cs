namespace MiniERP2.Models;

/// <summary>
/// 레거시 엑셀 거래명세표 마이그레이션으로 적재되는 과거 발행건(이력 데이터).
/// DocsForm이 새 문서를 만들 때 쓰는 <see cref="TradeStatementDoc"/>와는 별개 모델이다 —
/// 저건 "지금 작성 중인 문서"라 합계가 계산 프로퍼티지만, 이건 "이미 발행되어 총액이 고정된
/// 과거 기록"이라 합계를 저장된 값 그대로 보존해야 한다(재계산하면 원본과 달라질 수 있음).
/// </summary>
public class DocStatement
{
    public int Id { get; set; }

    /// <summary>DocPartyTable.Id 참조(거래처_DB이식_개발스펙.md §2.1 — 신규 테이블 대신 DocPartyTable 재사용).</summary>
    public int PartyId { get; set; }

    public DateTime? IssueDate { get; set; }
    public string IssueYearMonth { get; set; } = "";
    public decimal TotalSupply { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalQty { get; set; }
    public decimal CarryoverBalance { get; set; }
    public string ReconcileNote { get; set; } = "";

    /// <summary>스펙 §3.4 템플릿유형 코드(예 "Y-INC", "N-SEP").</summary>
    public string TemplateSignature { get; set; } = "";

    /// <summary>쉼표로 이어붙인 플래그 목록(예 "COPY_SUSPECTED,TOTALS_MISMATCH") — 스펙 §3.6 "차단 아님, 표기만".</summary>
    public string StatusFlags { get; set; } = "";

    public string SourceFileName { get; set; } = "";
    public string SourceSheetName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }

    public List<DocStatementLine> Lines { get; set; } = new();
}

public class DocStatementLine
{
    public int Id { get; set; }
    public int StatementId { get; set; }
    public int RowNo { get; set; }
    public DateTime? LineDate { get; set; }
    public string ItemName { get; set; } = "";
    public string Spec { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public bool UnitPriceVatIncluded { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>DocStatementBrowserForm에서 선택한 발행건 1건을 엑셀로 재현하는 데 필요한 데이터 묶음.</summary>
public record LegacyStatementExportItem(DocStatement Statement, DocParty Supplier, DocParty Buyer, List<DocStatementLine> Lines);
