using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class SettlementRowStatusTests
{
    [TestMethod]
    public void IsUnresolved_BlankMsku_ReturnsTrue()
    {
        var data = new SettlementData { Msku = null, Status = "매핑 키 없음" };
        Assert.IsTrue(SettlementRowStatus.IsUnresolved(data));
    }

    [TestMethod]
    public void IsUnresolved_NoCostInfo_ReturnsTrue()
    {
        var data = new SettlementData { Msku = "SKU1", Status = "원가 정보 없음" };
        Assert.IsTrue(SettlementRowStatus.IsUnresolved(data));
    }

    [TestMethod]
    public void IsUnresolved_MappedSuccessfully_ReturnsFalse()
    {
        var data = new SettlementData { Msku = "SKU1", Status = "매핑(1:1)" };
        Assert.IsFalse(SettlementRowStatus.IsUnresolved(data));
    }

    [TestMethod]
    public void IsUnresolved_ExcludedByExceptionRule_ReturnsFalse()
    {
        var data = new SettlementData { Msku = "[EXCLUDED]", Status = "제외(배송비 등)" };
        Assert.IsFalse(SettlementRowStatus.IsUnresolved(data));
    }

    [TestMethod]
    public void IsExcludedByExceptionRule_MatchesStatus()
    {
        var data = new SettlementData { Status = "제외(배송비 등)" };
        Assert.IsTrue(SettlementRowStatus.IsExcludedByExceptionRule(data));
    }
}
