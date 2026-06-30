using MiniERP2.Forms;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// 엑셀/CSV 파일을 ExcelPackage로 여는 공통 진입점입니다. CSV는 CsvWorkbookReader로 변환하고,
/// 암호로 보호된 엑셀 파일은 비밀번호 없이 열다 실패하면 EncryptedExcelFileException을 던져
/// 호출 측(UI)이 비밀번호를 입력받아 재시도할 수 있게 합니다.
/// </summary>
public static class ExcelFileOpener
{
    public static ExcelPackage Open(string filePath, string? password = null)
    {
        ExcelLicense.Ensure();

        if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return CsvWorkbookReader.LoadAsPackage(filePath);

        if (Path.GetExtension(filePath).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            return XlsWorkbookReader.LoadAsPackage(filePath);

        try
        {
            return string.IsNullOrEmpty(password)
                ? new ExcelPackage(new FileInfo(filePath))
                : new ExcelPackage(new FileInfo(filePath), password);
        }
        catch (Exception ex) when (string.IsNullOrEmpty(password))
        {
            // 비밀번호 없이 열다가 실패한 경우에만 "암호가 걸려있을 수 있다"고 간주한다.
            // 비밀번호를 입력했는데도 실패하면 그 오류를 그대로 호출 측에 전달한다.
            throw new EncryptedExcelFileException(filePath, ex);
        }
    }

    /// <summary>
    /// UI 스레드에서 동기적으로 호출하는 진입점. 암호로 보호된 파일이면 비밀번호 입력
    /// 다이얼로그를 띄우고 재시도한다. 사용자가 취소하면 null을 반환한다.
    /// </summary>
    public static ExcelPackage? OpenWithPasswordPrompt(string filePath, IWin32Window owner)
    {
        try
        {
            return Open(filePath);
        }
        catch (EncryptedExcelFileException)
        {
            using var dialog = new PasswordPromptDialog(Path.GetFileName(filePath));
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;

            return Open(filePath, dialog.Password);
        }
    }
}
