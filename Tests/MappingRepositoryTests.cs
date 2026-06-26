using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class MappingRepositoryTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void UpsertExactRule_NewKey_InsertsRule()
    {
        var repository = new MappingRepository();
        repository.UpsertExactRule("CH1", "상품A옵션1", "SKU-1");

        var rules = repository.GetRules(MappingRuleType.Exact, "CH1");
        Assert.IsTrue(rules.Any(r => r.Key == "상품A옵션1" && r.TargetSku == "SKU-1"));
    }

    [TestMethod]
    public void UpsertExactRule_ExistingKey_UpdatesTargetSkuWithoutDuplicating()
    {
        var repository = new MappingRepository();
        repository.UpsertExactRule("CH1", "상품A옵션1", "SKU-1");
        repository.UpsertExactRule("CH1", "상품A옵션1", "SKU-2");

        var rules = repository.GetRules(MappingRuleType.Exact, "CH1");
        Assert.HasCount(1, rules.Where(r => r.Key == "상품A옵션1"));
        Assert.AreEqual("SKU-2", rules.Single(r => r.Key == "상품A옵션1").TargetSku);
    }
}
