using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Forms;
using MiniERP2.Models;
using MiniERP2.UI;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// "운송장 파일 누락건 점검"의 전체 흐름(택배사 선택 → 운송장 결과 파일 선택 → 파싱 → DB 대조 →
/// TrackingBackfillViewer 열기)을 한 곳에 모았다. 원래 발주/출고 이력 관리창(OutboundHistoryForm)
/// 안에서만 시작할 수 있었는데, 메인 허브에서 바로 실행할 수 있게 뽑아냈다 — 로직은 완전히 같고
/// 여러 창에서 재사용할 수 있게 owner/상태 콜백만 매개변수로 받는다.
/// </summary>
public static class TrackingBackfillCheckFlow
{
    /// <param name="owner">파일 선택/택배사 선택 대화상자의 부모 창.</param>
    /// <param name="onStatus">완료 후 요약 문구를 받을 콜백(상태 표시줄이 있는 창에서만 지정).</param>
    public static void Run(IWin32Window owner, Action<string>? onStatus = null)
    {
        var settingsService = new SettingsService();
        var outboundRepository = new OutboundRepository();

        using var courierDialog = new SelectCourierDialog();
        if (FormManager.ShowDialogSafe(courierDialog, owner) != DialogResult.OK || courierDialog.SelectedCourier is not { } courier)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(courier.TrackingImportRecipientHeader) || string.IsNullOrWhiteSpace(courier.TrackingImportTrackingNoHeader))
        {
            MessageBox.Show(
                $"'{courier.CourierName}'의 운송장 결과 가져오기 양식이 설정되지 않았습니다.\n택배사 양식 관리 창에서 수령인/운송장번호 헤더를 먼저 지정하세요.",
                "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "운송장 결과 파일을 선택하세요(여러 개 선택 가능)",
            Multiselect = true,
            InitialDirectory = settingsService.GetLastFolder("TrackingBackfillCheck") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(owner) != DialogResult.OK || ofd.FileNames.Length == 0) return;

        try
        {
            settingsService.SetLastFolder("TrackingBackfillCheck", Path.GetDirectoryName(ofd.FileNames[0])!);

            var allRows = new List<TrackingBackfillRow>();
            var fileErrors = new List<string>();

            foreach (var fileName in ofd.FileNames)
            {
                using var package = Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    ? CsvWorkbookReader.LoadAsPackage(fileName)
                    : ExcelFileOpener.OpenWithPasswordPrompt(fileName, owner);
                if (package == null) continue; // 암호 입력 취소 등 — 그 파일만 건너뛴다.

                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                {
                    fileErrors.Add($"{Path.GetFileName(fileName)}: 엑셀 파일에서 시트를 찾을 수 없습니다.");
                    continue;
                }

                var parseResult = TrackingBackfillFileParser.Parse(worksheet, courier, Path.GetFileName(fileName));
                if (parseResult.Error != null)
                {
                    fileErrors.Add($"{Path.GetFileName(fileName)}: {parseResult.Error}");
                    continue;
                }

                allRows.AddRange(parseResult.Rows);
            }

            if (fileErrors.Count > 0)
            {
                MessageBox.Show(string.Join("\n\n", fileErrors), "일부 파일을 읽지 못했습니다", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (allRows.Count == 0)
            {
                MessageBox.Show("파일에서 운송장번호가 있는 행을 찾지 못했습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var existing = outboundRepository.GetExistingTrackingNos(allRows.Select(r => r.TrackingNo));
            foreach (var row in allRows) row.IsRegistered = existing.Contains(row.TrackingNo);

            var missingCount = allRows.Count(r => !r.IsRegistered);
            var fileWord = ofd.FileNames.Length > 1 ? $"파일 {ofd.FileNames.Length}개 " : "";
            onStatus?.Invoke($"운송장 {fileWord}{allRows.Count}건 중 미등록(누락 후보) {missingCount}건 — 뷰어 창에서 확인하세요.");

            var viewer = Application.OpenForms.OfType<TrackingBackfillViewer>().FirstOrDefault();
            if (viewer == null)
            {
                viewer = new TrackingBackfillViewer();
                viewer.Show();
            }
            else if (viewer.WindowState == FormWindowState.Minimized)
            {
                viewer.WindowState = FormWindowState.Normal;
            }
            viewer.LoadRows(allRows);
            viewer.BringToFront();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
