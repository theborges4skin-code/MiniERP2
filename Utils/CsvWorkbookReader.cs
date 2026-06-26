using System.Text;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// CSV 파일을 읽어 OrderLoader/SettlementLoader 등 기존 엑셀 처리 파이프라인이 그대로 쓸 수 있는
/// ExcelPackage(단일 시트)로 변환합니다. 인코딩은 UTF-8(BOM 포함)과 한글 Windows 기본 인코딩(CP949)을
/// 모두 시도합니다(엑셀에서 "CSV(쉼표로 분리)"로 저장하면 보통 CP949, "CSV UTF-8"로 저장하면 UTF-8).
/// </summary>
public static class CsvWorkbookReader
{
    private static bool _codePagesRegistered;

    public static ExcelPackage LoadAsPackage(string filePath, string sheetName = "Sheet1")
    {
        ExcelLicense.Ensure();

        var text = ReadTextWithEncodingDetection(filePath);
        var rows = ParseCsv(text);

        // 호출자가 Dispose를 책임진다(다른 로더들이 ExcelPackage를 직접 생성해 using으로 관리하는 것과 동일한 패턴).
        var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add(sheetName);

        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < rows[r].Count; c++)
            {
                sheet.Cells[r + 1, c + 1].Value = rows[r][c];
            }
        }

        return package;
    }

    private static string ReadTextWithEncodingDetection(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // UTF-8로 디코딩되지 않으면 한글 Windows 기본 인코딩(CP949)으로 시도한다.
            EnsureCodePagesRegistered();
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    private static void EnsureCodePagesRegistered()
    {
        if (_codePagesRegistered) return;
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        _codePagesRegistered = true;
    }

    /// <summary>
    /// RFC4180 기준 CSV 파서. 인용부호로 감싼 필드 안의 쉼표/개행/이중인용부호를 올바르게 처리한다.
    /// </summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break; // \n과 함께 오는 경우 무시, 단독으로 와도 다음 분기에서 처리되지 않으므로 줄바꿈으로도 취급하지 않음(일관성을 위해 \n만 행 구분자로 사용)
                case '\n':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        // 마지막 줄(개행으로 끝나지 않은 경우) 처리
        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}
