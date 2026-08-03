using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 월별 마감 자동화 메인 UI.
///
/// 흐름:
///   1. 폴더 + 기간 지정 → 스캔 → 파일 목록 + 채널 자동탐지
///   2. 미탐지 파일에 채널 수동 지정
///   3. 처리 시작 → ClosingOrchestrator 파이프라인
///   4. 미매핑 큐 → UnmappedQueueForm
/// </summary>
public class MonthlyClosingForm : Form
{
    private readonly ClosingOrchestrator _orchestrator = new();
    private readonly ClosingRunRepository _runRepo = new();
    private readonly ChannelConfigService _configService = new();

    private List<ChannelConfig> _channelConfigs = [];
    private List<ClosingStagedFile> _stagedFiles = [];
    private long _currentRunId = -1;
    private CancellationTokenSource? _cts;

    private TextBox _folderBox = new();
    private TextBox _periodBox = new();
    private DataGridView _filesGrid = new();
    private RichTextBox _logBox = new();
    private Button _scanBtn = new();
    private Button _processBtn = new();
    private Button _unmappedBtn = new();
    private Label _statusLabel = new();

    private const int ColFileName = 0;
    private const int ColChannel = 1;
    private const int ColSourceType = 2;
    private const int ColStatus = 3;
    private const int ColRows = 4;
    private const int ColUnmapped = 5;
    private const int ColError = 6;

