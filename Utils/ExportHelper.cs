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
}