namespace MiniERP2.MFG.Domain;

public static class RecipeStatus
{
    public const string Draft = "미확정";
    public const string Confirmed = "확정";
}

/// <summary>완제품 원료 열 연결 유형.</summary>
public static class ProductSourceType
{
    public const string Manual = "Manual";
    public const string RecipeSize = "RecipeSize";
    public const string Material = "Material";
    public const string Bundle = "Bundle";

    public static string Label(string type) => type switch
    {
        RecipeSize => "제조법 용량",
        Material => "원료 소분",
        Bundle => "묶음",
        _ => "수동값",
    };
}

/// <summary>제조법 용량별 원가 항목(제조법관련 엑셀 하단 표의 행과 같다).</summary>
public static class SizeCostCategory
{
    public const string Container = "용기";
    public const string Cap = "캡·펌프·스포이드";
    public const string Label = "라벨·스티커";
    public const string Box = "단상자·박스";
    public const string EtcPack = "기타 부자재";
    public const string Processing = "임가공비";
    public const string Inspection = "검사비";
    public const string Logistics = "물류·택배비";

    public static readonly string[] All = [Container, Cap, Label, Box, EtcPack, Processing, Inspection, Logistics];

    /// <summary>부자재 목록의 분류로 쓰는 항목(가공비·검사비·물류비 제외).</summary>
    public static readonly string[] PackCategories = [Container, Cap, Label, Box, EtcPack];
}

public class Supplier
{
    public string ChannelCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SyncedAt { get; set; }
    public string Memo { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({ChannelCode})";
}

public class Material
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Aliases { get; set; } = string.Empty;
    public string Inci { get; set; } = string.Empty;
    public string Cas { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public double? Density { get; set; }
    public bool IsMixture { get; set; }
    public bool IsTaxFree { get; set; }
    public bool IsActive { get; set; } = true;
    public string? DefaultSupplierCode { get; set; }
    public string Note { get; set; } = string.Empty;

    /// <summary>조회 시 함께 채워지는 현재 단가(VAT포함 원/kg). 단가가 없으면 null.</summary>
    public MaterialPrice? CurrentPrice { get; set; }

    public IEnumerable<string> AliasList =>
        Aliases.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string ToString() => $"{Name} [{Code}]";
}

public class MaterialPrice
{
    public long Id { get; set; }
    public long MaterialId { get; set; }
    public string? SupplierCode { get; set; }
    public double PricePerKg { get; set; }
    public string EffectiveDate { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool IsUncertain { get; set; }
    public string Note { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class MaterialComponent
{
    public long Id { get; set; }
    public long MaterialId { get; set; }
    public int SortOrder { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string Cas { get; set; } = string.Empty;
    public double Ratio { get; set; }
}

public class PackMaterial
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = SizeCostCategory.EtcPack;
    public string Name { get; set; } = string.Empty;
    public string? DefaultSupplierCode { get; set; }
    public bool IsTaxFree { get; set; }
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = string.Empty;

    public PackMaterialPrice? CurrentPrice { get; set; }

    public override string ToString() => $"{Name} [{Code}]";
}

public class PackMaterialPrice
{
    public long Id { get; set; }
    public long PackMaterialId { get; set; }
    public string? SupplierCode { get; set; }
    public double UnitPrice { get; set; }
    public string EffectiveDate { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class Recipe
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourceSheet { get; set; } = string.Empty;
    public string Status { get; set; } = RecipeStatus.Draft;
    public string? ConfirmedAt { get; set; }
    public string ConfirmNote { get; set; } = string.Empty;
    public double? MeasuredDensity { get; set; }
    public double BatchKg { get; set; } = 10;
    public string ProcessMemo { get; set; } = string.Empty;
    public bool IsArchived { get; set; }

    public List<RecipeLine> Lines { get; set; } = [];
    public List<RecipeSize> Sizes { get; set; } = [];

    public bool IsConfirmed => Status == RecipeStatus.Confirmed;

    public override string ToString() => $"{Code} {Name}";
}

public class RecipeLine
{
    public long Id { get; set; }
    public long RecipeId { get; set; }
    public int SortOrder { get; set; }
    public string Stage { get; set; } = string.Empty;
    public long MaterialId { get; set; }
    /// <summary>0~1 비율(1 = 100%).</summary>
    public double Percent { get; set; }
    public string Memo { get; set; } = string.Empty;
}

public class RecipeSize
{
    public long Id { get; set; }
    public long RecipeId { get; set; }
    public int SortOrder { get; set; }
    public double VolumeMl { get; set; }
    public string Memo { get; set; } = string.Empty;
    public List<RecipeSizeCost> Costs { get; set; } = [];
}

public class RecipeSizeCost
{
    public long Id { get; set; }
    public long RecipeSizeId { get; set; }
    public string Category { get; set; } = string.Empty;
    public long? PackMaterialId { get; set; }
    /// <summary>VAT포함 직접 입력 금액(부자재 연결이 없을 때).</summary>
    public double? ManualAmount { get; set; }
    public double Quantity { get; set; } = 1;
    public string Memo { get; set; } = string.Empty;
}

public class RecipeConfirmLog
{
    public long Id { get; set; }
    public long RecipeId { get; set; }
    public string ConfirmedAt { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public double KgCost { get; set; }
    public double Density { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
}

public class FinishedProduct
{
    public long Id { get; set; }
    public string? Sku { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ProductGroup { get; set; } = string.Empty;
    public double? ConsumerPrice { get; set; }
    public string SourceType { get; set; } = ProductSourceType.Manual;
    public long? RecipeSizeId { get; set; }
    public long? MaterialId { get; set; }
    public double? MaterialQty { get; set; }
    public string MaterialQtyUnit { get; set; } = "ml";
    public double ManualRaw { get; set; }
    public double ContainerCost { get; set; }
    public double PackageCost { get; set; }
    public double StickerCost { get; set; }
    public double EtcCost { get; set; }
    public double ShippingCost { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string SuggestedLink { get; set; } = string.Empty;
    public string Status { get; set; } = RecipeStatus.Draft;
    public double? ConfirmedTotal { get; set; }
    public string? ConfirmedAt { get; set; }
    public double? LastPushedCost { get; set; }
    public string? LastPushedAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<FinishedProductComponent> Components { get; set; } = [];

    public bool IsConfirmed => Status == RecipeStatus.Confirmed;

    public override string ToString() => string.IsNullOrEmpty(Sku) ? Name : $"{Sku} {Name}";
}

public class FinishedProductComponent
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long ComponentProductId { get; set; }
    public double Quantity { get; set; } = 1;
}
