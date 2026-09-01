using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6) — 이익분석 내보내기 결과 xlsx
/// 여러 개를 상호명(DocPartyTable.CompanyName) 단위로 재취합하는 화면. 마감/이익분석 창의
/// 계산 로직(SettlementLoader/ProfitCalculator)은 건드리지 않고 그 결과 파일만 다시 읽는다(§1).
/// 지금 단계(S5)는 파일 로드·_META 파싱·거래처 그룹화·CSKU 정규화까지만 다룬다 — 집계(§6.2 이후)는
/// 다음 단계에서 이 파일에 이어 붙인다.
/// </summary>
public class PartnerConsolidationForm : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly DocPartyRepository _docPartyRepository = new();

    private readonly BindingList<PartnerConsolidationFile> _files = [];
    private readonly BindingList<CompanyGroupSummary> _companyGroups = [];

    private ExcelLikeDataGridView _fileGrid = new();
    private ExcelLikeDataGridView _groupGrid = new();
    private Label _statusLabel = new();

    private const string NoCompanyGroupLabel = "(미지정)";

    /// <summary>§6.5 "중단 — 거래처 요약" 이전 단계의 그룹화 미리보기(집계 없음, 건수만).</summary>
    private class CompanyGroupSummary
    {
        public required string CompanyName { get; init; }
        public int FileCount { get; init; }
        public int RowCount { get; init; }
        public int MappedCount { get; init; }
        public int UnmappedCount { get; init; }
        public int CskuUnresolvedCount { get; init; }
        public int ExcludedCount { get; init; }
    }

    public PartnerConsolidationForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
    }

    private void InitializeComponent()
    {
        Text = "온라인 거래처 취합";
        Size = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        // ── 1행: 파일 추가/제거 ──────────────────────────────────────────
        var topPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnAddFiles = new Button { Text = "파일 추가", Size = new Size(90, 30) };
        var btnRemoveFiles = new Button { Text = "선택 파일 제거", Size = new Size(110, 30) };
        var btnReload = new Button { Text = "다시 불러오기", Size = new Size(100, 30) };
        var btnAssignChannel = new Button { Text = "채널 수동 지정...", Size = new Size(120, 30) };
        btnAddFiles.Click += (s, e) => AddFiles();
        btnRemoveFiles.Click += (s, e) => RemoveSelectedFiles();
        btnReload.Click += (s, e) => ReloadAllFiles();
        btnAssignChannel.Click += (s, e) => AssignChannelToSelectedFile();
        topPanel.Controls.Add(btnAddFiles);
        topPanel.Controls.Add(btnRemoveFiles);
        topPanel.Controls.Add(btnReload);
        topPanel.Controls.Add(btnAssignChannel);

        // ── 2행: 파일 목록(§6.5 상단) ────────────────────────────────────
        _fileGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerConsolidationForm.FileGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
            ReadOnly = true,
        };
        _fileGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "파일명", Name = "FileName", DataPropertyName = "FileName", Width = 240 },
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "CompanyName", DataPropertyName = "CompanyNameDisplay", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "채널명", Name = "ChannelName", DataPropertyName = "ChannelNameDisplay", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "행수", Name = "RowCount", DataPropertyName = "RowCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "StatusDisplay", Width = 300 }
        );
        _fileGrid.DataSource = _files;

        var groupLabel = new Label { Dock = DockStyle.Fill, Text = "거래처 그룹화 미리보기(건수만 — 집계는 다음 단계)", Padding = new Padding(6, 4, 0, 0), Font = new Font(Font, FontStyle.Bold) };

        // ── 4행: 거래처 그룹화 미리보기 ──────────────────────────────────
        _groupGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerConsolidationForm.GroupGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = false,
            ReadOnly = true,
        };
        _groupGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "CompanyName", DataPropertyName = "CompanyName", Width = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "파일수", Name = "FileCount", DataPropertyName = "FileCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "행수", Name = "RowCount", DataPropertyName = "RowCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "매핑됨", Name = "MappedCount", DataPropertyName = "MappedCount", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "미매핑", Name = "UnmappedCount", DataPropertyName = "UnmappedCount", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU 미확정", Name = "CskuUnresolvedCount", DataPropertyName = "CskuUnresolvedCount", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "제외", Name = "ExcludedCount", DataPropertyName = "ExcludedCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _groupGrid.DataSource = _companyGroups;

        // ── 5행: 상태표시줄 ──────────────────────────────────────────────
        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "파일을 추가하세요.", Padding = new Padding(6, 4, 0, 0) };

        mainLayout.Controls.Add(topPanel, 0, 0);
        mainLayout.Controls.Add(_fileGrid, 0, 1);
        mainLayout.Controls.Add(groupLabel, 0, 2);
        mainLayout.Controls.Add(_groupGrid, 0, 3);
        mainLayout.Controls.Add(_statusLabel, 0, 4);

        Controls.Add(mainLayout);
    }

    // ── 파일 추가/제거 ──────────────────────────────────────────────────

    private void AddFiles()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            Title = "이익분석 내보내기 결과 파일을 선택하세요 (여러 개 선택 가능)",
            Multiselect = true,
            InitialDirectory = _settingsService.GetLastFolder("PartnerConsolidation") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("PartnerConsolidation", Path.GetDirectoryName(ofd.FileNames[0])!);

        var addedCount = 0;
        foreach (var path in ofd.FileNames)
        {
            if (_files.Any(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue; // 같은 경로는 중복 추가하지 않음(W6은 "같은 채널의 다른 파일"이 대상이지 동일 경로 재추가가 아니다).

            var file = PartnerConsolidationFileLoader.Load(path, _channelSkuRepository);
            _files.Add(file);
            addedCount++;
        }

        RefreshGroupSummary();
        WarnDuplicateChannelFiles();
        _statusLabel.Text = $"파일 {addedCount}개 추가됨. 총 {_files.Count}개 로드됨.";
    }

    private void RemoveSelectedFiles()
    {
        var selected = _fileGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as PartnerConsolidationFile)
            .Where(f => f != null)
            .ToList();
        foreach (var f in selected) _files.Remove(f!);
        RefreshGroupSummary();
    }

    private void ReloadAllFiles()
    {
        var paths = _files.Select(f => f.FilePath).ToList();
        _files.Clear();
        foreach (var path in paths)
            _files.Add(PartnerConsolidationFileLoader.Load(path, _channelSkuRepository));
        RefreshGroupSummary();
        _statusLabel.Text = $"{paths.Count}개 파일을 다시 불러왔습니다.";
    }

    /// <summary>
    /// W4: _META가 없는(구버전) 파일의 채널을 수동으로 지정한다. 지정한 채널의 상호명을 DB에서
    /// 조회해 파일과 그 파일의 모든 행에 채워 넣고, CSKU 정규화를 그 채널 기준으로 다시 수행한다.
    /// </summary>
    private void AssignChannelToSelectedFile()
    {
        var selected = _fileGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as PartnerConsolidationFile)
            .FirstOrDefault(f => f != null);
        if (selected == null)
        {
            MessageBox.Show(this, "채널을 지정할 파일을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SelectChannelDialog();
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.SelectedChannel == null) return;

        var channel = dialog.SelectedChannel;
        var companyName = _docPartyRepository.GetByChannelCode(channel.ChannelCode)?.CompanyName ?? "";

        selected.ChannelCode = channel.ChannelCode;
        selected.ChannelName = channel.ChannelName;
        selected.CompanyName = companyName;

        // 파일 전체를 그 채널 기준으로 다시 읽는다 — 행의 CSKU 정규화가 채널코드에 좌우되므로
        // (수동 지정 전에는 채널을 몰라 원래 행의 '채널' 컬럼 값을 그대로 썼을 수 있다).
        var reloaded = PartnerConsolidationFileLoader.Load(selected.FilePath, _channelSkuRepository);
        reloaded.ChannelCode = channel.ChannelCode;
        reloaded.ChannelName = channel.ChannelName;
        reloaded.CompanyName = companyName;
        foreach (var row in reloaded.Rows)
        {
            row.ChannelName = channel.ChannelName;
            row.CompanyName = companyName;
        }

        var index = _files.IndexOf(selected);
        _files[index] = reloaded;

        RefreshGroupSummary();
        _statusLabel.Text = $"'{reloaded.FileName}'의 채널을 '{channel.ChannelName}'(으)로 지정했습니다.";
    }

    /// <summary>W6: 같은 채널의 파일이 2개 이상 로드되면 기간 중복 가능성을 경고만 한다(제거하지 않음).</summary>
    private void WarnDuplicateChannelFiles()
    {
        var dupChannels = _files
            .Where(f => !string.IsNullOrWhiteSpace(f.ChannelCode))
            .GroupBy(f => f.ChannelCode)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupChannels.Count == 0) return;

        MessageBox.Show(this,
            $"다음 채널의 파일이 2개 이상 로드되었습니다(기간이 겹칠 수 있습니다) — 확인 후 진행하세요:\n{string.Join(", ", dupChannels)}",
            "중복 채널 경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ── 거래처 그룹화 미리보기(§6.1 ②③④ 결과 확인용, 집계는 다음 단계) ──────

    private void RefreshGroupSummary()
    {
        _companyGroups.Clear();
        var groups = _files
            .Where(f => !f.LoadFailed)
            .SelectMany(f => f.Rows, (f, row) => row)
            .GroupBy(row => string.IsNullOrWhiteSpace(row.CompanyName) ? NoCompanyGroupLabel : row.CompanyName);

        foreach (var group in groups.OrderBy(g => g.Key == NoCompanyGroupLabel).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var rows = group.ToList();
            _companyGroups.Add(new CompanyGroupSummary
            {
                CompanyName = group.Key,
                FileCount = rows.Select(r => r.SourceFileName).Distinct().Count(),
                RowCount = rows.Count,
                MappedCount = rows.Count(r => r.Kind == PartnerConsolidationRowKind.Mapped),
                UnmappedCount = rows.Count(r => r.Kind == PartnerConsolidationRowKind.Unmapped),
                CskuUnresolvedCount = rows.Count(r => r.Kind == PartnerConsolidationRowKind.CskuUnresolved),
                ExcludedCount = rows.Count(r => r.Kind == PartnerConsolidationRowKind.Excluded),
            });
        }
    }
}
