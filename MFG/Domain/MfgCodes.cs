namespace MiniERP2.MFG.Domain;

/// <summary>
/// MFG 코드 체계(사용자 요청 2026-10-21): 모든 MFG 코드는 "mfg_"로 시작한다.
/// <list type="bullet">
/// <item>원료: master DB(MiniERP2 ItemTable)에 같은 원료를 소분 판매하는 SKU 계열이 있으면 그 기본 SKU를 차용한다.
///   예) master "cocog" = 코코글루코사이드 1L → 원료 코드 "mfg_cocog". 차용할 것이 없으면 "mfg_rm001"식(파일 일괄수정으로 고침).</item>
/// <item>부자재: "mfg_pk001".</item>
/// <item>제조법: "mfg_" + 분류 글자(c/s/h/d) + 번호. 예) "mfg_h14".</item>
/// </list>
/// 완제품(판매 SKU)은 MiniERP2 ItemTable.Sku 그대로 쓴다 — MiniERP2로 원가를 보내는 연결 키이기 때문.
/// </summary>
public static class MfgCodes
{
    public const string Prefix = "mfg_";
    public const string MaterialAutoPrefix = Prefix + "rm";
    public const string PackAutoPrefix = Prefix + "pk";

    public static string RecipePrefix(string categoryLetter) => Prefix + categoryLetter.ToLowerInvariant();

    /// <summary>제조법 코드의 분류 글자(C/S/H/D). "mfg_h14" → "H", 옛 코드 "H14" → "H".</summary>
    public static string RecipeCategory(string code)
    {
        var body = code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? code[Prefix.Length..] : code;
        return body.Length > 0 ? body[..1].ToUpperInvariant() : string.Empty;
    }

    /// <summary>제조법 코드 표시용 짧은 형태(예: "mfg_h14" → "H14"). 연결 후보 문구·검색에서 옛 코드와 맞춰 볼 때 쓴다.</summary>
    public static string RecipeShort(string code) =>
        (code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? code[Prefix.Length..] : code).ToUpperInvariant();

    public static bool HasPrefix(string code) => code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}
