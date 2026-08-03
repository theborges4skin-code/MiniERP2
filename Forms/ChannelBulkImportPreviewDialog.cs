using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 채널 일괄등록(엑셀) 기획서 §4.3(미리보기/분류)·§4.5(커밋/롤백). 파일을 즉시 반영하지 않고
/// 신규/수정/변경없음/오류로 분류해 보여준 뒤, 오류가 하나도 없을 때만 커밋을 허용한다.
/// </summary>
public class ChannelBulkImportPreviewDialog : Form
{
    private readonly List<SalesChannel> _existingChannels;
    private readonly List<ChannelConfig> _existingConfigs;
    private readonly List<DocParty> _existingParties;

    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly DocPartyRepository _docPartyRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly DbBackupService _dbBackupService = new();

    private ChannelBulkImportResult? _result;

    private readonly Label _summaryLabel = new() { AutoSize = true, Padding = new Padding(10, 8, 0, 0) };
    private readonly ListBox _fileErrorList = new() { Dock = DockStyle.Top, Height = 60, ForeColor = Color.Firebrick };
    private readonly ExcelLikeDataGridView _channelGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoGenerateColumns = false, PersistenceKey = "ChannelBulkImportPreviewDialog.ChannelGrid" };
    private readonly ListBox _sheetErrorList = new() { Dock = DockStyle.Bottom, Height = 90, ForeColor = Color.Firebrick };
    private readonly Button _btnCommit = new() { Text = "커밋", Width = 100 };

    public ChannelBulkImportPreviewDialog(string filePath, List<SalesChannel> existingChannels, List<ChannelConfig> existingConfigs, List<DocParty> existingParties)
    {
        _existingChannels = existingChannels;
        _existingConfigs = existingConfigs;
        _existingParties = existingParties;

        InitializeComponent();
        LoadFile(filePath);
    }

    private void InitializeComponent()
    {
        Text = "엑셀 일괄 등록 — 미리보기";
        Size = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterParent;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var topPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, AutoSize = true };
        topPanel.Controls.Add(_summaryLabel, 0, 0);
        topPanel.Controls.Add(_fileErrorList, 0, 1);

        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RowNumber", HeaderText = "행", DataPropertyName = "RowNumber", Width = 50 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusLabel", HeaderText = "상태", DataPropertyName = "StatusLabel", Width = 70 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChannelCode", HeaderText = "채널코드", DataPropertyName = "ChannelCode", Width = 80 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChannelName", HeaderText = "채널명", DataPropertyName = "ChannelName", Width = 140 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChannelType", HeaderText = "채널유형", DataPropertyName = "ChannelType", Width = 90 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Issues", HeaderText = "오류/경고", DataPropertyName = "Issues", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _channelGrid.RowPrePaint += OnChannelGridRowPrePaint;

        var bottomPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, AutoSize = true };
        bottomPanel.Controls.Add(new Label { Text = "매핑/거래처정보 시트 오류", AutoSize = true, Padding = new Padding(6, 4, 0, 0) }, 0, 0);
        bottomPanel.Controls.Add(_sheetErrorList, 0, 1);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var btnClose = new Button { Text = "닫기", Width = 100 };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnCommit.Click += OnCommitClick;
        buttonPanel.Controls.Add(btnClose);
        buttonPanel.Controls.Add(_btnCommit);

        outer.Controls.Add(topPanel, 0, 0);
        outer.Controls.Add(_channelGrid, 0, 1);

        var southPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, AutoSize = true };
        southPanel.Controls.Add(bottomPanel, 0, 0);
        southPanel.Controls.Add(buttonPanel, 0, 1);
        outer.Controls.Add(southPanel, 0, 2);

        Controls.Add(outer);
        CancelButton = btnClose;
    }

    private void LoadFile(string filePath)
    {
        try
        {
            _result = new ChannelBulkImportLoader().Load(filePath, _existingChannels, _existingConfigs, _existingParties);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _btnCommit.Enabled = false;
            return;
        }

        _fileErrorList.Items.Clear();
        foreach (var error in _result.FileErrors) _fileErrorList.Items.Add(error);
        _fileErrorList.Visible = _result.FileErrors.Count > 0;

        _sheetErrorList.Items.Clear();
        foreach (var row in _result.MappingRows.Where(r => r.HasErrors))
            _sheetErrorList.Items.Add($"[발주서/정산서매핑 {row.RowNumber}행] {string.Join(" / ", row.Errors)}");
        foreach (var row in _result.PartyRows.Where(r => r.HasErrors))
            _sheetErrorList.Items.Add($"[거래처정보 {row.RowNumber}행] {string.Join(" / ", row.Errors)}");

        _channelGrid.DataSource = new BindingList<ChannelRowViewModel>(_result.ChannelRows.Select(r => new ChannelRowViewModel(r)).ToList());

        _summaryLabel.Text = $"신규 {_result.NewCount}건 / 수정 {_result.UpdateCount}건 / 변경없음 {_result.UnchangedCount}건 / 오류 {_result.ErrorCount}건";
        _btnCommit.Enabled = !_result.HasBlockingErrors;
    }

    private void OnChannelGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (_channelGrid.Rows[e.RowIndex].DataBoundItem is not ChannelRowViewModel vm) return;
        _channelGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor = vm.Row.Status switch
        {
            ChannelImportRowStatus.Error => Color.MistyRose,
            ChannelImportRowStatus.New => Color.Honeydew,
            ChannelImportRowStatus.Update => Color.LightYellow,
            _ => Color.White,
        };
    }

    /// <summary>기획서 §4.5 커밋 순서: DB 스냅샷 → JSON 백업 복사 → 메모리 조립 → 단일 SQLite 트랜잭션 → JSON 저장.
    /// JSON 저장(5단계)이 실패하면 4단계 트랜잭션은 이미 커밋된 상태이므로 스냅샷으로 되돌리고 안내한다.</summary>
    private void OnCommitClick(object? sender, EventArgs e)
    {
        if (_result == null || _result.HasBlockingErrors) return;

        var committable = _result.ChannelRows.Where(r => r.Status != ChannelImportRowStatus.Error).ToList();
        if (committable.Count == 0)
        {
            MessageBox.Show("반영할 변경 내용이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"신규 {_result.NewCount}건, 수정 {_result.UpdateCount}건을 반영합니다.\n(변경없음 {_result.UnchangedCount}건은 그대로 둡니다.)\n계속하시겠습니까?",
            "커밋 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        string backupPath;
        string? jsonBackupPath = null;
        try
        {
            backupPath = _dbBackupService.CreateBackup("채널_일괄등록_전");
            var backupFolder = Path.GetDirectoryName(backupPath)!;
            var jsonPath = PathProvider.ChannelConfigFilePath;
            if (File.Exists(jsonPath))
            {
                jsonBackupPath = Path.Combine(backupFolder, $"channels_config_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Copy(jsonPath, jsonBackupPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"백업 생성 중 오류가 발생해 커밋을 중단합니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var finalChannels = committable.Select(r => r.FinalChannel!).ToList();
        var finalParties = committable.Where(r => r.FinalParty != null).Select(r => r.FinalParty!).ToList();

        var mergedConfigs = new List<ChannelConfig>(_existingConfigs.Where(c => committable.All(r => r.ResolvedChannelCode != c.ChannelCode)));
        mergedConfigs.AddRange(committable.Select(r => r.FinalConfig!));

        try
        {
            using var connection = SqliteConnectionFactory.OpenConnection();
            using var transaction = connection.BeginTransaction();
            _salesChannelRepository.UpsertMany(finalChannels, connection, transaction);
            _docPartyRepository.SaveMany(finalParties, connection, transaction);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"데이터베이스 반영 중 오류가 발생했습니다. 아무 것도 반영되지 않았습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            _channelConfigService.Save(mergedConfigs);
        }
        catch (Exception ex)
        {
            _dbBackupService.Restore(backupPath);
            var jsonNote = jsonBackupPath != null ? $"\n채널 설정 파일 백업: {jsonBackupPath}" : "";
            MessageBox.Show(
                $"채널 설정 파일 저장에 실패해, DB를 커밋 이전 상태로 되돌렸습니다.\n{ExportHelper.DescribeSaveError(ex)}{jsonNote}\n\n앱을 재시작한 뒤 다시 시도하세요.",
                "커밋 실패 — 롤백됨", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show($"신규 {_result.NewCount}건, 수정 {_result.UpdateCount}건을 반영했습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private class ChannelRowViewModel(ChannelImportChannelRow row)
    {
        public ChannelImportChannelRow Row => row;
        public int RowNumber => row.RowNumber;
        public string StatusLabel => row.Status switch
        {
            ChannelImportRowStatus.New => "신규",
            ChannelImportRowStatus.Update => "수정",
            ChannelImportRowStatus.Unchanged => "변경없음",
            ChannelImportRowStatus.Error => "오류",
            _ => "무시",
        };
        public string ChannelCode => row.ResolvedChannelCode ?? "";
        public string ChannelName => row.ChannelName;
        public string ChannelType => row.ResolvedChannelType.ToKoreanLabel();
        public string Issues => string.Join(" / ", row.Errors.Concat(row.Warnings));
    }
}
