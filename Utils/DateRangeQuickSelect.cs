namespace MiniERP2.Utils;

/// <summary>
/// 기간(시작~종료) 조회 화면 공용 "빠른 선택" 드롭다운. 발주/출고 이력관리(OutboundHistoryForm)에서
/// 처음 만든 오늘/어제/이번달/저번달/이번 분기/지난 분기 6가지 구성을 표준으로 삼아 모든 기간
/// 조회 화면이 이 헬퍼를 통해 동일하게 제공한다. 새 기간 조회 화면을 만들 때도 매번 메뉴를 직접
/// 작성하지 말고 이 헬퍼를 사용한다.
/// </summary>
public static class DateRangeQuickSelect
{
    /// <summary>from/to 피커를 오늘 날짜로 초기화하고, "빠른 선택 ▾" 버튼을 만들어 반환한다.
    /// 반환된 버튼을 툴바에 추가하면 된다.</summary>
    public static Button CreateButton(DateTimePicker fromPicker, DateTimePicker toPicker)
    {
        fromPicker.Value = DateTime.Today;
        toPicker.Value = DateTime.Today;

        var button = new Button { Text = "빠른 선택 ▾", Size = new Size(90, 30) };
        var menu = new ContextMenuStrip();

        menu.Items.Add("오늘", null, (_, _) => SetRange(fromPicker, toPicker, DateTime.Today, DateTime.Today));
        menu.Items.Add("어제", null, (_, _) =>
        {
            var yesterday = DateTime.Today.AddDays(-1);
            SetRange(fromPicker, toPicker, yesterday, yesterday);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("이번달", null, (_, _) =>
        {
            var today = DateTime.Today;
            SetRange(fromPicker, toPicker, new DateTime(today.Year, today.Month, 1), today);
        });
        menu.Items.Add("저번달", null, (_, _) =>
        {
            var prev = DateTime.Today.AddMonths(-1);
            SetRange(fromPicker, toPicker, new DateTime(prev.Year, prev.Month, 1), new DateTime(prev.Year, prev.Month, DateTime.DaysInMonth(prev.Year, prev.Month)));
        });
        menu.Items.Add("이번 분기", null, (_, _) =>
        {
            var today = DateTime.Today;
            var qStart = new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
            SetRange(fromPicker, toPicker, qStart, today);
        });
        menu.Items.Add("지난 분기", null, (_, _) =>
        {
            var today = DateTime.Today;
            var qStart = new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
            SetRange(fromPicker, toPicker, qStart.AddMonths(-3), qStart.AddDays(-1));
        });

        button.ContextMenuStrip = menu;
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        return button;
    }

    /// <summary>표준 6가지 외에 화면별로 필요한 추가 항목(예: 거래명세표 조회의 "전체기간")을
    /// 끝에 덧붙이고 싶을 때 쓴다. 항목 클릭 시 fromPicker/toPicker에 직접 값을 대입하면 된다.</summary>
    public static void AddExtraItem(Button quickSelectButton, string text, EventHandler onClick)
    {
        quickSelectButton.ContextMenuStrip!.Items.Add(text, null, onClick);
    }

    private static void SetRange(DateTimePicker fromPicker, DateTimePicker toPicker, DateTime from, DateTime to)
    {
        fromPicker.Value = from;
        toPicker.Value = to;
    }
}
