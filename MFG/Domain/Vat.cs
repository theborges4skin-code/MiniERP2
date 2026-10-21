namespace MiniERP2.MFG.Domain;

/// <summary>
/// VAT 환산. 저장은 항상 VAT포함, 화면은 보기 모드에 따라 환산한다(docs/MFG_PLAN.md §3-1).
/// 면세 항목은 보기 모드와 무관하게 같은 금액이다.
/// </summary>
public static class Vat
{
    public const double Rate = 0.1;

    /// <summary>저장값(VAT포함) → 화면값.</summary>
    public static double ToView(double storedIncl, bool showIncluded, bool taxFree = false) =>
        showIncluded || taxFree ? storedIncl : storedIncl / (1 + Rate);

    /// <summary>화면에서 입력한 값 → 저장값(VAT포함).</summary>
    public static double FromView(double viewValue, bool showIncluded, bool taxFree = false) =>
        showIncluded || taxFree ? viewValue : viewValue * (1 + Rate);

    /// <summary>엑셀(VAT별도) 값 → 저장값(VAT포함). 반올림하지 않는다(VAT별도 보기에서 원래 숫자가 그대로 보이도록).</summary>
    public static double FromExclusive(double exclusive, bool taxFree = false) =>
        taxFree ? exclusive : exclusive * (1 + Rate);

    public static string ModeLabel(bool showIncluded) => showIncluded ? "VAT포함" : "VAT별도";
}
