using System.Text;
using ExcelDataReader;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// 구형 엑셀(.xls, Excel 97-2003 BIFF) 파일을 읽어 기존 OrderLoader/SettlementLoader 파이프라인이
/// 그대로 쓸 수 있는 ExcelPackage(in-memory)로 변환합니다. EPPlus는 .xlsx(Office Open XML)만
/// 지원하므로 .xls를 EPPlus로 직접 열면 실패하고 "암호가 걸려있다"고 오판하는데, 이 클래스가
/// ExcelDataReader를 거쳐 중간 변환하는 역할을 합니다.
/// 한글 인코딩(CP949)을 위해 CodePages 등록을 먼저 수행합니다.
/// </summary>
public static class XlsWorkbookReader
{
    private static bool _encodingRegistered;

    public static ExcelPackage LoadAsPackage(string filePath)
    {
        ExcelLicense.Ensure();
        EnsureEncodingRegistered();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var package = new ExcelPackage();

        do
        {
            var sheetName = !string.IsNullOrWhiteSpace(reader.Name)
                ? reader.Name
                : $"Sheet{package.Workbook.Worksheets.Count + 1}";

            var sheet = package.Workbook.Worksheets.Add(sheetName);
            int row = 0;

            while (reader.Read())
            {
                row++;
                for (int col = 0; col < reader.FieldCount; col++)
                {
                    var value = reader.GetValue(col);
                    if (value != null)
                        sheet.Cells[row, col + 1].Value = value;
                }
            }
        } while (reader.NextResult());

        return package;
    }

    private static void EnsureEncodingRegistered()
    {
        if (_encodingRegistered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encodingRegistered = true;
    }
}
