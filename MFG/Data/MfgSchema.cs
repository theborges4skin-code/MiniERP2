using Microsoft.Data.Sqlite;

namespace MiniERP2.MFG.Data;

/// <summary>
/// mfgdb 스키마. MiniERP2 DbSchema와 같은 방식 — CREATE TABLE IF NOT EXISTS + 나중에 늘어나는
/// 열은 <see cref="EnsureColumn"/>로 추가한다(기존 데이터 보존).
///
/// 금액 규칙(docs/MFG_PLAN.md §3-1): 모든 단가·금액은 **VAT포함**으로 저장한다. 화면의 VAT별도
/// 보기는 표시용 환산(과세 항목만 ÷1.1)이다. 투입량(Percent)은 0~1 비율(1 = 100%)로 저장한다.
/// </summary>
public static class MfgSchema
{
    public static void EnsureCreated(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Supplier (
                ChannelCode TEXT PRIMARY KEY,           -- MiniERP2 SalesChannelTable.ChannelCode
                Name TEXT NOT NULL,
                SyncedAt TEXT,                          -- MiniERP2에서 마지막으로 불러온 시각
                Memo TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS Material (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL UNIQUE,              -- RM001~ 자동 채번
                Name TEXT NOT NULL,
                Aliases TEXT NOT NULL DEFAULT '',       -- 구 표기명들(줄바꿈 구분)
                Inci TEXT NOT NULL DEFAULT '',          -- 전성분명(표기명)
                Cas TEXT NOT NULL DEFAULT '',
                Role TEXT NOT NULL DEFAULT '',
                Density REAL,                           -- 비중(g/ml)
                IsMixture INTEGER NOT NULL DEFAULT 0,
                IsTaxFree INTEGER NOT NULL DEFAULT 0,   -- 1=면세(VAT별도 보기에서 ÷1.1 안 함)
                IsActive INTEGER NOT NULL DEFAULT 1,
                DefaultSupplierCode TEXT,
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MaterialPrice (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialId INTEGER NOT NULL REFERENCES Material(Id) ON DELETE CASCADE,
                SupplierCode TEXT,
                PricePerKg REAL NOT NULL,               -- 원/kg, VAT포함
                EffectiveDate TEXT NOT NULL,            -- yyyy-MM-dd
                Source TEXT NOT NULL DEFAULT '',
                IsUncertain INTEGER NOT NULL DEFAULT 0,
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_MaterialPrice_Material ON MaterialPrice(MaterialId, EffectiveDate);

            CREATE TABLE IF NOT EXISTS MaterialComponent (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialId INTEGER NOT NULL REFERENCES Material(Id) ON DELETE CASCADE,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                ComponentName TEXT NOT NULL,
                Cas TEXT NOT NULL DEFAULT '',
                Ratio REAL NOT NULL                     -- 0~1
            );

            CREATE TABLE IF NOT EXISTS PackMaterial (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL UNIQUE,              -- PK001~ 자동 채번
                Category TEXT NOT NULL DEFAULT '기타 부자재',
                Name TEXT NOT NULL,
                DefaultSupplierCode TEXT,
                IsTaxFree INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PackMaterialPrice (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PackMaterialId INTEGER NOT NULL REFERENCES PackMaterial(Id) ON DELETE CASCADE,
                SupplierCode TEXT,
                UnitPrice REAL NOT NULL,                -- 원/개, VAT포함
                EffectiveDate TEXT NOT NULL,
                Source TEXT NOT NULL DEFAULT '',
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_PackMaterialPrice_Pack ON PackMaterialPrice(PackMaterialId, EffectiveDate);

            CREATE TABLE IF NOT EXISTS Recipe (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL UNIQUE,              -- C/S/H/D + 번호
                Name TEXT NOT NULL,
                Version TEXT NOT NULL DEFAULT '',
                SourceSheet TEXT NOT NULL DEFAULT '',   -- 이관 원본(엑셀 시트명)
                Status TEXT NOT NULL DEFAULT '미확정',   -- 미확정 / 확정
                ConfirmedAt TEXT,
                ConfirmNote TEXT NOT NULL DEFAULT '',
                MeasuredDensity REAL,                   -- 실측 비중(있으면 계산 비중보다 우선)
                BatchKg REAL NOT NULL DEFAULT 10,
                ProcessMemo TEXT NOT NULL DEFAULT '',
                IsArchived INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RecipeLine (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeId INTEGER NOT NULL REFERENCES Recipe(Id) ON DELETE CASCADE,
                SortOrder INTEGER NOT NULL,
                Stage TEXT NOT NULL DEFAULT '',
                MaterialId INTEGER NOT NULL REFERENCES Material(Id),
                Percent REAL NOT NULL,                  -- 0~1 (1 = 100%)
                Memo TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS RecipeSize (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeId INTEGER NOT NULL REFERENCES Recipe(Id) ON DELETE CASCADE,
                SortOrder INTEGER NOT NULL,
                VolumeMl REAL NOT NULL,
                Memo TEXT NOT NULL DEFAULT ''
            );

            -- 용량별 부자재·가공비: (용량, 항목) 당 한 줄. 부자재 목록에 연결(PackMaterialId)하면
            -- 현재 단가 × 수량을, 연결이 없으면 직접 입력 금액(ManualAmount, VAT포함)을 쓴다.
            CREATE TABLE IF NOT EXISTS RecipeSizeCost (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeSizeId INTEGER NOT NULL REFERENCES RecipeSize(Id) ON DELETE CASCADE,
                Category TEXT NOT NULL,
                PackMaterialId INTEGER REFERENCES PackMaterial(Id),
                ManualAmount REAL,
                Quantity REAL NOT NULL DEFAULT 1,
                Memo TEXT NOT NULL DEFAULT ''
            );

            -- 확정 시점의 계산 결과를 그대로 얼려 둔 스냅샷(나중에 단가가 바뀌어도 그때의 근거를 추적).
            CREATE TABLE IF NOT EXISTS RecipeConfirmLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeId INTEGER NOT NULL REFERENCES Recipe(Id) ON DELETE CASCADE,
                ConfirmedAt TEXT NOT NULL,
                Note TEXT NOT NULL DEFAULT '',
                KgCost REAL NOT NULL,                   -- VAT포함 kg당 원료비
                Density REAL NOT NULL,
                SnapshotJson TEXT NOT NULL
            );

            -- 완제품(판매 SKU) 원가표 — product_master(PM)를 대체한다. 원가 구성 열은 PM과 같다
            -- (원료/용기/패키지/스티커 등/기타/택배비, 모두 VAT포함). "원료" 열은 연결 유형에 따라
            -- 제조법 용량·원료 소분·묶음 구성에서 자동 계산되거나(SourceType) 수동값(ManualRaw)이다.
            CREATE TABLE IF NOT EXISTS FinishedProduct (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Sku TEXT UNIQUE,                        -- MiniERP2 ItemTable.Sku (없을 수 있음)
                Name TEXT NOT NULL,
                Category TEXT NOT NULL DEFAULT '',      -- PM 구분
                ProductGroup TEXT NOT NULL DEFAULT '',  -- PM 상품그룹
                ConsumerPrice REAL,                     -- 소비자가(VAT포함)
                SourceType TEXT NOT NULL DEFAULT 'Manual', -- Manual / RecipeSize / Material / Bundle
                RecipeSizeId INTEGER REFERENCES RecipeSize(Id) ON DELETE SET NULL,
                MaterialId INTEGER REFERENCES Material(Id) ON DELETE SET NULL,
                MaterialQty REAL,
                MaterialQtyUnit TEXT NOT NULL DEFAULT 'ml',
                ManualRaw REAL NOT NULL DEFAULT 0,
                ContainerCost REAL NOT NULL DEFAULT 0,
                PackageCost REAL NOT NULL DEFAULT 0,
                StickerCost REAL NOT NULL DEFAULT 0,
                EtcCost REAL NOT NULL DEFAULT 0,
                ShippingCost REAL NOT NULL DEFAULT 0,
                Notes TEXT NOT NULL DEFAULT '',
                SuggestedLink TEXT NOT NULL DEFAULT '', -- 이관 시 유추한 제조법 연결 후보 + 신뢰도 코멘트
                Status TEXT NOT NULL DEFAULT '미확정',
                ConfirmedTotal REAL,                    -- 확정 시점 합계(VAT포함) — MiniERP2로 보내는 값
                ConfirmedAt TEXT,
                LastPushedCost REAL,
                LastPushedAt TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS FinishedProductComponent (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER NOT NULL REFERENCES FinishedProduct(Id) ON DELETE CASCADE,
                ComponentProductId INTEGER NOT NULL REFERENCES FinishedProduct(Id),
                Quantity REAL NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS AppMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>나중에 추가되는 열을 기존 DB에 보강한다(MiniERP2 DbSchema.EnsureColumn과 같은 방식).</summary>
    internal static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnType)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName})";
        using (var reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase)) return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
        alter.ExecuteNonQuery();
    }
}
