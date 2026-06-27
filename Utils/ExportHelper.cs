using System.Diagnostics;
using MiniERP2.Forms;
using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 파일 내보내기와 관련된 공통 기능을 제공합니다.
/// </summary>
public static class ExportHelper
{
    /// <summary>
    /// 파일 내보내기 완료 후 사용자에게 다음 행동을 묻는 다이얼로그를 표시하고,
    /// 선택에 따라 파일 또는 폴더를 엽니다.
    /// </summary>
    /// <param name="owner">다이얼로그를 소유할 부모 폼입니다.</param>
    /// <param name="filePath">내보내기가 완료된 파일의 전체 경로입니다.</param>
    public static void ShowPostExportDialog(IWin32Window owner, string filePath)
    {
        using var dialog = new PostExportDialog(filePath);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = dialog.SelectedAction == PostExportAction.OpenFile ? filePath : Path.GetDirectoryName(filePath)
            };
            Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"요청을 처리하는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 엑셀 파일 저장 실패 시 보여줄 메시지를 만든다. 가장 흔한 원인(저장하려는 파일을 엑셀 등에서
    /// 이미 열어둔 상태)은 EPPlus/내부 IO 예외의 원문 메시지가 영문이라 사용자가 원인을 알기
    /// 어려우므로, Win32 공유 위반 오류코드를 확인해 그 경우만 알아보기 쉬운 한글 안내로 바꾼다.
    /// </summary>
    public static string DescribeSaveError(Exception ex)
    {
        const int ErrorSharingViolation = unchecked((int)0x80070020);
        const int ErrorLockViolation = unchecked((int)0x80070021);

        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is IOException && (current.HResult == ErrorSharingViolation || current.HResult == ErrorLockViolation))
            {
                return "파일이 이미 다른 프로그램(엑셀 등)에서 열려 있어 저장할 수 없습니다.\n파일을 닫고 다시 시도하세요.";
            }
        }

        return ex.Message;
    }
}