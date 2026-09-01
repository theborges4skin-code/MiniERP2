using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>간이 마진 계산기 임시저장(§9) 목록 — 불러오기/삭제. 최근 5개까지만 보관되므로
/// 목록은 항상 짧게 유지된다(<see cref="MarginCalculatorScenarioService"/>).</summary>
public class MarginScenarioPickerDialog : Form
{
    public MarginCalcScenario? SelectedScenario { get; private set; }

    private readonly MarginCalculatorScenarioService _service;
    private List<MarginCalcScenario> _scenarios = new();
    private DataGridView _grid = new();

    public MarginScenarioPickerDialog(MarginCalculatorScenarioService service)
    {
        _service = service;
        InitializeComponent();
        LoadList();
    }

    private void InitializeComponent()
    {
        Text = "임시저장 불러오기";
        Size = new Size(560, 380);
        MinimumSize = new Size(420, 260);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.Columns.Add("Label", "저장 이름");
        _grid.Columns.Add("SavedAt", "저장 시각");
        _grid.Columns.Add("RowCount", "품목수");
        _grid.Columns["SavedAt"]!.Width = 130;
        _grid.Columns["RowCount"]!.Width = 60;
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Confirm(); };

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnClose = new Button { Text = "닫기", Width = 90, DialogResult = DialogResult.Cancel };
        var btnLoad = new Button { Text = "불러오기", Width = 90 };
        var btnDelete = new Button { Text = "삭제", Width = 90 };
        btnLoad.Click += (s, e) => Confirm();
        btnDelete.Click += OnDeleteClick;
        btnPanel.Controls.Add(btnClose);
        btnPanel.Controls.Add(btnLoad);
        btnPanel.Controls.Add(btnDelete);

        layout.Controls.Add(_grid, 0, 0);
        layout.Controls.Add(btnPanel, 0, 1);
        Controls.Add(layout);
        CancelButton = btnClose;
    }

    private void LoadList()
    {
        _scenarios = _service.LoadAll();
        _grid.Rows.Clear();
        foreach (var s in _scenarios)
            _grid.Rows.Add(s.Label, s.SavedAt.ToString("yyyy-MM-dd HH:mm"), s.Rows.Count);
    }

    private void Confirm()
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _scenarios.Count)
        {
            MessageBox.Show("불러올 항목을 선택하세요.", "알림");
            return;
        }
        SelectedScenario = _scenarios[idx];
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _scenarios.Count)
        {
            MessageBox.Show("삭제할 항목을 선택하세요.", "알림");
            return;
        }

        var target = _scenarios[idx];
        if (MessageBox.Show($"'{target.Label}'을(를) 삭제하시겠습니까?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _service.Delete(target.Id);
        LoadList();
    }
}
