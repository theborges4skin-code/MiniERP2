using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

/// <summary>
/// 실제 레거시 DB 대신, 구버전 MiniERP(V3)와 동일한 테이블 구조를 가진 합성 SQLite 파일을
/// 만들어 LegacyMigrationService가 올바르게 변환하는지 검증한다.
/// </summary>
[TestClass]
public class LegacyMigrationServiceTests
{
    private string _testFolder = string.Empty;
    private string _legacyDbPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _legacyDbPath = Path.Combine(_testFolder, "legacy.sqlite");

        CreateSyntheticLegacyDb();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private void CreateSyntheticLegacyDb()
    {
        using var connection = new SqliteConnection($"Data Source={_legacyDbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE SalesChannelTable (
                ChannelCode TEXT, ChannelName TEXT, ChannelType TEXT, ChannelTypeLabel TEXT,
                ExchangeRate REAL, GlobalHeaderRow INTEGER,
                HeaderReceiver TEXT, HeaderPhone TEXT, HeaderAddress TEXT,
                ReceiverName TEXT, Phone TEXT, Address TEXT,
                MappingJson TEXT, GrowthAuxSourcesJson TEXT
            );
            CREATE TABLE ItemTable (SKU TEXT, ItemName TEXT, CostPrice REAL, Extra1 TEXT, Extra2 TEXT, Extra3 TEXT);
            CREATE TABLE ChannelSkuTable (ChannelCode TEXT, SkuName TEXT, SupplyPrice REAL);
            CREATE TABLE RuleExactTable (ChannelCode TEXT, LegacyKey TEXT, TargetSku TEXT);
            CREATE TABLE RuleExceptionTable (ChannelCode TEXT, LegacyKey TEXT, TargetSku TEXT);
            CREATE TABLE RuleTempSkuTable (TempSku TEXT, ItemGroup TEXT, ItemName TEXT, CostPrice REAL);

            INSERT INTO SalesChannelTable VALUES (
                'CH01', '쿠팡그로스', '온라인', '4. 쿠팡(그로스)', 1.0, 2,
                '등록상품명', NULL, NULL,
                NULL, NULL, NULL,
                '{"STD_PRODUCT_NAME":{"col":"등록상품명"},"STD_OPTION":{"col":"옵션명"},"STD_QTY":{"col":"판매수량"},"STD_SETTLEMENT":{"col":"정산대상액"},"STD_SHIPPING":{"col":"보조소스 사용"},"STD_AMT":{"col":"매출금액"}}',
                '[{"enabled":"Y","target_std_field":"STD_SHIPPING","sheet_name":"배송비","header_row":1,"key_header":"옵션ID","value_header":"최종비용","out_col":"최종비용"}]'
            );
            INSERT INTO SalesChannelTable VALUES (
                'CH02', '고정거래처A', '거래처', '거래처(고정)', 1.0, 1,
                NULL, NULL, NULL,
                '홍길동', '010-1111-2222', '서울시 어딘가',
                '{}', '[]'
            );

            INSERT INTO ItemTable VALUES ('SKU1', '상품1', 1000, '01.원료', '메모1', NULL);

            INSERT INTO ChannelSkuTable VALUES ('CH01', 'SKU1', 5000);

            INSERT INTO RuleExactTable VALUES ('CH01', 'CH01::상품A_옵션1_', 'SKU1');
            INSERT INTO RuleExceptionTable VALUES ('CH01', 'CH01::<기본배송료>__', '[EXCLUDED]');

            INSERT INTO RuleTempSkuTable VALUES ('TEMP_LEGACY_1', '21.원료', '임시상품', 2000);
            """;
        command.ExecuteNonQuery();
    }

    [TestMethod]
    public void Migrate_ImportsChannelsItemsRulesAndPrices()
    {
        var service = new LegacyMigrationService();
        var result = service.Migrate(_legacyDbPath);

        Assert.AreEqual(2, result.ChannelsImported);
        Assert.AreEqual(1, result.ItemsImported);
        Assert.AreEqual(1, result.ChannelSkusImported);
        Assert.AreEqual(2, result.RulesImported); // exact 1건 + exception 1건
        Assert.AreEqual(1, result.TempSkusImported);

        // 채널/마스터SKU/납품가가 실제로 저장되었는지 확인
        var channels = new SalesChannelRepository().GetAll();
        Assert.IsTrue(channels.Any(c => c.ChannelCode == "CH01" && c.ChannelName == "쿠팡그로스"));

        var item = new ItemRepository().GetBySku("SKU1");
        Assert.IsNotNull(item);
        Assert.AreEqual("01.원료", item.ProductGroup);

        var tempItem = new ItemRepository().GetBySku("TEMP_LEGACY_1");
        Assert.IsNotNull(tempItem);
        Assert.AreEqual(2000m, tempItem.CostPrice);

        var csku = new ChannelSkuRepository().GetByChannelAndMsku("CH01", "SKU1");
        Assert.IsNotNull(csku);
        Assert.AreEqual(5000m, csku.SupplyPrice);
    }

    [TestMethod]
    public void Migrate_TranslatesChannelTypeAndFieldMappings()
    {
        var service = new LegacyMigrationService();
        service.Migrate(_legacyDbPath);

        var configs = new ChannelConfigService().Load();
        var growth = configs.First(c => c.ChannelCode == "CH01");

        Assert.AreEqual(ChannelType.CoupangGrowth, growth.ChannelType);
        Assert.AreEqual("등록상품명", growth.OrderFieldMappings[StdField.ProductName].Column);
        Assert.AreEqual("정산대상액", growth.SettlementFieldMappings[StdField.SettlementAmount].Column);
        // "보조소스 사용"은 실제 헤더가 아니므로 컬럼 매핑에 들어가면 안 된다.
        Assert.IsFalse(growth.SettlementFieldMappings.ContainsKey(StdField.ShippingFee));
        Assert.HasCount(1, growth.GrowthAuxSources);
        Assert.AreEqual(StdField.ShippingFee, growth.GrowthAuxSources[0].TargetStdField);

        var partner = configs.First(c => c.ChannelCode == "CH02");
        Assert.AreEqual(ChannelType.Partner, partner.ChannelType);
        Assert.AreEqual("홍길동", partner.OrderFieldMappings[StdField.Recipient].FixedValue);
    }

    [TestMethod]
    public void Migrate_StripsChannelPrefixFromLegacyKey()
    {
        var service = new LegacyMigrationService();
        service.Migrate(_legacyDbPath);

        var exactRules = new MappingRepository().GetRules(MappingRuleType.Exact, "CH01");
        Assert.IsTrue(exactRules.Any(r => r.Key == "상품A_옵션1_" && r.TargetSku == "SKU1"));

        var exceptionRules = new MappingRepository().GetRules(MappingRuleType.Exception, "CH01");
        Assert.IsTrue(exceptionRules.Any(r => r.Key == "<기본배송료>__" && r.TargetSku == "[EXCLUDED]"));
    }
}
