using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class ExportHelperTests
{
    [TestMethod]
    public void DescribeSaveError_FileLockedBySharingViolation_ReturnsFriendlyKoreanMessage()
    {
        // 저장하려는 엑셀 파일을 이미 엑셀 등에서 열어둔 상태일 때 .NET이 던지는 전형적인 예외
        // (ERROR_SHARING_VIOLATION = 0x80070020)를 시뮬레이션한다.
        var ex = new IOException("The process cannot access the file because it is being used by another process.");
        SetHResult(ex, unchecked((int)0x80070020));

        var message = ExportHelper.DescribeSaveError(ex);

        Assert.Contains("열려 있어 저장할 수 없습니다", message);
    }

    [TestMethod]
    public void DescribeSaveError_WrappedInnerException_StillDetectsSharingViolation()
    {
        var inner = new IOException("locked");
        SetHResult(inner, unchecked((int)0x80070021));
        var outer = new InvalidOperationException("Error saving file", inner);

        var message = ExportHelper.DescribeSaveError(outer);

        Assert.Contains("열려 있어 저장할 수 없습니다", message);
    }

    [TestMethod]
    public void DescribeSaveError_UnrelatedException_ReturnsOriginalMessage()
    {
        var ex = new InvalidOperationException("뭔가 다른 오류");

        var message = ExportHelper.DescribeSaveError(ex);

        Assert.AreEqual("뭔가 다른 오류", message);
    }

    private static void SetHResult(Exception ex, int hResult)
    {
        typeof(Exception).GetProperty(nameof(Exception.HResult))!.SetValue(ex, hResult);
    }
}
