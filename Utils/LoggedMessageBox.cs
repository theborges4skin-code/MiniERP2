namespace MiniERP2.Utils;

/// <summary>
/// MessageBox.Show와 똑같이 동작하되, 띄운 메시지와 사용자의 응답을 진단 로그(diagnostics.log)에도 남긴다.
/// 2026-10-21 OFS 택배양식 출력 중 뜬 메시지가 무엇이었는지 나중에 확인할 길이 없었던 일이 있어,
/// 화면에만 뜨고 사라지는 안내·경고·오류를 기록으로 남기기 위해 만들었다(우선 OFS 화면에 적용).
/// </summary>
public static class LoggedMessageBox
{
    public static DialogResult Show(string text) => Show(null, text, string.Empty, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(string text, string caption) => Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons) => Show(null, text, caption, buttons, MessageBoxIcon.None);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(null, text, caption, buttons, icon);

    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        var result = owner is null ? MessageBox.Show(text, caption, buttons, icon) : MessageBox.Show(owner, text, caption, buttons, icon);
        var kind = icon switch
        {
            MessageBoxIcon.Error => "오류",
            MessageBoxIcon.Warning => "경고",
            MessageBoxIcon.Question => "질문",
            _ => "안내",
        };
        var oneLine = text.Replace("\r\n", " / ").Replace('\n', ' ').Replace('\r', ' ');
        var answer = buttons == MessageBoxButtons.OK ? "" : $" → 응답 {result}";
        DiagnosticsLogger.Log($"[메시지·{kind}] {caption}: {oneLine}{answer}");
        return result;
    }
}
