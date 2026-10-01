using System.ComponentModel;
using System.Globalization;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 출고 보충(후처리). 휴가·출장 등으로 OFS를 거치지 않고 처리된 거래처 발주를, 운송장 결과 파일
/// (실제 출고) + 발주서 폴더(이메일 자동분류로 누적되는 원본 발주서)로 찾아 출고이력에 출고확정으로
/// 보충한다. 거래처 마감자료 없이도 실행할 수 있다 — 우리가 먼저 마감자료를 보내야 하는 경우를 위해.
/// 판정 로직은 <see cref="PartnerShipmentBackfillEngine"/>.
/// </summary>
public class PartnerShipmentBackfillForm : Form
{
    private const string OrderFolderKeyPrefix = "PartnerShipmentBackfill.OrderFolder.";
    private const string DefaultOrderFolderRoot = @"G:\내 드라이브\발주수집";

    private readonly SettingsService _settings = new();
    private readonly SalesChannelRepository _channelRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly OutboundRepository _outboundRepo = new();
    private readonly CourierRepository _courierRepo = new();
    private readonly ChannelConfigService _channelConfigService = new();

    private readonly ComboBox _channelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _periodCombo = new() { Width = 90 };
    private readonly ComboBox _courierCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Label _courierFilesLabel = new() { AutoSize = true, Text = "운송장 파일: (없음)", Padding = new Padding(4, 6, 10, 0) };
    private readonly TextBox _orderFolderBox = new() { Width = 300 };
    private readonly ExcelLikeDataGridView _candidateGrid = new();
    private readonly ExcelLikeDataGridView _unshippedGrid = new();
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    private string[] _courierFiles = [];
    private readonly BindingList<BackfillCandidate> _candidates = [];

    public PartnerShipmentBackfillForm(string? channelCode = null, string? period = null)
    {
        Text = "거래처 출고 보충(운송장·발주서 기준)";
        Size = new Size(1300, 760);
        StartPosition = FormStartPosition.CenterParent;
        BuildLayout();
        LoadCombos(channelCode, period);
    }

    private SalesChannel? CurrentChannel => _channelCombo.SelectedItem as SalesChannel;
    private string CurrentPeriod => _periodCombo.Text.Trim();

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        Label L(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(8, 6, 2, 0) };

        var row1 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        row1.Controls.Add(L("채널:"));
        row1.Controls.Add(_channelCombo);
        row1.Controls.Add(L("마감월:"));
        row1.Controls.Add(_periodCombo);
        row1.Controls.Add(L("택배사:"));
        row1.Controls.Add(_courierCombo);
        var pickFiles = new Button { Text = "운송장 결과 파일...", AutoSize = true };
        pickFiles.Click += (s, e) => PickCourierFiles();
        row1.Controls.Add(pickFiles);
        row1.Controls.Add(_courierFilesLabel);
        root.Controls.Add(row1);

        var row2 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        row2.Controls.Add(L("발주서 폴더:"));
        row2.Controls.Add(_orderFolderBox);
        var pickFolder = new Button { Text = "폴더...", AutoSize = true };
        pickFolder.Click += (s, e) => PickOrderFolder();
        row2.Controls.Add(pickFolder);
        var analyze = new Button { Text = "분석", Width = 80, Font = new Font(Font, FontStyle.Bold) };
        analyze.Click += async (s, e) => await AnalyzeAsync();
        row2.Controls.Add(analyze);
        root.Controls.Add(row2);

        BuildCandidateGrid();
        BuildUnshippedGrid();
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var candTab = new TabPage("보충 후보(운송장 있음·출고이력 없음)");
        candTab.Controls.Add(_candidateGrid);
        var unshippedTab = new TabPage("발주서만 있음(출고 확인 필요)");
        unshippedTab.Controls.Add(_unshippedGrid);
        tabs.TabPages.AddRange([candTab, unshippedTab]);
        root.Controls.Add(tabs);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var assign = new Button { Text = "선택 행 CSKU 지정", AutoSize = true };
        assign.Click += (s, e) => AssignCsku();
        var register = new Button { Text = "체크한 건 출고 등록", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        register.Click += (s, e) => RegisterChecked();
        buttons.Controls.AddRange([assign, register]);
        root.Controls.Add(buttons);
        root.Controls.Add(_statusLabel);

        Controls.Add(root);
    }

