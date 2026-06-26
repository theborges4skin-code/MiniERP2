using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 2.2절 '엑셀 내보내기 후 처리' 요구사항을 구현하는 공통 다이얼로그입니다.
/// 이 폼은 ExportHelper를 통해 사용하는 것을 권장합니다.
/// </summary>
public class PostExportDialog : Form
{
    public PostExportAction SelectedAction { get; private set; } = PostExportAction.Close;

    public PostExportDialog(string filePath)
    {
        InitializeComponent(filePath);
    }

    private void InitializeComponent(string filePath)
    {
        Text = "내보내기 완료";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(400, 180);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));

        var messageLabel = new Label
        {
            Text = $"엑셀 파일이 성공적으로 저장되었습니다.\n\n위치: {filePath}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };

        var btnClose = new Button { Text = "닫기", Size = new Size(100, 30) };
        var btnOpenFolder = new Button { Text = "폴더 열기", Size = new Size(100, 30) };
        var btnOpenFile = new Button { Text = "파일 열기", Size = new Size(100, 30) };

        btnClose.Click += (s, e) =>
        {
            SelectedAction = PostExportAction.Close;
            DialogResult = DialogResult.Cancel;
            Close();
        };

        btnOpenFolder.Click += (s, e) =>
        {
            SelectedAction = PostExportAction.OpenFolder;
            DialogResult = DialogResult.OK;
            Close();
        };

        btnOpenFile.Click += (s, e) =>
        {
            SelectedAction = PostExportAction.OpenFile;
            DialogResult = DialogResult.OK;
            Close();
        };

        buttonPanel.Controls.Add(btnClose);
        buttonPanel.Controls.Add(btnOpenFolder);
        buttonPanel.Controls.Add(btnOpenFile);

        mainLayout.Controls.Add(messageLabel, 0, 0);
        mainLayout.Controls.Add(buttonPanel, 0, 1);

        Controls.Add(mainLayout);

        AcceptButton = btnOpenFile;
        CancelButton = btnClose;
    }
}