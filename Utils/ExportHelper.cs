using System.Diagnostics;
using MiniERP2.Forms;
using MiniERP2.Models;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// 파일 내보내기와 관련된 공통 기능을 제공합니다.
/// </summary>
public static class ExportHelper
{
    /// <summary>
    /// EPPlus 패키지를 지정한 경로에 저장합니다.
    /// EPPlus의 SaveAs(FileInfo)는 내부에서 File.Delete + FileMode.CreateNew 를 사용하는데,
    /// 파일이 다른 프로세스(예: Excel)에서 열려 있으면 NTFS 지연 삭제(pending-delete) 때문에
    /// CreateNew 가 무한 대기 상태에 빠집니다. FileMode.Create 로 직접 스트림을 열면 잠긴
    /// 경우 즉시 IOException(공유 위반)을 던져 <see cref="DescribeSaveError"/>로 처리됩니다.
    /// </summary>
    public static void SaveExcel(ExcelPackage package, string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        package.SaveAs(fs);
    }

    /// <summary>
    /// SaveFileDialog를 표시하되, OS 기본 "덮어쓰기 확인" 팝업(OverwritePrompt) 대신 직접 구현한
    /// MessageBox로 확인한다. 환경에 따라 SaveFileDialog 내장 덮어쓰기 확인 하위 대화상자가
    /// WS_VISIBLE 없이 생성되어 화면에 전혀 나타나지 않으면서 앱 전체가 무한 대기 상태로 멈추는
    /// 사례가 발견되어(2026-08-03, 마감/이익분석 내보내기), OS 팝업에 의존하지 않도록 한다.
    /// </summary>
    /// <returns>저장할 경로. 사용자가 취소했으면 null.</returns>
    public static string? ShowSaveFileDialog(IWin32Window owner, string filter, string defaultFileName, string initialDirectory, string? title = null)
    {
        while (true)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = filter,
                FileName = defaultFileName,
                InitialDirectory = initialDirectory,
                OverwritePrompt = false,
                Title = title ?? "다른 이름으로 저장",
            };

            if (sfd.ShowDialog(owner) != DialogResult.OK) return null;

            if (File.Exists(sfd.FileName))
            {
                var result = MessageBox.Show(owner,
                    $"'{Path.GetFileName(sfd.FileName)}' 파일이 이미 있습니다. 덮어쓰시겠습니까?",
                    "다른 이름으로 저장 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) continue;
            }

            return sfd.FileName;
        }
    }

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