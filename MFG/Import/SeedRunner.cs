using System.Text;
using MiniERP2.MFG.Config;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Integration;

namespace MiniERP2.MFG.Import;

/// <summary>
/// 초기 데이터 가져오기 전체 절차(설정 탭 버튼과 명령줄 "--seed" 공용):
/// 백업 → (기존 데이터 있으면 비우기) → MiniERP2 매입처 동기화 → 엑셀 이관 → 검증 리포트 저장.
/// </summary>
public static class SeedRunner
{
    /// <returns>(리포트 텍스트, 리포트 파일 경로)</returns>
    public static (string Report, string ReportPath) Run(string recipePath, string? productMasterPath, MiniErp2Bridge bridge, bool clearExisting)
    {
        var backup = new MfgBackupService();
        if (SeedImporter.HasData())
        {
            if (!clearExisting) throw new InvalidOperationException("mfgdb에 이미 데이터가 있습니다.");
            backup.CreateBackup("before_reimport");
            SeedImporter.ClearAll();
        }
        else if (File.Exists(MfgPaths.DatabaseFilePath))
        {
            backup.CreateBackup("before_import");
        }

        // 매입처를 먼저 MiniERP2에서 불러와야 부자재의 매입처 자동 연결이 된다.
        string supplierNote;
        if (bridge.IsAvailable)
        {
            var channels = bridge.GetPurchaseChannels();
            var (added, _) = new SupplierRepository().Sync(channels);
            supplierNote = $"매입처 동기화: MiniERP2 매입 채널 {channels.Count}곳 (신규 {added})";
        }
        else
        {
            supplierNote = $"MiniERP2 DB가 없어 매입처를 불러오지 못했습니다({bridge.DatabasePath}) — 부자재 매입처 자동 연결 생략.";
        }

        var importer = new SeedImporter();
        importer.Run(recipePath, productMasterPath, new SupplierRepository().GetAll());
        var report = BuildReport(importer.Report, supplierNote, recipePath, productMasterPath);
        var reportPath = Path.Combine(MfgPaths.AppDataFolder, $"import_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(reportPath, report, Encoding.UTF8);
        return (report, reportPath);
    }

    public static string BuildReport(ImportReport r, string supplierNote, string recipePath, string? pmPath)
    {
        var snap = CostSnapshot.Load();
        var sb = new StringBuilder();
        sb.AppendLine($"[초기 데이터 가져오기 완료 {DateTime.Now:yyyy-MM-dd HH:mm}]");
        sb.AppendLine($"신제조법: {recipePath}");
        sb.AppendLine($"product_master: {pmPath ?? "(생략)"}");
        sb.AppendLine(supplierNote);
        sb.AppendLine($"원료 {r.Materials} / 단가이력 {r.PriceHistory} / 혼합물 구성 {r.Mixtures} / 제조법 {r.Recipes} / 부자재 {r.Packs} / 완제품 {r.Products}");
        sb.AppendLine("모든 제조법·완제품은 '미확정'입니다. 확인하면서 확정 버튼으로 표시하세요.");
        sb.AppendLine("가격은 엑셀(VAT별도) × 1.1 = VAT포함으로 저장했습니다. VAT별도 보기로 바꾸면 엑셀 숫자와 같습니다.");
        sb.AppendLine("원료목록 단가는 신제조법 작성일(2026-10-20) 기준 현재가로 기록했습니다(원래 입력일은 단가 메모에 있음).");
        sb.AppendLine();
        sb.AppendLine("[검증] 제조법 kg당 원료비(VAT별도): 엑셀 시트 vs 이관 후 계산");
        var mismatch = 0;
        foreach (var (code, name, sheetKg, id) in r.RecipeChecks)
        {
            var recipe = snap.Recipes.FirstOrDefault(x => x.Id == id);
            if (recipe is null) continue;
            var calc = snap.Recipe(recipe, false).KgCost;
            if (Math.Abs(calc - sheetKg) > 0.01)
            {
                mismatch++;
                sb.AppendLine($"  ✗ {code} {name}: 시트 {Fmt.Won(sheetKg)} / 계산 {Fmt.Won(calc)}");
            }
        }
        sb.AppendLine(mismatch == 0 ? $"  {r.RecipeChecks.Count}개 모두 일치" : $"  {r.RecipeChecks.Count}개 중 불일치 {mismatch}건 (아래 경고 참고)");
        sb.AppendLine();
        if (r.PacksWithoutSupplier.Count > 0)
        {
            sb.AppendLine($"[매입처 자동 연결 못 한 부자재 {r.PacksWithoutSupplier.Count}개] — 부자재 탭에서 기본 매입처를 지정하세요");
            foreach (var n in r.PacksWithoutSupplier) sb.AppendLine("  - " + n);
            sb.AppendLine();
        }
        sb.AppendLine($"[경고 {r.Warnings.Count}건]");
        foreach (var w in r.Warnings) sb.AppendLine("  - " + w);
        return sb.ToString();
    }
}