    public MonthlyClosingForm()
    {
        _channelConfigs = _configService.Load();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "월별 마감 자동화";
        Size = new Size(1100, 750);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 550);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        outer.Controls.Add(BuildToolbar(), 0, 0);
        outer.Controls.Add(BuildBody(), 0, 1);
        outer.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(outer);
    }

    // ─── 상단 툴바 ───────────────────────────────────────────────────────────

    private Panel BuildToolbar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 4) };

        var folderLabel = new Label { Text = "폴더:", AutoSize = true, Top = 14, Left = 0 };
        _folderBox = new TextBox { Width = 380, Top = 10, Left = 38, ReadOnly = true };

        var browseBtn = new Button { Text = "찾아보기", Width = 70, Top = 9, Left = 424 };
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "정산 파일 폴더 선택" };
            if (dlg.ShowDialog() == DialogResult.OK)
                _folderBox.Text = dlg.SelectedPath;
        };

        var periodLabel = new Label { Text = "기간:", AutoSize = true, Top = 14, Left = 504 };
        _periodBox = new TextBox
        {
            Width = 80, Top = 10, Left = 538,
            PlaceholderText = "YYYY-MM"
        };
        _periodBox.Text = DateTime.Today.AddMonths(-1).ToString("yyyy-MM");

        _scanBtn = new Button { Text = "스캔", Width = 70, Top = 9, Left = 628 };
        _scanBtn.Click += OnScanClick;

        _statusLabel = new Label
        {
            AutoSize = true, Top = 14, Left = 710,
            ForeColor = Color.Gray
        };

        panel.Controls.AddRange([folderLabel, _folderBox, browseBtn, periodLabel, _periodBox, _scanBtn, _statusLabel]);
        return panel;
    }

    // ─── 중단 본문 (그리드 + 로그) ───────────────────────────────────────────

    private Control BuildBody()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 400,
        };

        _filesGrid = BuildFilesGrid();
        split.Panel1.Controls.Add(_filesGrid);

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGray,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        split.Panel2.Controls.Add(_logBox);

        return split;
    }

    private DataGridView BuildFilesGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        };

        // 파일명
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "파일명", HeaderText = "파일명", Width = 260, ReadOnly = true,
        });

        // 채널 (ComboBox)
        var channelItems = new List<object> { new { ChannelCode = "", ChannelName = "(미지정)" } };
        channelItems.AddRange(_channelConfigs.Cast<object>());
        var channelCol = new DataGridViewComboBoxColumn
        {
            Name = "채널", HeaderText = "채널", Width = 160,
            DataSource = _channelConfigs.Select(c => c.ChannelName).Prepend("(미지정)").ToList(),
        };
        grid.Columns.Add(channelCol);

        // 소스유형
        var srcCol = new DataGridViewComboBoxColumn
        {
            Name = "소스유형", HeaderText = "소스유형", Width = 90,
            DataSource = new List<string> { "settlement", "ad" },
        };
        grid.Columns.Add(srcCol);

        // 상태, 행수, 미매핑, 오류
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "상태", HeaderText = "상태", Width = 80, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "행수", HeaderText = "행수", Width = 60, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "미매핑", HeaderText = "미매핑", Width = 60, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "오류", HeaderText = "오류", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });

        grid.CellValidating += OnGridCellValidating;
        grid.CellValueChanged += OnGridCellValueChanged;

        return grid;
    }

    // ─── 하단 버튼 바 ────────────────────────────────────────────────────────

    private Panel BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };

        _processBtn = new Button
        {
            Text = "처리 시작", Width = 90, Top = 4, Left = 0, Enabled = false
        };
        _processBtn.Click += OnProcessClick;

        _unmappedBtn = new Button
        {
            Text = "미매핑 큐", Width = 90, Top = 4, Left = 100, Enabled = false
        };
        _unmappedBtn.Click += OnUnmappedClick;

        var cancelBtn = new Button { Text = "중단", Width = 70, Top = 4, Left = 200 };
        cancelBtn.Click += (_, _) => _cts?.Cancel();

        var closeBtn = new Button { Text = "닫기", Width = 70, Top = 4, Left = 280 };
        closeBtn.Click += (_, _) => Close();

        panel.Controls.AddRange([_processBtn, _unmappedBtn, cancelBtn, closeBtn]);
        return panel;
    }

    // ─── 스캔 ────────────────────────────────────────────────────────────────

    private void OnScanClick(object? s, EventArgs e)
    {
        var folder = _folderBox.Text.Trim();
        var period = _periodBox.Text.Trim();

        if (!Directory.Exists(folder))
        {
            MessageBox.Show("폴더가 존재하지 않습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(period, @"^\d{4}-\d{2}$"))
        {
            MessageBox.Show("기간 형식이 올바르지 않습니다 (YYYY-MM).", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _channelConfigs = _configService.Load();
        var scanned = _orchestrator.ScanFolder(folder);

        if (scanned.Count == 0)
        {
            SetStatus("지원 파일이 없습니다 (.xlsx/.xls/.csv)");
            return;
        }

        // 새 ClosingRun 생성
        _currentRunId = _runRepo.CreateRun(folder, period);
        _stagedFiles = [];

        // ClosingStagedFile 생성 및 DB 저장
        foreach (var sf in scanned)
        {
            var channelCode = sf.DetectedChannelCode ?? "";
            var cfg = _channelConfigs.FirstOrDefault(c => c.ChannelCode == channelCode);
            var staged = new ClosingStagedFile
            {
                RunId = _currentRunId,
                ChannelCode = channelCode,
                ChannelName = cfg?.ChannelName ?? "",
                SourceType = sf.Meta?.SourceType ?? "settlement",
                OriginalPath = sf.FilePath,
                FileCreatedAt = sf.Meta?.FileCreatedAt ?? "",
                Status = "pending",
            };
            staged.Id = _runRepo.UpsertStagedFile(staged);
            _stagedFiles.Add(staged);
        }

        PopulateGrid();
        _processBtn.Enabled = _stagedFiles.Count > 0;
        SetStatus($"파일 {scanned.Count}개 감지됨. 채널을 확인 후 처리 시작을 누르세요.");
    }

    private void PopulateGrid()
    {
        _filesGrid.Rows.Clear();

        foreach (var staged in _stagedFiles)
        {
            var channelDisplay = string.IsNullOrEmpty(staged.ChannelName) ? "(미지정)" : staged.ChannelName;
            _filesGrid.Rows.Add(
                staged.FileName,
                channelDisplay,
                staged.SourceType,
                staged.Status,
                staged.RowCount > 0 ? staged.RowCount.ToString() : "",
                staged.UnmappedCount > 0 ? staged.UnmappedCount.ToString() : "",
                staged.ErrorMessage ?? ""
            );
        }
    }

    private void RefreshGridRow(int rowIndex, ClosingStagedFile staged)
    {
        if (rowIndex < 0 || rowIndex >= _filesGrid.Rows.Count) return;
        var row = _filesGrid.Rows[rowIndex];
        row.Cells[ColStatus].Value = staged.Status;
        row.Cells[ColRows].Value = staged.RowCount > 0 ? staged.RowCount.ToString() : "";
        row.Cells[ColUnmapped].Value = staged.UnmappedCount > 0 ? staged.UnmappedCount.ToString() : "";
        row.Cells[ColError].Value = staged.ErrorMessage ?? "";

        // 상태별 행 색상
        row.DefaultCellStyle.BackColor = staged.Status switch
        {
            "processed" => Color.FromArgb(220, 255, 220),
            "error" => Color.FromArgb(255, 220, 220),
            "skipped" => Color.FromArgb(240, 240, 240),
            _ => Color.White,
        };
    }

    // ─── 그리드 편집 ─────────────────────────────────────────────────────────

    private void OnGridCellValidating(object? s, DataGridViewCellValidatingEventArgs e)
    {
        // ComboBox 열은 목록 내 값만 허용
        if (e.ColumnIndex != ColChannel && e.ColumnIndex != ColSourceType) return;
        if (_filesGrid.Rows[e.RowIndex].IsNewRow) return;
    }

    private void OnGridCellValueChanged(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _stagedFiles.Count) return;
        var staged = _stagedFiles[e.RowIndex];

        if (e.ColumnIndex == ColChannel)
        {
            var name = _filesGrid.Rows[e.RowIndex].Cells[ColChannel].Value as string ?? "";
            var cfg = _channelConfigs.FirstOrDefault(c => c.ChannelName == name);
            staged.ChannelCode = cfg?.ChannelCode ?? "";
            staged.ChannelName = cfg?.ChannelName ?? "";
        }
        else if (e.ColumnIndex == ColSourceType)
        {
            staged.SourceType = _filesGrid.Rows[e.RowIndex].Cells[ColSourceType].Value as string ?? "settlement";
        }
    }

    // ─── 처리 시작 ───────────────────────────────────────────────────────────

    private async void OnProcessClick(object? s, EventArgs e)
    {
        if (_currentRunId < 0 || _stagedFiles.Count == 0) return;

        var period = _periodBox.Text.Trim();

        // 그리드 변경사항을 DB에 반영
        CommitGridChanges();

        _processBtn.Enabled = false;
        _scanBtn.Enabled = false;
        _cts = new CancellationTokenSource();
        _logBox.Clear();

        var progress = new Progress<ClosingOrchestrator.ProcessProgress>(p =>
        {
            var color = p.IsError ? Color.Salmon : Color.LightGreen;
            AppendLog($"[{p.FileName}] {p.ChannelName} — {p.Message}", color);

            // 처리 완료된 파일 행 갱신
            var idx = _stagedFiles.FindIndex(f => f.FileName == p.FileName);
            if (idx >= 0) RefreshGridRow(idx, _stagedFiles[idx]);
        });

        try
        {
            SetStatus("처리 중...");
            await _orchestrator.ProcessRunAsync(_currentRunId, _stagedFiles, period, progress, _cts.Token);

            var unmappedCount = _runRepo.GetUnmappedCount(_currentRunId);
            _unmappedBtn.Enabled = unmappedCount > 0;
            SetStatus($"완료. 미매핑 항목: {unmappedCount}건");
            AppendLog($"처리 완료. 미매핑 항목: {unmappedCount}건", Color.Cyan);
        }
        catch (OperationCanceledException)
        {
            SetStatus("중단됨.");
            AppendLog("처리가 중단되었습니다.", Color.Orange);
        }
        catch (Exception ex)
        {
            SetStatus("오류 발생.");
            AppendLog($"처리 오류: {ex.Message}", Color.Salmon);
        }
        finally
        {
            _processBtn.Enabled = true;
            _scanBtn.Enabled = true;
        }
    }

    private void CommitGridChanges()
    {
        _filesGrid.EndEdit();
        foreach (var staged in _stagedFiles)
            _runRepo.UpdateStagedFile(staged);
    }

    // ─── 미매핑 큐 ───────────────────────────────────────────────────────────

    private void OnUnmappedClick(object? s, EventArgs e)
    {
        if (_currentRunId < 0) return;

        using var form = new UnmappedQueueForm(_currentRunId, _periodBox.Text.Trim(), _orchestrator);
        FormManager.ShowDialogSafe(form, this);

        // 재계산 후 그리드 갱신
        var updated = _runRepo.GetStagedFiles(_currentRunId);
        for (int i = 0; i < Math.Min(updated.Count, _stagedFiles.Count); i++)
        {
            _stagedFiles[i] = updated[i];
            RefreshGridRow(i, _stagedFiles[i]);
        }

        _unmappedBtn.Enabled = _runRepo.GetUnmappedCount(_currentRunId) > 0;
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────────────

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void AppendLog(string text, Color? color = null)
    {
        if (_logBox.InvokeRequired)
        {
            _logBox.Invoke(() => AppendLog(text, color));
            return;
        }
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color ?? Color.LightGray;
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        _logBox.ScrollToCaret();
    }
}
