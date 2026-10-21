namespace MiniERP2.MFG.Import;

/// <summary>
/// product_master 품목 ↔ 신제조법 배합 대응 후보(유추). 근거·신뢰도는 docs/COST_MANAGER_DRAFT.md §5.
/// 이관 때 FinishedProduct.SuggestedLink에 그대로 기록되어 완제품 화면에 "연결 후보"로 보인다.
/// 자동으로 연결하지는 않는다 — PM 원료 열에 부자재 포함 여부가 품목마다 달라(핸드워시는 단품 전체,
/// 화장품은 원료만) 자동 연결하면 이중 계산될 수 있으므로, 사용자가 비교 화면을 보고 직접 연결한다.
/// 키는 SKU, SKU가 없는 행은 "name:" + 상품명.
/// </summary>
public static class MappingHints
{
    private static readonly (string RecipeCode, string Confidence, string Comment, string[] Keys)[] Hints =
    [
        ("C01", "중간", "PM 15ml K=614 / 30ml K=307로 15·30이 뒤바뀐 것으로 보임", ["amzprs15", "amzprs30"]),
        ("C02", "높음", "PM값/계산값 비율 1.05(비중 미반영 차이)", ["amzcps10", "amzcps15", "amzcps30"]),
        ("C03", "낮음", "PM 원료가 계산값의 13.5배 — 엘케이 완제품 매입가로 보임(외부 매입 여부 확인)", ["pkv15amp", "pkv30amp", "pkv50amp", "amzpkv30"]),
        ("C04", "높음", "비율 0.986", ["amzpdr10", "amzpdr15", "amzpdr30"]),
        ("C11", "높음", "비율 1.05(비중 0.949 미반영)", ["vbten15amp", "vbten30amp", "vbten50amp"]),
        ("C12", "낮음", "PM 'EGF 앰플'이 EGF 단독인지 Evofiline인지 불명(비율 0.93)", ["egf10amp", "egf15amp", "egf50amp"]),
        ("S03", "중간", "비율 1.17", ["rzo50", "amzrzo50"]),
        ("S10", "중간", "비율 1.41 — S11(퀵드라이)과 현재 판매 배합 확인 필요", ["mbc200", "mbc500"]),
        ("H01", "낮음", "'원액'이 농축 판매품용인지 생산 중간재인지 불명", ["amzrcc150br", "amzrcc150ph"]),
        ("H02", "중간", "브라운 단품", ["cprrz500br"]),
        ("H03", "중간", "필립스 단품", ["cprrz500ph"]),
        ("H04", "중간", "파나소닉 단품", ["cprrz500pa"]),
        ("H02", "중간", "PM은 'H02~H04 3종류 평균값' — 평균 처리 방식 결정 필요", ["rz500", "rz500cup"]),
        ("H05", "중간", "비율 2.4(PM 원료에 용기 포함 추정). rz1000은 H05인지 3종 평균인지 확인", ["igrz1000", "rz1000"]),
        ("H06", "중간", "비율 3.3(PM 원료에 용기 포함 추정)", ["rzqd200"]),
        ("H09", "중간", "비율 2.10 일정 — PM은 옛 단가 기준으로 보임", ["igusc500", "igusc1000", "igusc4000", "igusc13", "igusc18", "igusc16"]),
        ("H12", "낮음", "이름 다름(석회 vs 스케일) — 같은 제품인지 추정", ["dsc4000"]),
        ("H14", "높음", "300ml 1149.4 = PM 1149, 500ml 1335.0 = PM 1335 (PM 원료 열 = 단품 전체 원가)",
            ["foam300", "foam4000", "igfoam300", "igfoam500", "igfoam4000", "cprfoam300l", "cprfoam300m", "cprfoam4000l", "cprfoam4000m"]),
        ("H15", "낮음", "이공 핸드(리퀴드)가 H15(물비누형)인지 H16(SLES형)인지 불명", ["ighand500", "ighand4000"]),
        ("H18", "중간", "현재 생산이 2종 A인지 B(260413)인지 불명 — 날짜상 B가 최신", ["coc500", "cpgcoc500"]),
        ("H19", "중간", "디스케일러 = 3종 강산(스케일)으로 추정", ["cod500", "cpgcod500", "amzcod500"]),
        ("H20", "낮음", "PM 품목은 'vita C 1kg 중국' 매입 파우더로 보여 배합과 다를 수 있음", ["amzsrv1kg"]),
        ("D01", "중간", "비율 1.21", ["dbet", "dbet100", "dbet500"]),
        ("D02", "높음", "비율 1.10", ["name:디퓨저베이스(에탄올) 개선 DPG 18L"]),
        ("D05", "낮음", "비율 1.56 — 다른 배합일 가능성", ["db", "db100", "db500", "db18", "db4"]),
        ("D07", "중간", "향수베이스 구성 A/B(D08) 중 어느 쪽인지 불명", ["pb", "pb100", "pb500"]),
        ("D12", "높음", "비율 1.15 — D16(에탄올디퓨저 최종)이 현재 납품 배합일 수도 있음", ["cdz"]),
        ("D13", "높음", "PM 비고 '안씀, 일반 디퓨저베이스랑 통합함' — 폐기 배합", ["name:디퓨저베이스(에탄올 고함량) 18L"]),
        ("D14", "중간", "PM 비고 '원가 다시 계산해야함'", ["name:룸스프레이베이스 18L"]),
        ("D15", "중간", "PM 비고 '원가 다시 계산해야함'", ["name:향수베이스 18L"]),
    ];

    /// <summary>SKU(또는 상품명)에 대한 연결 후보 문구. 없으면 빈 문자열.</summary>
    public static string For(string? sku, string name)
    {
        var parts = new List<string>();
        foreach (var (code, conf, comment, keys) in Hints)
        {
            if (keys.Any(k => (sku is not null && k == sku) || k == "name:" + name.Trim()))
            {
                parts.Add($"{code} (신뢰도 {conf}) — {comment}");
            }
        }
        return string.Join(" / ", parts);
    }

    /// <summary>연결 후보 문구에서 첫 번째 제조법 코드(예: "H14")를 꺼낸다.</summary>
    public static string? FirstRecipeCode(string suggestedLink)
    {
        if (string.IsNullOrWhiteSpace(suggestedLink)) return null;
        var head = suggestedLink.Split(' ')[0];
        return head.Length >= 2 ? head : null;
    }
}
