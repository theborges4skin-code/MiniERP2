using MiniERP2.Config;
using MiniERP2.Models;

namespace MiniERP2.UI;

/// <summary>
/// 폼(창)의 생성과 표시를 관리합니다.
/// 기획서 2.6절 '창 크기 기억'과 2.7절 '창 중복 실행 방지' 요구사항을 구현합니다.
/// </summary>
public static class FormManager
{
    private static readonly WindowBoundsService WindowBoundsService = new();

    // 매핑관리창처럼 OFS/마감 화면 등에서 "OpenForms에 있으면 그걸, 없으면 new T()"로 직접 생성되는
    // 창은 Show&lt;T&gt;()를 거치지 않아 크기/위치 기억이 전혀 붙지 않았다. 그래서 각 주요 작업창
    // 생성자에서도 ApplyBoundsTracking(this)을 직접 호출하게 했는데, Show&lt;T&gt;() 경로로 열릴 때는
    // 두 번 호출될 수 있으므로(중복 구독 방지) 이미 추적 중인 폼은 건너뛴다.
    private static readonly HashSet<Form> TrackedForms = new();

    public static void Show<T>() where T : Form, new()
    {
        // 이미 열려 있는 폼이 있는지 확인합니다.
        var form = Application.OpenForms.OfType<T>().FirstOrDefault();

        if (form is null)
        {
            // 열려있는 폼이 없으면 새로 생성하고, 마지막 크기/위치를 복원한 뒤 표시합니다.
            form = new T();
            ApplyBoundsTracking(form);
            form.Show();
        }
        else
        {
            // 이미 열려있으면 해당 폼을 맨 앞으로 가져옵니다.
            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }
            form.BringToFront();
        }
    }

    /// <summary>
    /// Form.ShowDialog()를 대신한다. 이 환경에서 모달 창이 WS_VISIBLE 없이 생성되어 화면에
    /// 전혀 나타나지 않으면서(작업 관리자엔 응답함으로 표시) 앱 전체가 무한 대기 상태로 멈추는
    /// 사례가 반복 확인되어(2026-08-03, 마감/이익분석 저장 및 CSKU 마스터SKU 연결), Shown 시점에
    /// Visible을 다시 assert해 이 경쟁 상태를 완화한다.
    /// </summary>
    public static DialogResult ShowDialogSafe(Form dialog, IWin32Window? owner)
    {
        dialog.Shown += (_, _) =>
        {
            if (!dialog.Visible) dialog.Visible = true;
            dialog.Activate();
            dialog.BringToFront();
        };
        return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
    }

    /// <summary>
    /// 폼 하나에 마지막 크기/위치 복원 및 저장 동작을 연결합니다.
    /// FormManager.Show&lt;T&gt;()를 거치지 않고 직접 생성하는 최상위 창(MainHub 등)에도 사용할 수 있습니다.
    /// </summary>
    public static void ApplyBoundsTracking(Form form)
    {
        if (!TrackedForms.Add(form)) return;

        RestoreBounds(form);
        form.FormClosing += (s, e) => SaveBounds(form);
        form.FormClosed += (s, e) => TrackedForms.Remove(form);
    }

    private static void RestoreBounds(Form form)
    {
        var bounds = WindowBoundsService.Get(form.GetType().Name);
        if (bounds == null || bounds.Width <= 0 || bounds.Height <= 0) return;

        // 저장된 위치가 현재 화면 구성에서 벗어나면(모니터 변경 등) 복원하지 않고 기본 위치를 사용한다.
        if (!SystemInformation.VirtualScreen.Contains(new Point(bounds.Left, bounds.Top))) return;

        form.StartPosition = FormStartPosition.Manual;
        form.SetBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

        if (Enum.TryParse<FormWindowState>(bounds.WindowState, out var state) && state == FormWindowState.Maximized)
        {
            form.WindowState = FormWindowState.Maximized;
        }
    }

    private static void SaveBounds(Form form)
    {
        var isNormal = form.WindowState == FormWindowState.Normal;
        var rect = isNormal ? new Rectangle(form.Left, form.Top, form.Width, form.Height) : form.RestoreBounds;

        WindowBoundsService.Save(form.GetType().Name, new WindowBounds
        {
            Left = rect.Left,
            Top = rect.Top,
            Width = rect.Width,
            Height = rect.Height,
            WindowState = form.WindowState.ToString(),
        });
    }
}
