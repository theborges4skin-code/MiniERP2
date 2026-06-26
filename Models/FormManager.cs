namespace MiniERP2.UI;

/// <summary>
/// 폼(창)의 생성과 표시를 관리합니다.
/// 기획서 2.7절 '창 중복 실행 방지' 요구사항을 구현합니다.
/// </summary>
public static class FormManager
{
    public static void Show<T>() where T : Form, new()
    {
        // 이미 열려 있는 폼이 있는지 확인합니다.
        var form = Application.OpenForms.OfType<T>().FirstOrDefault();

        if (form is null)
        {
            // 열려있는 폼이 없으면 새로 생성하고 표시합니다.
            form = new T();
            form.Show();
        }
        else
        {
            // 이미 열려있으면 해당 폼을 맨 앞으로 가져옵니다.
            form.BringToFront();
        }
    }
}