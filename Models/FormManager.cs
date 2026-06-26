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
    /// 폼 하나에 마지막 크기/위치 복원 및 저장 동작을 연결합니다.
    /// FormManager.Show&lt;T&gt;()를 거치지 않고 직접 생성하는 최상위 창(MainHub 등)에도 사용할 수 있습니다.
    /// </summary>
    public static void ApplyBoundsTracking(Form form)
    {
        RestoreBounds(form);
        form.FormClosing += (s, e) => SaveBounds(form);
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
