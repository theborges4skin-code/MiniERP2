using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Config;

namespace MiniERP2.MFG.Tests;

/// <summary>테스트마다 임시 AppData 폴더(= 새 mfgdb)를 쓴다.</summary>
public abstract class TestDb
{
    private string _folder = string.Empty;
    private string _original = string.Empty;

    [TestInitialize]
    public void SetUpDb()
    {
        _original = MfgPaths.AppDataFolder;
        _folder = Path.Combine(Path.GetTempPath(), "mfg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        MfgPaths.AppDataFolder = _folder;
    }

    [TestCleanup]
    public void TearDownDb()
    {
        MfgPaths.AppDataFolder = _original;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    protected string Folder => _folder;
}
