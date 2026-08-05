namespace MiniERP2.Utils;

/// <summary>
/// FbaOrderForm 저장 버튼을 눌렀을 때의 검증 규칙을 폼과 분리해 단위 테스트할 수 있게 한다
/// (기획서 §12 테스트 필수 항목 4/6). BoxSeq=0은 아직 박스에 배정되지 않은 "미배정 품목 풀"을
/// 뜻하며, 저장 시점에는 반드시 실제 박스(BoxSeq&gt;=1)로 옮겨져 있어야 한다. 의도적으로
/// "1단수량(QtyPerLayer)의 배수인지"는 검사하지
/// 않는다 — §3.4에서 "1단 미만·비배수 허용"이라고 명시했기 때문에, 이 검증기가 그런 행을 걸러내지
/// 않는다는 사실 자체가 회귀 테스트 대상이다.
/// </summary>
public static class FbaOrderSaveValidator
{
    public readonly record struct Row(int BoxSeq, bool IsPlaceholder, int Qty);

    public readonly record struct ValidationResult(bool IsValid, string? ErrorMessage)
    {
        public static readonly ValidationResult Valid = new(true, null);
    }

    public static ValidationResult Validate(IReadOnlyCollection<Row> rows)
    {
        if (rows.Count == 0)
        {
            return new ValidationResult(false, "박스를 하나 이상 추가하세요.");
        }

        if (rows.Any(r => r.BoxSeq == 0))
        {
            return new ValidationResult(false, "미배정 품목이 있습니다. 박스에 담은 뒤 저장하세요.");
        }

        var emptyBoxes = rows.Where(r => r.IsPlaceholder).Select(r => r.BoxSeq).Distinct().OrderBy(x => x).ToList();
        if (emptyBoxes.Count > 0)
        {
            return new ValidationResult(false, $"박스 {string.Join(", ", emptyBoxes)}에 담긴 품목이 없습니다. CSKU를 추가하거나 박스를 삭제하세요.");
        }

        if (rows.Any(r => r.Qty < 1))
        {
            return new ValidationResult(false, "수량이 1 미만인 행이 있습니다.");
        }

        return ValidationResult.Valid;
    }
}
