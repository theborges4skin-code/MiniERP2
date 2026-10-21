using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Data;

public class ProductRepository
{
    public List<FinishedProduct> GetAll()
    {
        using var c = MfgDb.Open();
        return GetAll(c);
    }

    internal List<FinishedProduct> GetAll(SqliteConnection c, SqliteTransaction? tx = null)
    {
        var list = new List<FinishedProduct>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM FinishedProduct ORDER BY SortOrder, Id", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                list.Add(new FinishedProduct
                {
                    Id = r.Lng("Id"), Sku = r.NStr("Sku"), Name = r.Str("Name"), Category = r.Str("Category"), ProductGroup = r.Str("ProductGroup"),
                    ConsumerPrice = r.NDbl("ConsumerPrice"), SourceType = r.Str("SourceType"), RecipeSizeId = r.NLng("RecipeSizeId"),
                    MaterialId = r.NLng("MaterialId"), MaterialQty = r.NDbl("MaterialQty"), MaterialQtyUnit = r.Str("MaterialQtyUnit"),
                    ManualRaw = r.Dbl("ManualRaw"), ContainerCost = r.Dbl("ContainerCost"), PackageCost = r.Dbl("PackageCost"),
                    StickerCost = r.Dbl("StickerCost"), EtcCost = r.Dbl("EtcCost"), ShippingCost = r.Dbl("ShippingCost"),
                    Notes = r.Str("Notes"), SuggestedLink = r.Str("SuggestedLink"), Status = r.Str("Status"),
                    ConfirmedTotal = r.NDbl("ConfirmedTotal"), ConfirmedAt = r.NStr("ConfirmedAt"),
                    LastPushedCost = r.NDbl("LastPushedCost"), LastPushedAt = r.NStr("LastPushedAt"),
                    SortOrder = (int)r.Lng("SortOrder"), IsActive = r.Bool("IsActive"),
                });
            }
        }

        var comps = new List<FinishedProductComponent>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM FinishedProductComponent ORDER BY Id", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                comps.Add(new FinishedProductComponent { Id = r.Lng("Id"), ProductId = r.Lng("ProductId"), ComponentProductId = r.Lng("ComponentProductId"), Quantity = r.Dbl("Quantity") });
            }
        }
        var byProduct = comps.ToLookup(x => x.ProductId);
        foreach (var p in list) p.Components = byProduct[p.Id].ToList();
        return list;
    }

    public long Save(FinishedProduct p)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        Save(c, p, tx);
        tx.Commit();
        return p.Id;
    }

    internal void Save(SqliteConnection c, FinishedProduct p, SqliteTransaction? tx)
    {
        var now = MfgDb.Now();
        var args = new (string, object?)[]
        {
            ("$sku", string.IsNullOrWhiteSpace(p.Sku) ? null : p.Sku.Trim()), ("$name", p.Name), ("$cat", p.Category), ("$grp", p.ProductGroup),
            ("$cp", p.ConsumerPrice), ("$st", p.SourceType), ("$rs", p.RecipeSizeId), ("$mid", p.MaterialId), ("$mq", p.MaterialQty),
            ("$mu", p.MaterialQtyUnit), ("$raw", p.ManualRaw), ("$con", p.ContainerCost), ("$pkg", p.PackageCost), ("$stk", p.StickerCost),
            ("$etc", p.EtcCost), ("$shp", p.ShippingCost), ("$notes", p.Notes), ("$sug", p.SuggestedLink), ("$status", p.Status),
            ("$ct", p.ConfirmedTotal), ("$cat2", p.ConfirmedAt), ("$lpc", p.LastPushedCost), ("$lpa", p.LastPushedAt),
            ("$so", p.SortOrder), ("$act", p.IsActive ? 1 : 0), ("$now", now), ("$id", p.Id),
        };
        if (p.Id == 0)
        {
            p.Id = MfgDb.Insert(c, """
                INSERT INTO FinishedProduct(Sku,Name,Category,ProductGroup,ConsumerPrice,SourceType,RecipeSizeId,MaterialId,MaterialQty,MaterialQtyUnit,
                  ManualRaw,ContainerCost,PackageCost,StickerCost,EtcCost,ShippingCost,Notes,SuggestedLink,Status,ConfirmedTotal,ConfirmedAt,
                  LastPushedCost,LastPushedAt,SortOrder,IsActive,CreatedAt,UpdatedAt)
                VALUES($sku,$name,$cat,$grp,$cp,$st,$rs,$mid,$mq,$mu,$raw,$con,$pkg,$stk,$etc,$shp,$notes,$sug,$status,$ct,$cat2,$lpc,$lpa,$so,$act,$now,$now)
                """, tx, args);
        }
        else
        {
            MfgDb.Exec(c, """
                UPDATE FinishedProduct SET Sku=$sku,Name=$name,Category=$cat,ProductGroup=$grp,ConsumerPrice=$cp,SourceType=$st,RecipeSizeId=$rs,
                  MaterialId=$mid,MaterialQty=$mq,MaterialQtyUnit=$mu,ManualRaw=$raw,ContainerCost=$con,PackageCost=$pkg,StickerCost=$stk,
                  EtcCost=$etc,ShippingCost=$shp,Notes=$notes,SuggestedLink=$sug,Status=$status,ConfirmedTotal=$ct,ConfirmedAt=$cat2,
                  LastPushedCost=$lpc,LastPushedAt=$lpa,SortOrder=$so,IsActive=$act,UpdatedAt=$now WHERE Id=$id
                """, tx, args);
        }

        MfgDb.Exec(c, "DELETE FROM FinishedProductComponent WHERE ProductId=$p", tx, ("$p", p.Id));
        foreach (var comp in p.Components)
        {
            comp.ProductId = p.Id;
            comp.Id = MfgDb.Insert(c, "INSERT INTO FinishedProductComponent(ProductId,ComponentProductId,Quantity) VALUES($p,$c,$q)", tx,
                ("$p", p.Id), ("$c", comp.ComponentProductId), ("$q", comp.Quantity));
        }
    }

    public void Delete(long id)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        MfgDb.Exec(c, "DELETE FROM FinishedProductComponent WHERE ComponentProductId=$id", tx, ("$id", id));
        MfgDb.Exec(c, "DELETE FROM FinishedProduct WHERE Id=$id", tx, ("$id", id));
        tx.Commit();
    }

    public void MarkPushed(IEnumerable<(long Id, double Cost)> pushed)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        var now = MfgDb.Now();
        foreach (var (id, cost) in pushed)
        {
            MfgDb.Exec(c, "UPDATE FinishedProduct SET LastPushedCost=$c, LastPushedAt=$t WHERE Id=$id", tx, ("$c", cost), ("$t", now), ("$id", id));
        }
        tx.Commit();
    }
}
