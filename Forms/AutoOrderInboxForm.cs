using System.ComponentModel;
using System.Security.Cryptography;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 자동발주처리(Gmail 자동화) 알림 목록창(02_자동발주처리_MiniERP2연동_설계.md §5–6). Drive에서
/// 감지된 항목을 보여주고, [다운로드&amp;저장]/[발주 파일 로드로 열기]/[무시]로 상태를 전이시킨다.
/// </summary>
public class AutoOrderInboxForm : Form
{
    private readonly AutoOrderInboxRepository _inboxRepository = new();
    private readonly AutoOrderSettingsService _settingsService = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly DataLoaders.OrderLoader _orderLoader = new();

    private DataGridView _grid = new();
    private CheckBox _showHiddenCheckBox = new();
    private Label _statusLabel = new();

    public AutoOrderInboxForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        Load += (s, e) => RefreshGrid();
    }

    private void InitializeComponent()
    {
        Text = "자동발주처리";
        Size = new Size(900, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnRefresh = new Button { Text = "지금 확인", Size = new Size(90, 30) };
        var btnDownload = new Button { Text = "다운로드&저장", Size = new Size(110, 30) };
        var btnImport = new Button { Text = "발주 파일 로드로 열기", Size = new Size(160, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnDismiss = new Button { Text = "무시", Size = new Size(80, 30) };
        var btnSettings = new Button { Text = "연동 설정", Size = new Size(90, 30) };

        btnRefresh.Click += OnRefreshClick;
        btnDownload.Click += OnDownloadClick;
        btnImport.Click += OnImportClick;
        btnDismiss.Click += OnDismissClick;
        btnSettings.Click += OnSettingsClick;

        _showHiddenCheckBox = new CheckBox { Text = "완료/무시 항목도 표시", AutoSize = true, Padding = new Padding(10, 6, 0, 0) };
        _showHiddenCheckBox.CheckedChanged += (s, e) => RefreshGrid();

        toolStrip.Controls.Add(btnRefresh);
        toolStrip.Controls.Add(btnDownload);
        toolStrip.Controls.Add(btnImport);
        toolStrip.Controls.Add(btnDismiss);
        toolStrip.Controls.Add(btnSettings);
        toolStrip.Controls.Add(_showHiddenCheckBox);

        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "제목", Name = "SubjectSnip", DataPropertyName = "SubjectSnip", Width = 300 },
            new DataGridViewTextBoxColumn { HeaderText = "수신시각", Name = "ReceivedAt", DataPropertyName = "ReceivedAt", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "행수", Name = "RowCount", DataPropertyName = "RowCount", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "검증", Name = "ParseStatus", DataPropertyName = "ParseStatus", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "Status", Width = 90 }
        );
        _grid.CellFormatting += OnGridCellFormatting;

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(mainLayout);
    }

    /// <summary>ParseStatus가 partial/failed인 행은 경고색으로 눈에 띄게 한다(§6).</summary>
    private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not AutoOrderInboxItem item) return;
        if (item.ParseStatus is "partial" or "failed")
        {
            _grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Color.OrangeRed;
        }
    }

    private void RefreshGrid()
    {
        var all = _inboxRepository.GetAll();
        var visible = _showHiddenCheckBox.Checked
            ? all
            : all.Where(i => i.Status is not ("imported" or "dismissed")).ToList();

        _grid.DataSource = new BindingList<AutoOrderInboxItem>(visible);
        _statusLabel.Text = $"신규 {all.Count(i => i.Status == "new")}건 · 전체 {all.Count}건";
    }

    private AutoOrderInboxItem? GetSelected() =>
        _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].DataBoundItem as AutoOrderInboxItem : null;

    private async void OnRefreshClick(object? sender, EventArgs e)
    {
        var settings = _settingsService.Load();
        if (!settings.IsConfigured)
        {
            MessageBox.Show("먼저 [연동 설정]에서 클라이언트 ID/보안 비밀·pending 폴더 ID를 입력하세요.", "설정 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = "Drive에서 확인하는 중...";
        try
        {
            var client = new GoogleDriveAutoOrderClient(_settingsService);
            var pollingService = new AutoOrderPollingService(client, _inboxRepository);
            var newCount = await pollingService.PollAsync(allowInteractiveAuth: true);
            RefreshGrid();
            _statusLabel.Text = newCount > 0 ? $"신규 {newCount}건을 확인했습니다." : "신규 항목이 없습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"확인 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "확인 중 오류가 발생했습니다.";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var dialog = new AutoOrderSettingsDialog();
        FormManager.ShowDialogSafe(dialog, this);
    }

    private async void OnDownloadClick(object? sender, EventArgs e)
    {
        var item = GetSelected();
        if (item == null)
        {
            MessageBox.Show("다운로드할 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await DownloadAsync(item);
        RefreshGrid();
    }

    /// <summary>
    /// 아직 다운로드 전이면 Drive에서 받아 sha256을 대조하고(§5 — 불일치 시 거부) 로컬에 저장한다.
    /// 이미 다운로드된 항목(LocalFilePath 존재)은 재사용한다. 성공 시 로컬 경로를 반환.
    /// </summary>
    private async Task<string?> DownloadAsync(AutoOrderInboxItem item)
    {
        if (item.Status != "new" && !string.IsNullOrEmpty(item.LocalFilePath) && File.Exists(item.LocalFilePath))
        {
            return item.LocalFilePath;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var client = new GoogleDriveAutoOrderClient(_settingsService);
            if (!client.HasCachedAuthorization())
            {
                await client.AuthorizeAsync();
            }

            var fileName = Path.GetFileName(item.XlsxPath);
            var bytes = await client.DownloadFileAsync(fileName);
            if (bytes == null)
            {
                MessageBox.Show($"Drive에서 '{fileName}' 파일을 찾지 못했습니다.", "다운로드 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            if (!string.IsNullOrEmpty(item.Sha256))
            {
                var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(actualHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "다운로드한 파일의 무결성 검사에 실패했습니다(sha256 불일치). 변조되었거나 부분 업로드된 파일일 수 있어 저장을 거부합니다.",
                        "무결성 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
            }

            var downloadFolder = Path.Combine(PathProvider.AppDataFolder, "autoorder_downloads");
            Directory.CreateDirectory(downloadFolder);
            var localPath = Path.Combine(downloadFolder, fileName);
            await File.WriteAllBytesAsync(localPath, bytes);

            _inboxRepository.MarkDownloaded(item.Id, localPath);
            return localPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"다운로드 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// [발주 파일 로드로 열기] — 아직 안 받았으면 먼저 받고, "자동발주(표준)" 프리셋으로 파싱한 뒤
    /// 채널힌트를 실채널로 해석해 SKU 매핑까지 적용하고, OFS 창에 그대로 얹는다(02번 설계 §1).
    /// </summary>
    private async void OnImportClick(object? sender, EventArgs e)
    {
        var item = GetSelected();
        if (item == null)
        {
            MessageBox.Show("불러올 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var channelConfigs = _channelConfigService.Load();
        var preset = AutoOrderChannelResolver.FindStandardPreset(channelConfigs);
        if (preset == null)
        {
            MessageBox.Show(
                "\"자동발주(표준)\" 파싱 프리셋으로 지정된 채널이 없습니다.\n" +
                "채널설정 창에서 채널 하나를 추가/선택한 뒤 \"자동발주 연동\" 탭의 \"자동발주(표준) 파싱 프리셋으로 사용\"을 체크하고, " +
                "발주서 매핑 탭에서 자동발주 표준 열(채널힌트|상품명|옵션명|수량|수취인|연락처|주소|발주번호|비고|발주일)을 매핑하세요.",
                "프리셋 미설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var localPath = await DownloadAsync(item);
        if (localPath == null) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var presetMapper = new Mapping.SkuMapper(_mappingRepository, preset.ChannelCode, _channelSkuRepository);
            var items = await _orderLoader.LoadFromFileAsync(presetMapper, preset, localPath);

            var resolvedCount = AutoOrderChannelResolver.ApplyChannelOverrides(items, channelConfigs, _mappingRepository, _channelSkuRepository);

            var ofsForm = Application.OpenForms.OfType<OfsForm>().FirstOrDefault();
            if (ofsForm == null)
            {
                FormManager.Show<OfsForm>();
                ofsForm = Application.OpenForms.OfType<OfsForm>().First();
            }
            else
            {
                ofsForm.BringToFront();
            }
            ofsForm.AddLoadedOrders(items);

            _inboxRepository.MarkImported(item.Id);
            RefreshGrid();

            var unresolvedCount = items.Count - resolvedCount;
            _statusLabel.Text = unresolvedCount > 0
                ? $"{items.Count}건을 불러왔습니다(채널힌트 미해석 {unresolvedCount}건 — OFS에서 채널 확인 필요)."
                : $"{items.Count}건을 불러와 채널·SKU 매핑까지 적용했습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"발주 파일 로드 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnDismissClick(object? sender, EventArgs e)
    {
        var item = GetSelected();
        if (item == null)
        {
            MessageBox.Show("무시할 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _inboxRepository.MarkDismissed(item.Id);
        RefreshGrid();
    }
}
