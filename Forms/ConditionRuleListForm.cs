using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 노션 5.1 피드백: "조건부 매핑(상세)" 탭에서 항상 떠 있던 좌측 규칙 목록을 빼고, 필요할 때만
/// 이 창에서 전체 목록을 관리하게 한다. 대상SKU+조건이 완전히 같은 규칙은 같은 색으로 강조해
/// 중복을 한눈에 알아볼 수 있게 하고, 그런 행들을 골라 "중복 규칙 병합"으로 하나만 남길 수 있다.
/// 행을 고르고 "이 규칙 편집"을 누르면(또는 더블클릭) 그 규칙 Id를 돌려주고 닫힌다.
/// </summary>
public class ConditionRuleListForm : Form
{
    private readonly MappingRepository _mappingRepository;
    private readonly string _channelCode;
    private ExcelLikeDataGridView _grid = new();
    private List<RuleRow> _rows = [];

    public long? SelectedRuleId { get; private set; }

    public ConditionRuleListForm(MappingRepository mappingRepository, string channelCode)
    {
        _mappingRepository = mappingRepository;
        _channelCode = channelCode;
        InitializeComponent();
        LoadRules();
    }

    private void InitializeComponent()
    {
        Text = "전체 조건부 규칙 관리";
        Size = new Size(860, 560);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var topPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnAdd = new Button { Text = "새 규칙 추가", Size = new Size(100, 28) };
        btnAdd.Click += OnAddRuleClick;
        var btnDelete = new Button { Text = "선택 삭제", Size = new Size(90, 28) };
        btnDelete.Click += OnDeleteRuleClick;
        var btnMerge = new Button { Text = "중복 규칙 병합", Size = new Size(110, 28) };
        btnMerge.Click += OnMergeDuplicatesClick;
        topPanel.Controls.Add(btnAdd);
        topPanel.Controls.Add(btnDelete);
        topPanel.Controls.Add(btnMerge);
        topPanel.Controls.Add(new Label
        {
            Text = "같은 색으로 강조된 행은 대상SKU+조건이 완전히 동일한 중복 규칙입니다.",
            AutoSize = true,
            Padding = new Padding(15, 7, 0, 0),
            ForeColor = Color.DimGray,
        });

        _grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "키(요약)", DataPropertyName = "Key", Width = 180 },
            new DataGridViewTextBoxColumn { Name = "TargetSku", HeaderText = "대상 SKU", DataPropertyName = "TargetSku", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "ConditionSummary", HeaderText = "조건 요약", DataPropertyName = "ConditionSummary", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "DuplicateGroup", HeaderText = "중복그룹", DataPropertyName = "DuplicateGroupLabel", Width = 70, ReadOnly = true }
        );
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) PickCurrentRuleAndClose(); };
        _grid.RowPrePaint += OnGridRowPrePaint;

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        var btnClose = new Button { Text = "닫기", Size = new Size(80, 28) };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnEdit = new Button { Text = "이 규칙 편집", Size = new Size(100, 28) };
        btnEdit.Click += (s, e) => PickCurrentRuleAndClose();
        bottomPanel.Controls.Add(btnClose);
        bottomPanel.Controls.Add(btnEdit);

        layout.Controls.Add(topPanel, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(bottomPanel, 0, 2);
        Controls.Add(layout);
        CancelButton = btnClose;
    }

    private class RuleRow
    {
        public long Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string TargetSku { get; set; } = string.Empty;
        public string ConditionSummary { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public int DuplicateGroup { get; set; }
        public string DuplicateGroupLabel => DuplicateGroup > 0 ? DuplicateGroup.ToString() : string.Empty;
    }

    /// <summary>
    /// 노션 5.1: "대상 SKU가 같으면서 상세 조건이 전부 중복되는 경우"를 중복으로 본다. 조건
    /// 순서가 달라도 같은 집합이면 같은 중복으로 보도록 정렬해서 시그니처를 만든다(예: A AND B와
    /// B AND A는 의미상 같음). Logic까지 포함하므로 OR/AND가 다르면 다른 규칙으로 본다.
    /// </summary>
    private void LoadRules()
    {
        var rules = _mappingRepository.GetRules(MappingRuleType.Condition, _channelCode);
        // 규칙마다 GetConditionDetails(ruleId)로 따로 조회하면 그때마다 새 SQLite 연결을 열어서
        // 규칙이 많을수록(특히 병합 후 다시 그릴 때) "병합 누르면 멈춘 것처럼 보임" 신고와 같은
        // 원인으로 느려진다(ReapplyMappingForAllRows에서 고친 것과 동일한 N+1 패턴). 채널 전체
        // 상세조건을 한 번에 가져오는 GetConditionDetailsByChannel로 대체한다.
        var detailsByRuleId = _mappingRepository.GetConditionDetailsByChannel(_channelCode);
        var rows = new List<RuleRow>();
        foreach (var rule in rules)
        {
            var details = detailsByRuleId.GetValueOrDefault(rule.Id, []);
            var conditionSummary = string.Join(" / ", details.Select(d => $"{d.HeaderField} {d.Operator} '{d.TargetValue}'({d.Logic})"));
            var signature = ConditionRuleSignature.Build(rule.TargetSku, details);

            rows.Add(new RuleRow { Id = rule.Id, Key = rule.Key, TargetSku = rule.TargetSku, ConditionSummary = conditionSummary, Signature = signature });
        }

        var groupIndex = 0;
        foreach (var group in rows.GroupBy(r => r.Signature))
        {
            if (group.Count() < 2) continue;
            groupIndex++;
            foreach (var row in group) row.DuplicateGroup = groupIndex;
        }

        _rows = rows;
        _grid.DataSource = new BindingList<RuleRow>(rows);
    }

    private static readonly Color[] DuplicateGroupColors =
    [
        Color.MistyRose, Color.Honeydew, Color.LightYellow, Color.Lavender, Color.LightCyan, Color.Bisque,
    ];

    private void OnGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.DataBoundItem is not RuleRow data) return;

        if (data.DuplicateGroup > 0)
        {
            row.DefaultCellStyle.BackColor = DuplicateGroupColors[(data.DuplicateGroup - 1) % DuplicateGroupColors.Length];
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else
        {
            row.DefaultCellStyle.BackColor = _grid.DefaultCellStyle.BackColor;
            row.DefaultCellStyle.ForeColor = _grid.DefaultCellStyle.ForeColor;
        }
    }

    private void PickCurrentRuleAndClose()
    {
        if (_grid.CurrentRow?.DataBoundItem is not RuleRow row) return;
        SelectedRuleId = row.Id;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnAddRuleClick(object? sender, EventArgs e)
    {
        var newRuleId = _mappingRepository.AddConditionRuleWithDetails(_channelCode, "새 조건부 규칙", string.Empty, []);
        SelectedRuleId = newRuleId;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnDeleteRuleClick(object? sender, EventArgs e)
    {
        var selectedIds = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as RuleRow)
            .Where(r => r != null)
            .Select(r => r!.Id)
            .Distinct()
            .ToList();
        if (selectedIds.Count == 0)
        {
            MessageBox.Show("삭제할 규칙을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show($"{selectedIds.Count}건의 조건부 규칙을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        foreach (var id in selectedIds) _mappingRepository.DeleteConditionRule(id);
        LoadRules();
    }

    /// <summary>같은 중복그룹(같은 색)으로 강조된 행 2개 이상을 골라야 병합할 수 있다. 가장 작은 Id(먼저 만든 규칙)만 남기고 나머지는 삭제한다.</summary>
    private void OnMergeDuplicatesClick(object? sender, EventArgs e)
    {
        var selectedRows = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as RuleRow)
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();

        if (selectedRows.Count < 2)
        {
            MessageBox.Show("병합할 중복 규칙(같은 색으로 강조된 행) 2개 이상을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (selectedRows.Select(r => r.Signature).Distinct().Count() > 1)
        {
            MessageBox.Show("선택한 규칙들의 대상SKU/조건이 서로 달라 병합할 수 없습니다. 완전히 동일한(같은 색) 규칙만 함께 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var keep = selectedRows.OrderBy(r => r.Id).First();
        var toDelete = selectedRows.Where(r => r.Id != keep.Id).ToList();
        if (MessageBox.Show($"{selectedRows.Count}건을 1건으로 병합합니다('{keep.Key}'만 남고 나머지 {toDelete.Count}건은 삭제됩니다). 계속하시겠습니까?", "병합 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        foreach (var row in toDelete) _mappingRepository.DeleteConditionRule(row.Id);
        LoadRules();
    }
}
