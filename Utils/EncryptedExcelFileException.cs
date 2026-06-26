namespace MiniERP2.Utils;

/// <summary>
/// 엑셀 파일을 암호 없이 열다가 실패했을 때 던집니다. 호출 측(UI)에서 이 예외를 잡아
/// 비밀번호 입력 다이얼로그를 띄우고 재시도하는 데 사용합니다.
/// </summary>
public class EncryptedExcelFileException(string filePath, Exception innerException)
    : Exception($"'{Path.GetFileName(filePath)}' 파일이 암호로 보호되어 있을 수 있습니다.", innerException)
{
    public string FilePath { get; } = filePath;
}
