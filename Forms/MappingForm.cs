using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.3절 '매핑관리창'
/// </summary>
public class MappingForm : Form
{
    private readonly MappingRepository _mappingRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();

    private ComboBox _channelComboBox = new();
    private TabControl _ruleTabControl = new();
    private readonly HashSet<TabPage> _dirtyTabs = new();
    private readonly Dictionary<MappingRuleType, HashSet<string>> _conflictingKeysByType = new();
    private DataGridView _conflictGrid = new();

    public MappingForm()
    {
        InitializeComponent();
        LoadChannels();
    }

    private void InitializeComponent()
    {
        Text = "매핑 관리";
        Size = new Size(1024, 768);

        // Enable drag-and-drop functionality
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topPanel = CreateTopPanel();
        _ruleTabControl = CreateRuleTabControl();

        mainLayout.Controls.Add(topPanel, 0, 0);
        mainLayout.Controls.Add(_ruleTabControl, 0, 1);
        Controls.Add(mainLayout);

        FormClosing += OnFormClosing;
    }

    private Control CreateTopPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var channelLabel = new Label { Text = "채널:", Anchor = AnchorStyles.Left, AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
        _channelComboBox = new ComboBox { Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _channelComboBox.SelectedIndexChanged += (s, e) => LoadRulesForSelectedChannel();

        var btnSave = new Button { Text = "저장", Size = new Size(100, 30) };
        btnSave.Click += OnSaveClick;

        panel.Controls.Add(channelLabel);
        panel.Controls.Add(_channelComboBox);
        panel.Controls.Add(btnSave);

        return panel;
    }

    private TabControl CreateRuleTabControl()
    {
        var tabControl = new TabControl { Dock = DockStyle.Fill };

        tabControl.TabPages.Add(CreateRuleTabPage("예외 처리", MappingRuleType.Exception));
        tabControl.TabPages.Add(CreateRuleTabPage("1:1 매핑", MappingRuleType.Exact));
        tabControl.TabPages.Add(CreateRuleTabPage("임시 매핑", MappingRuleType.Temp));
        tabControl.TabPages.Add(CreateRuleTabPage("조건부 매핑", MappingRuleType.Condition));
        tabControl.TabPages.Add(CreateConflictTabPage());

        tabControl.Selecting += OnTabSelecting;

        return tabControl;
    }

    private TabPage CreateConflictTabPage()
    {
        var tabPage = new TabPage("충돌 감지");

        _conflictGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.MistyRose },
        };
        _conflictGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "RuleTypeName", HeaderText = "규칙 유형", DataPropertyName = "RuleTypeName", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "KeyA", HeaderText = "키 A", DataPropertyName = "KeyA", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "TargetSkuA", HeaderText = "SKU A", DataPropertyName = "TargetSkuA", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "KeyB", HeaderText = "키 B", DataPropertyName = "KeyB", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "TargetSkuB", HeaderText = "SKU B", DataPropertyName = "TargetSkuB", Width = 120 }
        );

        tabPage.Controls.Add(_conflictGrid);
        return tabPage;
    }

    private TabPage CreateRuleTabPage(string title, MappingRuleType ruleType)
    {
        var tabPage = new TabPage(title);
        var grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = $"MappingForm.{ruleType}Grid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            Tag = ruleType // Store the rule type in the Tag for later use
        };

        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "매칭 키 (상품명/옵션명 등)", DataPropertyName = "Key", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "TargetSku", HeaderText = "매핑할 SKU", DataPropertyName = "TargetSku", Width = 250 }
        );

        // 데이터 변경 시 'dirty' 상태로 만들기 위한 이벤트 핸들러 연결
        grid.CellValueChanged += (s, e) => { MarkTabAsDirty(tabPage); RefreshConflicts(); };
        grid.RowsAdded += (s, e) => { MarkTabAsDirty(tabPage); RefreshConflicts(); };
        grid.RowsRemoved += (s, e) => { MarkTabAsDirty(tabPage); RefreshConflicts(); };

        // 충돌 규칙이 있는 행을 강조 표시
        grid.RowPrePaint += (s, e) => OnRuleGridRowPrePaint(grid, ruleType, e);

        tabPage.Controls.Add(grid);
        return tabPage;
    }

    private void OnRuleGridRowPrePaint(ExcelLikeDataGridView grid, MappingRuleType ruleType, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count || grid.Rows[e.RowIndex].IsNewRow) return;

        var row = grid.Rows[e.RowIndex];
        if (row.DataBoundItem is not MappingRule rule) return;

        var isConflicting = _conflictingKeysByType.TryGetValue(ruleType, out var keys) && keys.Contains(rule.Key);
        row.DefaultCellStyle.BackColor = isConflicting ? Color.MistyRose : grid.DefaultCellStyle.BackColor;
    }

    /// <summary>
    /// 현재 채널에 로드된 4종 규칙 전체를 대상으로 충돌을 다시 감지하고,
    /// 규칙 그리드 강조 및 '충돌 감지' 탭 요약을 갱신합니다.
    /// </summary>
    private void RefreshConflicts()
    {
        _conflictingKeysByType.Clear();
        var allConflicts = new List<MappingConflict>();

        foreach (TabPage tabPage in _ruleTabControl.TabPages)
        {
            if (tabPage.Controls.Count == 0 || tabPage.Controls[0] is not ExcelLikeDataGridView grid || grid.Tag is not MappingRuleType ruleType) continue;

            var rules = (grid.DataSource as BindingList<MappingRule>)?.ToList() ?? new List<MappingRule>();
            var conflicts = MappingConflictDetector.Detect(ruleType, rules);
            if (conflicts.Count > 0)
            {
                _conflictingKeysByType[ruleType] = MappingConflictDetector.GetConflictingKeys(conflicts);
                allConflicts.AddRange(conflicts);
            }

            grid.Invalidate();
        }

        _conflictGrid.DataSource = new BindingList<ConflictRow>(allConflicts.Select(c => new ConflictRow(c)).ToList());
    }

    private record ConflictRow(MappingConflict Conflict)
    {
        public string RuleTypeName => Conflict.RuleType switch
        {
            MappingRuleType.Exception => "예외 처리",
            MappingRuleType.Exact => "1:1 매핑",
            MappingRuleType.Temp => "임시 매핑",
            MappingRuleType.Condition => "조건부 매핑",
            _ => Conflict.RuleType.ToString(),
        };
        public string KeyA => Conflict.KeyA;
        public string TargetSkuA => Conflict.TargetSkuA;
        public string KeyB => Conflict.KeyB;
        public string TargetSkuB => Conflict.TargetSkuB;
    }

    private void LoadChannels()
    {
        var channels = _salesChannelRepository.GetAll();
        _channelComboBox.DataSource = channels;
        _channelComboBox.DisplayMember = "ChannelName";
        _channelComboBox.ValueMember = "ChannelCode";
    }

    /// <summary>
    /// 지정된 채널 코드를 콤보박스에서 선택합니다. OFS에서 미매핑건이 발견되었을 때
    /// 해당 채널의 매핑 규칙을 바로 보여주기 위해 사용합니다.
    /// </summary>
    public void SelectChannelByCode(string channelCode)
    {
        _channelComboBox.SelectedValue = channelCode;
    }

    private async void LoadRulesForSelectedChannel()
    {
        // Use SelectedValue which corresponds to ValueMember ("ChannelCode")
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel)) return;

        // 채널 변경 전, 저장되지 않은 변경사항이 있는지 비동기적으로 확인
        if (!await PromptToSaveChanges()) return;

        // 채널 변경 시, dirty 상태 초기화
        _dirtyTabs.Clear();

        foreach (TabPage tabPage in _ruleTabControl.TabPages)
        {
            if (tabPage.Controls[0] is not ExcelLikeDataGridView grid || grid.Tag is not MappingRuleType ruleType) continue;

            var rules = _mappingRepository.GetRules(ruleType, selectedChannel);
            grid.DataSource = new BindingList<MappingRule>(rules);
        }

        RefreshConflicts();
    }

    private async void OnSaveClick(object? sender, EventArgs e)
    {
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel))
        {
            MessageBox.Show("저장할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_dirtyTabs.Count == 0)
        {
            MessageBox.Show("변경된 내용이 없어 저장할 항목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var dirtyTabsToSave = _dirtyTabs.ToList();
        int savedTabsCount = 0;

        // UI를 대기 상태로 변경
        Cursor = Cursors.WaitCursor;
        Enabled = false;

        try
        {
            // DB 작업을 백그라운드 스레드에서 실행
            await Task.Run(() =>
            {
                foreach (var dirtyTab in dirtyTabsToSave)
                {
                    if (dirtyTab.Controls[0] is not ExcelLikeDataGridView grid || grid.Tag is not MappingRuleType ruleType) continue;

                    var rules = (grid.DataSource as BindingList<MappingRule>)?.ToList() ?? new List<MappingRule>();
                    _mappingRepository.SaveRules(ruleType, selectedChannel, rules);
                    savedTabsCount++;
                }
            });

            _dirtyTabs.Clear();
            MessageBox.Show($"'{selectedChannel}' 채널의 변경된 {savedTabsCount}개 탭의 규칙이 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            // UI 상태 복원
            Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length == 1 && Path.GetExtension(files[0]).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel))
        {
            MessageBox.Show("먼저 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var activeTab = _ruleTabControl.SelectedTab;
        if (activeTab?.Tag is not MappingRuleType ruleType)
        {
            MessageBox.Show("규칙을 적용할 유효한 탭이 선택되지 않았습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var files = (string[])e.Data!.GetData(DataFormats.FileDrop)!;
        var filePath = files[0];

        try
        {
            using var package = ExcelFileOpener.OpenWithPasswordPrompt(filePath, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                MessageBox.Show("엑셀 파일에 워크시트가 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var rulesToImport = new List<MappingRule>();
            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var key = worksheet.Cells[row, 1].Value?.ToString();
                if (string.IsNullOrWhiteSpace(key)) continue;

                var targetSku = worksheet.Cells[row, 2].Value?.ToString() ?? string.Empty;

                rulesToImport.Add(new MappingRule { Key = key, TargetSku = targetSku });
            }

            if (rulesToImport.Count == 0)
            {
                MessageBox.Show("가져올 유효한 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"'{activeTab.Text}' 탭에 {rulesToImport.Count}개의 규칙을 읽었습니다.\n기존 규칙을 모두 덮어쓰고 데이터베이스에 반영하시겠습니까?",
                "가져오기 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _mappingRepository.SaveRules(ruleType, selectedChannel, rulesToImport);
                MessageBox.Show("데이터를 성공적으로 반영했습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // 그리드 데이터를 새로고침하고 dirty 상태로 표시
                if (activeTab.Controls[0] is ExcelLikeDataGridView grid)
                {
                    grid.DataSource = new BindingList<MappingRule>(rulesToImport);
                    MarkTabAsDirty(activeTab);
                    RefreshConflicts();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void MarkTabAsDirty(TabPage tabPage)
    {
        _dirtyTabs.Add(tabPage);
    }

    private async void OnTabSelecting(object? sender, TabControlCancelEventArgs e)
    {
        // 탭을 변경하기 전에 저장되지 않은 변경사항이 있는지 확인
        if (!await PromptToSaveChanges())
        {
            // 사용자가 '취소'를 선택하면 탭 변경을 막음
            e.Cancel = true;
        }
    }

    /// <summary>
    /// 사용자에게 변경사항을 저장할지 묻고, 그 결과에 따라 후속 조치를 진행할지 여부를 반환합니다.
    /// </summary>
    /// <returns>후속 조치(탭 변경, 채널 변경 등)를 계속 진행하면 true, 중단하면 false를 반환합니다.</returns>
    private async Task<bool> PromptToSaveChanges()
    {
        if (_dirtyTabs.Count == 0)
        {
            return true; // 변경사항이 없으므로 계속 진행
        }

        var result = MessageBox.Show(
            "변경 내용이 있습니다. 저장하시겠습니까?",
            "저장 확인",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        switch (result)
        {
            case DialogResult.Yes:
                await Task.Run(() => Invoke(new Action(() => OnSaveClick(null, EventArgs.Empty))));
                return _dirtyTabs.Count == 0; // 저장이 성공적으로 완료되었는지(dirty flag가 해제되었는지) 확인
            case DialogResult.No:
                return true; // 변경사항을 무시하고 계속 진행
            case DialogResult.Cancel:
            default:
                return false; // 작업을 취소하고 계속 진행하지 않음
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!await PromptToSaveChanges())
        {
            e.Cancel = true;
        }
    }
}