    private void BuildCandidateGrid()
    {
        _candidateGrid.Dock = DockStyle.Fill;
        _candidateGrid.AutoGenerateColumns = false;
        _candidateGrid.AllowUserToAddRows = false;
        _candidateGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        DataGridViewTextBoxColumn T(string prop, string header, int width, bool readOnly = true, string? format = null) => new()
        {
            DataPropertyName = prop, Name = prop, HeaderText = header, Width = width, ReadOnly = readOnly,
            DefaultCellStyle = format == null ? new DataGridViewCellStyle()
                : new DataGridViewCellStyle { Format = format, Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        _candidateGrid.Columns.AddRange(
            new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", Name = "Selected", HeaderText = "등록", Width = 40 },
            T("ReceivedAtText", "접수일(=출고일)", 90),
            T("TrackingNo", "운송장번호", 115),
            T("Recipient", "수령인", 70),
            T("MatchKind", "판정", 150),
            T("OrderDateText", "발주일", 85),
            T("OrderProductName", "발주서 품목", 240),
            T("CourierProductName", "운송장 품목명", 240),
            T("Qty", "수량", 45, readOnly: false, format: "N0"),
            T("CskuCode", "CSKU", 150),
            T("SupplyPrice", "납품가", 70, readOnly: false, format: "N0"));
        _candidateGrid.DataSource = _candidates;
        _candidateGrid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex >= 0 && _candidateGrid.Rows[e.RowIndex].DataBoundItem is BackfillCandidate c && c.NeedsCsku)
                e.CellStyle!.ForeColor = Color.DarkOrange;
        };
    }

