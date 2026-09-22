using MiniERP2.Config;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 메인 허브 메뉴/버튼별 누적 사용 횟수를 보여주는 화면. "자주 쓰는 기능을 즐겨찾기로 강조"하는
/// 작업의 1단계(데이터 수집)로 만든 조회 전용 화면이다 — 이 목록을 보고 실제로 어떤 메뉴를
/// 즐겨찾기/강조할지는 데이터가 어느 정도 쌓인 뒤 사람이 판단해서 정한다.
/// </summary>
public class MenuUsageStatsForm : Form
{
    private readonly MenuUsageLogService _menuUsageLogService = new();
    private DataGridView _grid = new();

    public MenuUsageStatsForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = "메뉴 사용 통계";
        Size = new Size(560, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var topPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(5) };
        var btnRefresh = new Button { Text = "새로고침", Width = 90 };
        btnRefresh.Click += (s, e) => LoadData();
        topPanel.Controls.Add(btnRefresh);
        topPanel.Controls.Add(new Label
        {
            Text = "상단 메뉴/중앙 버튼/검색창으로 각 화면을 연 횟수입니다(§2 자주 쓰는 기능 파악용).",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(10, 10, 0, 0),
        });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "메뉴/화면", Name = "Label", DataPropertyName = "Label", Width = 260 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "사용 횟수",
            Name = "Count",
            DataPropertyName = "Count",
            Width = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "마지막 사용", Name = "LastUsedAt", DataPropertyName = "LastUsedAt", Width = 150 });

        Controls.Add(_grid);
        Controls.Add(topPanel);
    }

    private void LoadData()
    {
        var rows = _menuUsageLogService.GetAll()
            .Select(kv => new MenuUsageRow { Label = kv.Key, Count = kv.Value.Count, LastUsedAt = kv.Value.LastUsedAt })
            .OrderByDescending(r => r.Count)
            .ThenByDescending(r => r.LastUsedAt)
            .ToList();

        _grid.DataSource = rows;
    }

    private class MenuUsageRow
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTime LastUsedAt { get; set; }
    }
}
