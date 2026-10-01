using System.Runtime.InteropServices;

namespace MiniERP2.Utils;

/// <summary>설치된 Excel로 xlsx를 PDF로 바꾼다(엑셀의 인쇄 설정 그대로). Excel이 없으면 예외.</summary>
public static class ExcelPdfConverter
{
    public static void Convert(string xlsxPath, string pdfPath)
    {
        var type = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("이 PC에 Excel이 설치되어 있지 않아 PDF를 만들 수 없습니다. 엑셀 파일을 열어 직접 PDF로 인쇄하세요.");
        dynamic? excel = null;
        dynamic? workbook = null;
        try
        {
            excel = Activator.CreateInstance(type)!;
            excel.Visible = false;
            excel.DisplayAlerts = false;
            workbook = excel.Workbooks.Open(Path.GetFullPath(xlsxPath), ReadOnly: true);
            workbook.ExportAsFixedFormat(0, Path.GetFullPath(pdfPath)); // 0 = xlTypePDF, 통합문서 전체
        }
        finally
        {
            if (workbook is not null) { workbook.Close(false); Marshal.FinalReleaseComObject(workbook); }
            if (excel is not null) { excel.Quit(); Marshal.FinalReleaseComObject(excel); }
        }
    }
}