    private void BuildUnshippedGrid()
    {
        _unshippedGrid.Dock = DockStyle.Fill;
        _unshippedGrid.AutoGenerateColumns = false;
        _unshippedGrid.AllowUserToAddRows = false;
        _unshippedGrid.ReadOnly = true;
        _unshippedGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "OrderDate", HeaderText = "발주일", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Recipient", HeaderText = "수령인", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "Product", HeaderText = "품목", Width = 350 },
            new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "수량", Width = 50 },
            new DataGridViewTextBoxColumn { Name = "Csku", HeaderText = "매핑 CSKU", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "File", HeaderText = "발주서 파일", Width = 250 });
    }

    private void LoadCombos(string? channelCode, string? period)
    {
        var channels = _channelRepo.GetAll().OrderBy(c => c.ChannelName, StringComparer.CurrentCulture).ToList();
        _channelCombo.DisplayMember = nameof(SalesChannel.ChannelName);
        _channelCombo.DataSource = channels;
        var target = channels.FirstOrDefault(c => c.ChannelCode == channelCode);
        if (target != null) _channelCombo.SelectedItem = target;
        _channelCombo.SelectedIndexChanged += (s, e) => LoadOrderFolder();

        for (int i = 0; i < 12; i++) _periodCombo.Items.Add(DateTime.Now.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture));
        _periodCombo.Text = period ?? DateTime.Now.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var couriers = _courierRepo.GetAll();
        _courierCombo.DisplayMember = nameof(CourierMaster.CourierName);
        _courierCombo.DataSource = couriers;
        var cj = couriers.FirstOrDefault(c => c.CourierName.Contains("CJ", StringComparison.OrdinalIgnoreCase));
        if (cj != null) _courierCombo.SelectedItem = cj;

        LoadOrderFolder();
    }

    /// <summary>채널별로 기억한 발주서 폴더. 처음이면 '발주수집\{채널명}' 폴더가 있을 때 그걸 제안한다.</summary>
    private void LoadOrderFolder()
    {
        if (CurrentChannel is not { } ch) return;
        var saved = _settings.GetLastFolder(OrderFolderKeyPrefix + ch.ChannelCode);
        if (!string.IsNullOrWhiteSpace(saved)) { _orderFolderBox.Text = saved; return; }
        var guess = Path.Combine(DefaultOrderFolderRoot, PartnerShipmentBackfillEngine.ChannelHint(ch.ChannelName));
        _orderFolderBox.Text = Directory.Exists(guess) ? guess : "";
    }

    private void PickCourierFiles()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*",
            Title = "운송장 결과(이력 조회) 파일을 선택하세요(여러 개 가능)",
            Multiselect = true,
            InitialDirectory = _settings.GetLastFolder("TrackingBackfillCheck") ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settings.SetLastFolder("TrackingBackfillCheck", Path.GetDirectoryName(ofd.FileNames[0])!);
        _courierFiles = ofd.FileNames;
        _courierFilesLabel.Text = $"운송장 파일: {_courierFiles.Length}개 ({string.Join(", ", _courierFiles.Select(Path.GetFileName))})";
    }

    private void PickOrderFolder()
    {
        using var fbd = new FolderBrowserDialog { SelectedPath = _orderFolderBox.Text };
        if (fbd.ShowDialog(this) == DialogResult.OK) _orderFolderBox.Text = fbd.SelectedPath;
    }

    private async Task AnalyzeAsync()
    {
        if (CurrentChannel is not { } channel) return;
        if (!DateTime.TryParseExact(CurrentPeriod, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            MessageBox.Show(this, "마감월 형식이 올바르지 않습니다(YYYY-MM).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_courierFiles.Length == 0 || _courierCombo.SelectedItem is not CourierMaster courier)
        {
            MessageBox.Show(this, "운송장 결과 파일과 택배사를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var folder = _orderFolderBox.Text.Trim();
        if (folder.Length > 0) _settings.SetLastFolder(OrderFolderKeyPrefix + channel.ChannelCode, folder);

        UseWaitCursor = true;
        _statusLabel.Text = "분석 중...";
        try
        {
            var courierRows = ReadCourierRows(courier, out var courierErrors);
            var (orders, orderFileCount, orderErrors) = await LoadOrdersAsync(channel, folder);

            var (from, to) = PartnerShipmentBackfillEngine.PeriodRange(CurrentPeriod);
            var registered = _outboundRepo.GetByTrackingNos(courierRows.Select(r => r.TrackingNo));
            var history = _outboundRepo.GetByChannel(channel.ChannelCode, from.AddDays(-45), to.AddDays(45));
            var cskus = _channelSkuRepo.GetAllByChannel(channel.ChannelCode);
            var known = cskus.Where(c => !string.IsNullOrWhiteSpace(c.InvoiceDisplayName)).Select(c => (c.InvoiceDisplayName!, c.CskuCode))
                .Concat(_outboundRepo.GetByChannel(channel.ChannelCode, from.AddYears(-1), to)
                    .Select(d => (d.ProductName, string.IsNullOrWhiteSpace(d.CskuCode) ? d.MskuCode : d.CskuCode!)))
                .Where(k => !string.IsNullOrWhiteSpace(k.Item1) && !string.IsNullOrWhiteSpace(k.Item2))
                .Distinct()
                .ToList();
            var priceByCsku = cskus.ToDictionary(c => c.CskuCode, c => c.SupplyPrice, StringComparer.Ordinal);

            var analysis = PartnerShipmentBackfillEngine.Analyze(CurrentPeriod, channel.ChannelCode, channel.ChannelName, courierRows, orders,
                registered, history, known, code => priceByCsku.GetValueOrDefault(code));

            _candidates.Clear();
            foreach (var c in analysis.Candidates) _candidates.Add(c);
            _unshippedGrid.Rows.Clear();
            foreach (var o in analysis.UnshippedOrders)
                _unshippedGrid.Rows.Add(o.OrderDate.ToString("yyyy-MM-dd"), o.Recipient,
                    string.IsNullOrWhiteSpace(o.OptionName) ? o.ProductName : $"{o.ProductName} / {o.OptionName}", o.Qty, o.MappedCsku ?? "", o.SourceFileName);

            var errors = courierErrors.Concat(orderErrors).ToList();
            _statusLabel.Text = $"{CurrentPeriod} 운송장 {analysis.CourierRowsInPeriod}건(미등록 {analysis.UnregisteredInPeriod}) · 발주서 {orderFileCount}개 파일/{orders.Count}줄 → " +
                $"보충 후보 {analysis.Candidates.Select(c => c.TrackingNo).Distinct().Count()}송장/{analysis.Candidates.Count}줄 " +
                $"(CSKU 지정 필요 {analysis.Candidates.Count(c => c.NeedsCsku)}), 발주서만 있음 {analysis.UnshippedOrders.Count}건" +
                (errors.Count > 0 ? $" · 읽기 실패 {errors.Count}건" : "");
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n", errors.Take(20)), "일부 파일을 읽지 못했습니다", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "분석 실패";
            MessageBox.Show(this, ex.Message, "분석 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private List<TrackingBackfillRow> ReadCourierRows(CourierMaster courier, out List<string> errors)
    {
        errors = [];
        var rows = new List<TrackingBackfillRow>();
        foreach (var file in _courierFiles)
        {
            try
            {
                using var package = Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    ? CsvWorkbookReader.LoadAsPackage(file)
                    : ExcelFileOpener.OpenWithPasswordPrompt(file, this);
                if (package == null) continue;
                var sheet = package.Workbook.Worksheets.FirstOrDefault();
                if (sheet == null) continue;
                var parsed = TrackingBackfillFileParser.Parse(sheet, courier, Path.GetFileName(file));
                if (parsed.Error != null) errors.Add($"{Path.GetFileName(file)}: {parsed.Error}");
                else rows.AddRange(parsed.Rows);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return rows;
    }

    /// <summary>
    /// 발주서 폴더(하위 폴더 포함)에서 파일명 날짜가 마감월 전후(-15일 ~ +5일)인 파일을 채널 발주서 설정으로
    /// 읽는다. 같은 파일이 여러 날짜 폴더에 복사돼 있을 수 있어 파일명으로 중복을 거른다.
    /// </summary>
    private async Task<(List<BackfillOrderLine> Orders, int FileCount, List<string> Errors)> LoadOrdersAsync(SalesChannel channel, string folder)
    {
        var orders = new List<BackfillOrderLine>();
        var errors = new List<string>();
        if (folder.Length == 0 || !Directory.Exists(folder)) return (orders, 0, errors);

        var config = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channel.ChannelCode);
        if (config == null)
        {
            errors.Add($"'{channel.ChannelName}' 채널 설정(발주서 매핑)이 없습니다.");
            return (orders, 0, errors);
        }

        var (from, to) = PartnerShipmentBackfillEngine.PeriodRange(CurrentPeriod);
        var files = Directory.EnumerateFiles(folder, "*.xls*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
            .Select(f => (Path: f, Date: PartnerShipmentBackfillEngine.FileOrderDate(f) ?? File.GetLastWriteTime(f).Date))
            .Where(f => f.Date >= from.AddDays(-15) && f.Date < to.AddDays(5))
            .GroupBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var skuMapper = await Task.Run(() => new SkuMapper(new MappingRepository(), channel.ChannelCode, _channelSkuRepo));
        var loader = new OrderLoader();
        foreach (var (path, date) in files)
        {
            try
            {
                var items = await loader.LoadFromFileAsync(skuMapper, config, path);
                foreach (var item in items)
                {
                    if (item.Status?.StartsWith("제외", StringComparison.Ordinal) == true) continue;
                    orders.Add(new BackfillOrderLine(date, item.Recipient?.Trim() ?? "", item.ProductName ?? "", item.OptionName ?? "",
                        item.Quantity, string.IsNullOrWhiteSpace(item.MappedSku) ? null : item.MappedSku,
                        item.Address ?? "", item.Phone ?? "", Path.GetFileName(path)));
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return (orders, files.Count, errors);
    }

    private void AssignCsku()
    {
        if (CurrentChannel is not { } channel) return;
        var rows = _candidateGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<BackfillCandidate>().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "CSKU를 지정할 행을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new CskuPickerDialog(channel.ChannelCode);
        if (FormManager.ShowDialogSafe(picker, this) != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedCskuCode)) return;
        if (picker.SelectedChannelCode != null && picker.SelectedChannelCode != channel.ChannelCode)
        {
            MessageBox.Show(this, $"'{channel.ChannelName}' 채널의 CSKU를 고르세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var r in rows)
        {
            r.CskuCode = picker.SelectedCskuCode!;
            r.SupplyPrice = picker.SelectedUnitPrice;
            r.Selected = true;
        }
        _candidateGrid.Refresh();
    }

    /// <summary>
    /// 체크한 후보를 출고확정으로 등록한다(출고일 = 운송장 접수일, 송장번호 그대로). 같은 송장·같은 CSKU는
    /// 수량을 합친다(출고이력 고유키가 묶음키+CSKU). 등록 직전에 송장번호 존재를 다시 확인해 중복 등록을 막는다.
    /// </summary>
    private void RegisterChecked()
    {
        if (CurrentChannel is not { } channel) return;
        _candidateGrid.EndEdit();
        var chosen = _candidates.Where(c => c.Selected).ToList();
        if (chosen.Count == 0) { _statusLabel.Text = "체크한 후보가 없습니다."; return; }

        var missingCsku = chosen.Where(c => c.NeedsCsku).ToList();
        if (missingCsku.Count > 0)
        {
            MessageBox.Show(this, $"CSKU가 없는 줄이 {missingCsku.Count}건 있습니다. [선택 행 CSKU 지정]으로 먼저 지정하거나 체크를 해제하세요.",
                "출고 등록", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 이력에 미출고로 있던 송장은 새로 넣지 않고 운송장 접수일로 출고확정만 한다.
        var toConfirm = chosen.Where(c => c.IsConfirmExisting).ToList();
        var newOnes = chosen.Where(c => !c.IsConfirmExisting).ToList();
        var already = _outboundRepo.GetExistingTrackingNos(newOnes.Select(c => c.TrackingNo));
        var toRegister = newOnes.Where(c => !already.Contains(c.TrackingNo)).ToList();
        var merged = toRegister
            .GroupBy(c => (c.TrackingNo, c.CskuCode))
            .Select(g => (First: g.First(), Qty: g.Sum(x => x.Qty)))
            .ToList();

        var trackingCount = merged.Select(m => m.First.TrackingNo).Distinct().Count();
        if (MessageBox.Show(this,
                $"'{channel.ChannelName}'에 송장 {trackingCount}건 / {merged.Count}줄을 출고확정으로 새로 등록하고, " +
                $"이력에 미출고로 있던 송장 {toConfirm.Count}건을 출고확정 처리합니다(출고일 = 운송장 접수일)." +
                (already.Count > 0 ? $"\n이미 등록된 송장 {already.Count}건은 건너뜁니다." : ""),
                "출고 등록", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        foreach (var c in toConfirm) _outboundRepo.MarkAsShippedOn(c.ExistingDetailIds, c.ReceivedAt);

        var details = merged.Select(m => new OutboundDetail
        {
            ChannelCode = channel.ChannelCode,
            OrderNo = $"보충-{m.First.TrackingNo}",
            TrackingNo = m.First.TrackingNo,
            MskuCode = m.First.CskuCode,
            CskuCode = m.First.CskuCode,
            Qty = m.Qty,
            SupplyPrice = m.First.SupplyPrice,
            ConfirmedAt = m.First.ReceivedAt,
            Recipient = m.First.Recipient,
            Phone = m.First.Phone,
            Address = m.First.Address,
            ProductName = string.IsNullOrWhiteSpace(m.First.OrderProductName) ? m.First.CourierProductName : m.First.OrderProductName,
            Remark = m.First.OrderDate is { } od ? $"출고보충(발주 {od:MM-dd})" : "출고보충(운송장)",
        }).ToList();
        if (details.Count > 0) _outboundRepo.AddManualEntries(details);

        foreach (var c in toConfirm) _candidates.Remove(c);
        foreach (var c in toRegister) _candidates.Remove(c);
        _statusLabel.Text = $"출고 등록 완료 — 신규 송장 {trackingCount}건 / {details.Count}줄, 출고확정 처리 {toConfirm.Count}건. 거래처 마감보드에서 새로고침하면 반영됩니다.";
    }
}
