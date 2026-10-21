using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Data;

public class RecipeRepository
{
    /// <summary>코드 첫 글자: C=화장품, S=면도·바디·헤어, H=세정제, D=방향제·디퓨저(신제조법 규칙).</summary>
    public static readonly (string Prefix, string Label)[] Categories =
        [("C", "C 화장품"), ("S", "S 면도·바디·헤어"), ("H", "H 세정제"), ("D", "D 방향제·디퓨저")];

    public List<Recipe> GetAll(bool withDetails = true)
    {
        using var c = MfgDb.Open();
        return GetAll(c, withDetails);
    }

    internal List<Recipe> GetAll(SqliteConnection c, bool withDetails = true, SqliteTransaction? tx = null)
    {
        var list = new List<Recipe>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM Recipe ORDER BY Code", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                list.Add(new Recipe
                {
                    Id = r.Lng("Id"), Code = r.Str("Code"), Name = r.Str("Name"), Version = r.Str("Version"), SourceSheet = r.Str("SourceSheet"),
                    Status = r.Str("Status"), ConfirmedAt = r.NStr("ConfirmedAt"), ConfirmNote = r.Str("ConfirmNote"),
                    MeasuredDensity = r.NDbl("MeasuredDensity"), BatchKg = r.Dbl("BatchKg"), ProcessMemo = r.Str("ProcessMemo"), IsArchived = r.Bool("IsArchived"),
                });
            }
        }
        if (!withDetails) return list;

        var lines = new List<RecipeLine>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM RecipeLine ORDER BY RecipeId, SortOrder, Id", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                lines.Add(new RecipeLine
                {
                    Id = r.Lng("Id"), RecipeId = r.Lng("RecipeId"), SortOrder = (int)r.Lng("SortOrder"), Stage = r.Str("Stage"),
                    MaterialId = r.Lng("MaterialId"), Percent = r.Dbl("Percent"), Memo = r.Str("Memo"),
                });
            }
        }

        var sizes = new List<RecipeSize>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM RecipeSize ORDER BY RecipeId, SortOrder, Id", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                sizes.Add(new RecipeSize { Id = r.Lng("Id"), RecipeId = r.Lng("RecipeId"), SortOrder = (int)r.Lng("SortOrder"), VolumeMl = r.Dbl("VolumeMl"), Memo = r.Str("Memo") });
            }
        }

        var costs = new List<RecipeSizeCost>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM RecipeSizeCost ORDER BY Id", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                costs.Add(new RecipeSizeCost
                {
                    Id = r.Lng("Id"), RecipeSizeId = r.Lng("RecipeSizeId"), Category = r.Str("Category"), PackMaterialId = r.NLng("PackMaterialId"),
                    ManualAmount = r.NDbl("ManualAmount"), Quantity = r.Dbl("Quantity"), Memo = r.Str("Memo"),
                });
            }
        }

        var costsBySize = costs.ToLookup(x => x.RecipeSizeId);
        foreach (var s in sizes) s.Costs = costsBySize[s.Id].ToList();
        var linesByRecipe = lines.ToLookup(x => x.RecipeId);
        var sizesByRecipe = sizes.ToLookup(x => x.RecipeId);
        foreach (var rec in list)
        {
            rec.Lines = linesByRecipe[rec.Id].ToList();
            rec.Sizes = sizesByRecipe[rec.Id].ToList();
        }
        return list;
    }

    public Recipe? Get(long id) => GetAll().FirstOrDefault(r => r.Id == id);

    /// <param name="prefix">분류 글자(C/S/H/D) — 코드는 "mfg_" + 소문자 분류 + 번호.</param>
    public string NextCode(string prefix)
    {
        using var c = MfgDb.Open();
        return CodeGenerator.Next(c, "Recipe", MfgCodes.RecipePrefix(prefix), 2);
    }

    /// <summary>
    /// 제조법 저장(헤더 + 배합행 + 용량/원가). 배합행과 원가 줄은 통째로 다시 쓰지만, 용량(RecipeSize)은
    /// Id를 유지한 채 갱신한다 — 완제품(FinishedProduct.RecipeSizeId)이 용량 Id로 연결되어 있기 때문이다.
    /// 화면에서 지운 용량만 삭제된다(연결된 완제품은 연결이 풀린다: ON DELETE SET NULL).
    /// </summary>
    public long Save(Recipe recipe)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        Save(c, recipe, tx);
        tx.Commit();
        return recipe.Id;
    }

    internal void Save(SqliteConnection c, Recipe recipe, SqliteTransaction? tx)
    {
        var now = MfgDb.Now();
        if (recipe.Id == 0 && string.IsNullOrWhiteSpace(recipe.Code)) recipe.Code = CodeGenerator.Next(c, "Recipe", MfgCodes.RecipePrefix("C"), 2, tx);
        var args = new (string, object?)[]
        {
            ("$code", recipe.Code), ("$name", recipe.Name.Trim()), ("$ver", recipe.Version), ("$src", recipe.SourceSheet), ("$st", recipe.Status),
            ("$cat", recipe.ConfirmedAt), ("$cn", recipe.ConfirmNote), ("$md", recipe.MeasuredDensity), ("$bk", recipe.BatchKg),
            ("$pm", recipe.ProcessMemo), ("$ar", recipe.IsArchived ? 1 : 0), ("$now", now), ("$id", recipe.Id),
        };
        if (recipe.Id == 0)
        {
            recipe.Id = MfgDb.Insert(c, """
                INSERT INTO Recipe(Code,Name,Version,SourceSheet,Status,ConfirmedAt,ConfirmNote,MeasuredDensity,BatchKg,ProcessMemo,IsArchived,CreatedAt,UpdatedAt)
                VALUES($code,$name,$ver,$src,$st,$cat,$cn,$md,$bk,$pm,$ar,$now,$now)
                """, tx, args);
        }
        else
        {
            MfgDb.Exec(c, """
                UPDATE Recipe SET Code=$code,Name=$name,Version=$ver,SourceSheet=$src,Status=$st,ConfirmedAt=$cat,ConfirmNote=$cn,
                  MeasuredDensity=$md,BatchKg=$bk,ProcessMemo=$pm,IsArchived=$ar,UpdatedAt=$now WHERE Id=$id
                """, tx, args);
        }

        MfgDb.Exec(c, "DELETE FROM RecipeLine WHERE RecipeId=$r", tx, ("$r", recipe.Id));
        var order = 0;
        foreach (var line in recipe.Lines)
        {
            line.RecipeId = recipe.Id;
            line.SortOrder = order++;
            line.Id = MfgDb.Insert(c, "INSERT INTO RecipeLine(RecipeId,SortOrder,Stage,MaterialId,Percent,Memo) VALUES($r,$o,$s,$m,$p,$memo)", tx,
                ("$r", recipe.Id), ("$o", line.SortOrder), ("$s", line.Stage), ("$m", line.MaterialId), ("$p", line.Percent), ("$memo", line.Memo));
        }

        var keepIds = recipe.Sizes.Where(s => s.Id != 0).Select(s => s.Id).ToHashSet();
        using (var existing = MfgDb.Cmd(c, "SELECT Id FROM RecipeSize WHERE RecipeId=$r", tx, ("$r", recipe.Id)))
        using (var rd = existing.ExecuteReader())
        {
            var toDelete = new List<long>();
            while (rd.Read()) if (!keepIds.Contains(rd.GetInt64(0))) toDelete.Add(rd.GetInt64(0));
            rd.Close();
            foreach (var id in toDelete) MfgDb.Exec(c, "DELETE FROM RecipeSize WHERE Id=$id", tx, ("$id", id));
        }

        order = 0;
        foreach (var size in recipe.Sizes)
        {
            size.RecipeId = recipe.Id;
            size.SortOrder = order++;
            if (size.Id == 0)
            {
                size.Id = MfgDb.Insert(c, "INSERT INTO RecipeSize(RecipeId,SortOrder,VolumeMl,Memo) VALUES($r,$o,$v,$m)", tx,
                    ("$r", recipe.Id), ("$o", size.SortOrder), ("$v", size.VolumeMl), ("$m", size.Memo));
            }
            else
            {
                MfgDb.Exec(c, "UPDATE RecipeSize SET SortOrder=$o,VolumeMl=$v,Memo=$m WHERE Id=$id", tx,
                    ("$o", size.SortOrder), ("$v", size.VolumeMl), ("$m", size.Memo), ("$id", size.Id));
            }

            MfgDb.Exec(c, "DELETE FROM RecipeSizeCost WHERE RecipeSizeId=$s", tx, ("$s", size.Id));
            foreach (var cost in size.Costs)
            {
                if (cost.PackMaterialId is null && cost.ManualAmount is null && string.IsNullOrEmpty(cost.Memo)) continue;
                cost.RecipeSizeId = size.Id;
                cost.Id = MfgDb.Insert(c, "INSERT INTO RecipeSizeCost(RecipeSizeId,Category,PackMaterialId,ManualAmount,Quantity,Memo) VALUES($s,$c,$p,$a,$q,$m)", tx,
                    ("$s", size.Id), ("$c", cost.Category), ("$p", cost.PackMaterialId), ("$a", cost.ManualAmount), ("$q", cost.Quantity), ("$m", cost.Memo));
            }
        }
    }

    public void Delete(long recipeId)
    {
        using var c = MfgDb.Open();
        MfgDb.Exec(c, "DELETE FROM Recipe WHERE Id=$id", null, ("$id", recipeId));
    }

    /// <summary>확정: 상태·확정일을 기록하고 계산 결과 스냅샷(VAT포함)을 확정 이력에 남긴다.</summary>
    public void Confirm(long recipeId, string note, double kgCostIncl, double density, string snapshotJson)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        var now = MfgDb.Now();
        MfgDb.Exec(c, "UPDATE Recipe SET Status=$s, ConfirmedAt=$t, ConfirmNote=$n, UpdatedAt=$t WHERE Id=$id", tx,
            ("$s", RecipeStatus.Confirmed), ("$t", now), ("$n", note), ("$id", recipeId));
        MfgDb.Exec(c, "INSERT INTO RecipeConfirmLog(RecipeId,ConfirmedAt,Note,KgCost,Density,SnapshotJson) VALUES($r,$t,$n,$k,$d,$j)", tx,
            ("$r", recipeId), ("$t", now), ("$n", note), ("$k", kgCostIncl), ("$d", density), ("$j", snapshotJson));
        tx.Commit();
    }

    public void SetDraft(long recipeId)
    {
        using var c = MfgDb.Open();
        MfgDb.Exec(c, "UPDATE Recipe SET Status=$s, UpdatedAt=$t WHERE Id=$id", null, ("$s", RecipeStatus.Draft), ("$t", MfgDb.Now()), ("$id", recipeId));
    }

    public List<RecipeConfirmLog> GetConfirmLogs(long? recipeId = null)
    {
        using var c = MfgDb.Open();
        using var cmd = MfgDb.Cmd(c, "SELECT * FROM RecipeConfirmLog" + (recipeId is null ? "" : " WHERE RecipeId=$r") + " ORDER BY Id DESC", null, ("$r", recipeId));
        using var r = cmd.ExecuteReader();
        var list = new List<RecipeConfirmLog>();
        while (r.Read())
        {
            list.Add(new RecipeConfirmLog
            {
                Id = r.Lng("Id"), RecipeId = r.Lng("RecipeId"), ConfirmedAt = r.Str("ConfirmedAt"), Note = r.Str("Note"),
                KgCost = r.Dbl("KgCost"), Density = r.Dbl("Density"), SnapshotJson = r.Str("SnapshotJson"),
            });
        }
        return list;
    }
}
