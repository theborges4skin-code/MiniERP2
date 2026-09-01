using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class MetaSheetHelperTests
{
    private static string TempXlsxPath()
        => Path.Combine(Path.GetTempPath(), $"MetaSheetHelperTests_{Guid.NewGuid():N}.xlsx");

    [TestMethod]
    public void Write_Read_RoundTrip_IncludesCompanyName()
    {
        var path = TempXlsxPath();
        try
        {
            ExcelLicense.Ensure();
            using (var package = new ExcelPackage())
            {
                package.Workbook.Worksheets.Add("Sheet1");
                package.SaveAs(new FileInfo(path));
            }

            var meta = new FileMeta
            {
                ChannelCode = "CH027",
                ChannelName = "쿠팡일반",
                SourceType = "settlement",
                CompanyName = "펩투나",
                Period = "202609",
            };
            MetaSheetHelper.Write(path, meta);

            var read = MetaSheetHelper.TryRead(path);

            Assert.IsNotNull(read);
            Assert.AreEqual("CH027", read!.ChannelCode);
            Assert.AreEqual("펩투나", read.CompanyName);
            Assert.AreEqual(2, read.SchemaVersion);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void TryRead_V1FileWithoutCompanyNameKey_ReturnsBlankCompanyName()
    {
        var path = TempXlsxPath();
        try
        {
            ExcelLicense.Ensure();
            using (var package = new ExcelPackage())
            {
                var sheet = package.Workbook.Worksheets.Add("_META");
                // v1 스키마: company_name 키 자체가 없음.
                sheet.Cells[1, 1].Value = "schema_version"; sheet.Cells[1, 2].Value = "1";
                sheet.Cells[2, 1].Value = "source_type"; sheet.Cells[2, 2].Value = "settlement";
                sheet.Cells[3, 1].Value = "channel_name"; sheet.Cells[3, 2].Value = "쿠팡일반";
                sheet.Cells[4, 1].Value = "channel_code"; sheet.Cells[4, 2].Value = "CH027";
                package.SaveAs(new FileInfo(path));
            }

            var read = MetaSheetHelper.TryRead(path);

            Assert.IsNotNull(read);
            Assert.AreEqual(1, read!.SchemaVersion);
            Assert.AreEqual("CH027", read.ChannelCode);
            Assert.AreEqual("", read.CompanyName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void TryRead_NoMetaSheet_ReturnsNull()
    {
        var path = TempXlsxPath();
        try
        {
            ExcelLicense.Ensure();
            using (var package = new ExcelPackage())
            {
                package.Workbook.Worksheets.Add("Sheet1");
                package.SaveAs(new FileInfo(path));
            }

            Assert.IsNull(MetaSheetHelper.TryRead(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void WriteToPackage_DoesNotSave_CallerMustSave()
    {
        var path = TempXlsxPath();
        try
        {
            ExcelLicense.Ensure();
            using (var package = new ExcelPackage())
            {
                package.Workbook.Worksheets.Add("분석결과상세");
                MetaSheetHelper.WriteToPackage(package, new FileMeta
                {
                    ChannelCode = "CH099",
                    SourceType = "ad",
                    CompanyName = "한결",
                });
                // WriteToPackage 자체는 저장하지 않는다 — 호출자가 명시적으로 저장해야 함.
                package.SaveAs(new FileInfo(path));
            }

            var read = MetaSheetHelper.TryRead(path);

            Assert.IsNotNull(read);
            Assert.AreEqual("CH099", read!.ChannelCode);
            Assert.AreEqual("ad", read.SourceType);
            Assert.AreEqual("한결", read.CompanyName);
            // _META가 맨 앞에 배치됐는지(MoveToStart) 확인.
            using var verifyPackage = ExcelFileOpener.Open(path);
            Assert.AreEqual("_META", verifyPackage.Workbook.Worksheets[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
