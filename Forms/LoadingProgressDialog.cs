namespace MiniERP2.Forms;

/// <summary>
/// 시간이 걸리는 파일 로드 작업 중에 띄워두는 비모달(모달이 아닌) 진행 안내 창입니다. 작업이
/// async/await로 백그라운드에서 진행되는 동안 화면이 멈춘 것처럼 보이지 않도록, 메시지와
/// 진행률(또는 움직이는 막대)을 보여줍니다. 호출 측이 작업 단계마다 SetIndeterminate/SetProgress로
/// 갱신하고, 끝나면 Close()로 닫습니다.
/// </summary>
public class LoadingProgressDialog : Form
{
    private readonly Label _messageLabel = new();
    private readonly ProgressBar _progressBar = new();

    public LoadingProgressDialog(string initialMessage)
    {
        Text = "처리 중";
        Size = new Size(420, 130);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(15) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));

        _messageLabel.Text = initialMessage;
        _messageLabel.Dock = DockStyle.Fill;
        _messageLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_messageLabel, 0, 0);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 30;
        layout.Controls.Add(_progressBar, 0, 1);

        Controls.Add(layout);
    }

    /// <summary>전체 단계 수를 알 수 없을 때(파일 1개만 처리 등) 움직이는 막대로 "작업 중"을 표시한다.</summary>
    public void SetIndeterminate(string message)
    {
        _messageLabel.Text = message;
        if (_progressBar.Style != ProgressBarStyle.Marquee)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 30;
        }
        Refresh();
    }

    /// <summary>전체 단계 수를 알 때(여러 파일을 순서대로 처리 등) 진행률 막대로 표시한다.</summary>
    public void SetProgress(string message, int current, int total)
    {
        _messageLabel.Text = message;
        if (_progressBar.Style != ProgressBarStyle.Blocks)
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.MarqueeAnimationSpeed = 0;
        }
        _progressBar.Minimum = 0;
        _progressBar.Maximum = Math.Max(total, 1);
        _progressBar.Value = Math.Clamp(current, 0, _progressBar.Maximum);
        Refresh();
    }
}
