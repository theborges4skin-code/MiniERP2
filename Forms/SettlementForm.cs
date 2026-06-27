using System.ComponentModel;
using System.Data;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.6절 '마감/이익분석' 창.
/// 이익분석(자동) 탭과 마감 대조(수기) 탭을 한 화면에서 다룬다.
/// </summary>
public class SettlementForm : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly SettlementRepository _settlementRepository = new();
    private readonly OutboundRepository _outboundRepository = new();
    private readonly SettlementLoader _settlementLoader = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();

    private ExcelLikeDataGridView _settlementGrid = new();
    private BindingList<SettlementData> _settlementRows = new();
    private ToolStripStatusLabel _statusLabel = new();

    private ExcelLikeDataGridView _summaryGrid = new();
    private CheckBox _unmappedOnlyCheckBox = new();
    private Label _summaryTotalsLabel = new();

    private ExcelLikeDataGridView _outboundGrid = new();
    private DataGridView _statementGrid = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ComboBox _reconcileChannelComboBox = new();

    public SettlementForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "마감/이익분석";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var tabControl = new TabControl { Dock = DockStyle.Fill };
        tabControl.TabPages.Add(CreateProfitAnalysisTab());
        tabControl.TabPages.Add(CreateReconciliationTab());

        Controls.Add(tabControl);

        FormClosing += (s, e) =>
        {
            _settlementGrid.SaveLayout();
            _outboundGrid.SaveLayout();
        };
    }

    // ===================== 이익분석(자동) =====================

    private TabPage CreateProfitAnalysisTab()
    {
        var tabPage = new TabPage("이익분석(자동)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnLoad = new Button { Text = "정산파일 로드", Size = new Size(120, 30) };
        var btnSave = new Button { Text = "결과 저장", Size = new Size(100, 30) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };

        btnLoad.Click += OnLoadSettlementClick;
        btnSave.Click += OnSaveSettlementClick;
        btnExport.Click += OnExportSettlementClick;

        toolStrip.Controls.Add(btnLoad);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnExport);

        // 99.1: 기본값은 미매핑/확인필요 건만 보이게 — 체크 해제하면 전체(미매핑이 위로 정렬된 채)를 본다.
        _unmappedOnlyCheckBox = new CheckBox { Text = "미매핑건만 보기", AutoSize = true, Checked = true, Padding = new Padding(10, 7, 0, 0) };
        _unmappedOnlyCheckBox.CheckedChanged += (s, e) => RefreshProfitAnalysisView();
        toolStrip.Controls.Add(_unmappedOnlyCheckBox);

        _settlementGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.SettlementGrid",
            AutoGenerateColumns = false,
        };
        _settlementGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelCode", DataPropertyName = "ChannelCode", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "상품명", Name = "ProductName", DataPropertyName = "ProductName", Width = 220 },
            new DataGridViewTextBoxColumn { HeaderText = "옵션명", Name = "OptionName", DataPropertyName = "OptionName", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "매핑 SKU", Name = "Msku", DataPropertyName = "Msku", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "정산액", Name = "Settlement", DataPropertyName = "Settlement", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "배송비", Name = "Shipping", DataPropertyName = "Shipping", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "입출고비", Name = "Fee", DataPropertyName = "Fee", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "이익액", Name = "Profit", DataPropertyName = "Profit", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "Status", Width = 100, ReadOnly = true }
        );
        _settlementGrid.RowPrePaint += OnSettlementGridRowPrePaint;

        // 99.1: 상단(전체/필터된 목록) + 하단(상품그룹별 요약) 분할. 사용자가 조절한 폭은 기억된다.
        var split = new PersistentSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 420, PersistenceKey = "SettlementForm.ProfitSplit" };
        split.Panel1.Controls.Add(_settlementGrid);

        var summaryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _summaryTotalsLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        _summaryGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.SummaryGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
        };
        _summaryGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상품그룹", Name = "ProductGroup", DataPropertyName = "ProductGroup", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", Name = "RowCount", DataPropertyName = "RowCount", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "매출액", Name = "Settlement", DataPropertyName = "Settlement", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "배송비", Name = "Shipping", DataPropertyName = "Shipping", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "입출고비", Name = "Fee", DataPropertyName = "Fee", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "순이익", Name = "Profit", DataPropertyName = "Profit", Width = 110 }
        );

        summaryLayout.Controls.Add(_summaryTotalsLabel, 0, 0);
        summaryLayout.Controls.Add(_summaryGrid, 0, 1);
        split.Panel2.Controls.Add(WithGroupLabel("상품그룹별 요약 (광고비/택배비는 별도 집계라 미포함)", summaryLayout));

        var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusLabel = new ToolStripStatusLabel("준비");
        statusStrip.Items.Add(_statusLabel);

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(split, 0, 1);
        mainLayout.Controls.Add(statusStrip, 0, 2);

        tabPage.Controls.Add(mainLayout);
        RefreshProfitAnalysisView();
        return tabPage;
    }

    /// <summary>
    /// 99.1: 미매핑건만 보기 토글/정렬을 그리드에 반영하고, 하단 상품그룹별 요약을 다시 집계한다.
    /// "결과 저장"/"엑셀로 내보내기"는 항상 <see cref="_settlementRows"/>(원본 전체)을 직접 참조하므로
    /// 여기서 그리드에 보여주는 필터링된 뷰와 무관하게 항상 전체 데이터를 대상으로 동작한다.
    /// </summary>
    private void RefreshProfitAnalysisView()
    {
        var view = _unmappedOnlyCheckBox.Checked
            ? _settlementRows.Where(SettlementRowStatus.IsUnresolved)
            : _settlementRows.OrderByDescending(SettlementRowStatus.IsUnresolved);
        _settlementGrid.DataSource = new BindingList<SettlementData>(view.ToList());

        var groups = _settlementRows
            .GroupBy(ResolveProductGroup)
            .Select(g => new ProfitGroupSummary
            {
                ProductGroup = g.Key,
                RowCount = g.Count(),
                Qty = g.Sum(x => x.Qty),
                Settlement = g.Sum(x => x.Settlement),
                Shipping = g.Sum(x => x.Shipping),
                Fee = g.Sum(x => x.Fee),
                Profit = g.Sum(x => x.Profit),
            })
            .OrderByDescending(s => s.Profit)
            .ToList();
        _summaryGrid.DataSource = new BindingList<ProfitGroupSummary>(groups);

        _summaryTotalsLabel.Text = BuildTotalsText(groups, _settlementRows.Count(SettlementRowStatus.IsUnresolved));
    }

    private string BuildTotalsText(List<ProfitGroupSummary> groups, int unresolvedCount)
    {
        if (_settlementRows.Count == 0) return "전체 0건";

        return $"전체 {_settlementRows.Count}건 (미매핑/확인필요 {unresolvedCount}건) | " +
               $"매출액 합계 {groups.Sum(g => g.Settlement):N0} | 수량 합계 {groups.Sum(g => g.Qty):N0}개 | " +
               $"순이익 합계 {groups.Sum(g => g.Profit):N0} | 배송비 합계 {groups.Sum(g => g.Shipping):N0} | 입출고비 합계 {groups.Sum(g => g.Fee):N0}";
    }

    /// <summary>
    /// SettlementData.Msku는 매핑 규칙의 TargetSku(CSKU 코드일 수 있음)이므로, 마스터SKU로 변환한
    /// 뒤 마스터DB의 상품그룹을 찾는다.
    /// </summary>
    private string ResolveProductGroup(SettlementData data)
    {
        if (string.IsNullOrWhiteSpace(data.Msku)) return "(미매핑)";

        var masterSku = _channelSkuRepository.ResolveMasterSku(data.ChannelCode, data.Msku);
        var item = _itemRepository.GetBySku(masterSku);
        return string.IsNullOrWhiteSpace(item?.ProductGroup) ? "(미지정)" : item.ProductGroup!;
    }

    private async void OnLoadSettlementClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Multiselect = true,
            Title = "정산 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("SettlementLoad") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        using var channelDialog = new SelectChannelDialog();
        if (channelDialog.ShowDialog(this) != DialogResult.OK || channelDialog.SelectedChannel == null)
        {
            _statusLabel.Text = "채널이 선택되지 않아 작업을 취소했습니다.";
            return;
        }

        var channelConfig = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelDialog.SelectedChannel.ChannelCode);
        if (channelConfig == null)
        {
            GuideToChannelConfig(channelDialog.SelectedChannel);
            return;
        }

        var skuMapper = new SkuMapper(_mappingRepository, channelConfig.ChannelCode);

        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = $"'{channelDialog.SelectedChannel.ChannelName}' 채널의 설정으로 정산 파일을 읽는 중입니다...";

        try
        {
            _settingsService.SetLastFolder("SettlementLoad", Path.GetDirectoryName(ofd.FileNames[0])!);

            foreach (var file in ofd.FileNames)
            {
                var loadedRows = await LoadSettlementFileWithPasswordRetryAsync(skuMapper, channelConfig, file);
                if (loadedRows == null) continue; // 사용자가 비밀번호 입력을 취소함
                foreach (var row in loadedRows) _settlementRows.Add(row);
            }

            RefreshProfitAnalysisView();

            var unresolvedCount = _settlementRows.Count(SettlementRowStatus.IsUnresolved);
            _statusLabel.Text = $"총 {_settlementRows.Count}건의 정산 데이터가 로드되었습니다. (미매핑/확인필요 {unresolvedCount}건)";

            // 99.1: 미매핑/원가없음 등 확인이 필요한 건이 있으면 안내한다(목록 상단/필터로 이미 노출됨).
            if (unresolvedCount > 0)
            {
                MessageBox.Show(
                    $"미매핑/원가없음 등 확인이 필요한 건이 {unresolvedCount}건 있습니다.\n목록 상단에 자동으로 표시했습니다(\"미매핑건만 보기\" 체크 해제 시 전체 확인 가능).",
                    "확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n\n{ex.Message}", "로드 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "오류 발생";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// 파일이 암호로 보호되어 있으면 비밀번호를 물어보고 재시도합니다. 사용자가 취소하면 null을 반환합니다.
    /// </summary>
    private async Task<List<SettlementData>?> LoadSettlementFileWithPasswordRetryAsync(SkuMapper skuMapper, ChannelConfig channelConfig, string file)
    {
        try
        {
            return await _settlementLoader.LoadFromFileAsync(skuMapper, _itemRepository, channelConfig, file, channelSkuRepository: _channelSkuRepository);
        }
        catch (EncryptedExcelFileException)
        {
            using var dialog = new PasswordPromptDialog(Path.GetFileName(file));
            if (dialog.ShowDialog(this) != DialogResult.OK) return null;

            return await _settlementLoader.LoadFromFileAsync(skuMapper, _itemRepository, channelConfig, file, dialog.Password, _channelSkuRepository);
        }
    }

    /// <summary>
    /// 선택한 채널에 ChannelConfig가 없을 때 안내 후 채널 설정 창을 열어 해당 채널을 바로 보여줍니다.
    /// </summary>
    private void GuideToChannelConfig(SalesChannel channel)
    {
        MessageBox.Show(
            $"'{channel.ChannelName}' 채널의 설정이 없습니다.\n채널 설정 창에서 정산 파일을 읽는 방법을 먼저 설정해주세요.",
            "채널 설정 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        var configForm = Application.OpenForms.OfType<ChannelConfigForm>().FirstOrDefault() ?? new ChannelConfigForm();
        if (!configForm.Visible) configForm.Show();
        configForm.BringToFront();
        configForm.SelectChannelByCode(channel.ChannelCode);
    }

    private async void OnSaveSettlementClick(object? sender, EventArgs e)
    {
        if (_settlementRows.Count == 0)
        {
            MessageBox.Show("저장할 이익분석 결과가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show($"{_settlementRows.Count}건의 이익분석 결과를 저장하시겠습니까?", "저장 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var rowsToSave = _settlementRows.ToList();
            await Task.Run(() => _settlementRepository.Insert(rowsToSave));
            _statusLabel.Text = $"{rowsToSave.Count}건 저장 완료.";

            // 99.1: 저장 시 분석 결과 요약을 별도로 보여준다(하단 상품그룹별 요약 그리드는 항상 떠 있음).
            RefreshProfitAnalysisView();
            MessageBox.Show(
                $"{rowsToSave.Count}건 저장 완료.\n\n{_summaryTotalsLabel.Text}",
                "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnExportSettlementClick(object? sender, EventArgs e)
    {
        if (_settlementRows.Count == 0)
        {
            MessageBox.Show("내보낼 이익분석 결과가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"이익분석_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("SettlementExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("SettlementExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();

            WriteDetailSheet(package, "분석결과상세", _settlementRows);
            WriteSummarySheet(package.Workbook.Worksheets.Add("분석요약(상품그룹별)"));
            WriteDetailSheet(package, "미매핑·예외건", _settlementRows.Where(d => SettlementRowStatus.IsUnresolved(d) || SettlementRowStatus.IsExcludedByExceptionRule(d)).ToList());
            WriteRawDataSheet(package, "원본데이터");

            package.SaveAs(new FileInfo(filePath));

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static readonly string[] DetailHeaders = ["채널", "상품명", "옵션명", "매핑SKU", "수량", "정산액", "배송비", "입출고비", "이익액", "상태"];

    /// <summary>
    /// SalesManagerV2의 다중 시트 결과 저장 양식(상세/요약/미매핑/원본)을 참고해, "분석결과상세"와
    /// "미매핑·예외건" 두 시트에 공통으로 쓰는 표 형식.
    /// </summary>
    private static void WriteDetailSheet(ExcelPackage package, string sheetName, IReadOnlyList<SettlementData> rows)
    {
        var sheet = package.Workbook.Worksheets.Add(sheetName);
        for (int i = 0; i < DetailHeaders.Length; i++) sheet.Cells[1, i + 1].Value = DetailHeaders[i];

        int row = 2;
        foreach (var data in rows)
        {
            sheet.Cells[row, 1].Value = data.ChannelCode;
            sheet.Cells[row, 2].Value = data.ProductName;
            sheet.Cells[row, 3].Value = data.OptionName;
            sheet.Cells[row, 4].Value = data.Msku;
            sheet.Cells[row, 5].Value = data.Qty;
            sheet.Cells[row, 6].Value = data.Settlement;
            sheet.Cells[row, 7].Value = data.Shipping;
            sheet.Cells[row, 8].Value = data.Fee;
            sheet.Cells[row, 9].Value = data.Profit;
            sheet.Cells[row, 10].Value = data.Status;
            row++;
        }
        sheet.Cells.AutoFitColumns();
    }

    private void WriteSummarySheet(ExcelWorksheet sheet)
    {
        string[] headers = ["상품그룹", "건수", "수량", "매출액", "배송비", "입출고비", "순이익"];
        for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

        var groups = _settlementRows
            .GroupBy(ResolveProductGroup)
            .Select(g => new ProfitGroupSummary
            {
                ProductGroup = g.Key,
                RowCount = g.Count(),
                Qty = g.Sum(x => x.Qty),
                Settlement = g.Sum(x => x.Settlement),
                Shipping = g.Sum(x => x.Shipping),
                Fee = g.Sum(x => x.Fee),
                Profit = g.Sum(x => x.Profit),
            })
            .OrderByDescending(s => s.Profit)
            .ToList();

        int row = 2;
        foreach (var g in groups)
        {
            sheet.Cells[row, 1].Value = g.ProductGroup;
            sheet.Cells[row, 2].Value = g.RowCount;
            sheet.Cells[row, 3].Value = g.Qty;
            sheet.Cells[row, 4].Value = g.Settlement;
            sheet.Cells[row, 5].Value = g.Shipping;
            sheet.Cells[row, 6].Value = g.Fee;
            sheet.Cells[row, 7].Value = g.Profit;
            row++;
        }

        sheet.Cells[row, 1].Value = "합계";
        sheet.Cells[row, 2].Value = groups.Sum(g => g.RowCount);
        sheet.Cells[row, 3].Value = groups.Sum(g => g.Qty);
        sheet.Cells[row, 4].Value = groups.Sum(g => g.Settlement);
        sheet.Cells[row, 5].Value = groups.Sum(g => g.Shipping);
        sheet.Cells[row, 6].Value = groups.Sum(g => g.Fee);
        sheet.Cells[row, 7].Value = groups.Sum(g => g.Profit);
        sheet.Cells[row, 1, row, 7].Style.Font.Bold = true;

        sheet.Cells.AutoFitColumns();
    }

    /// <summary>
    /// 정산 파일에서 읽은 원본 행(SettlementLoader가 RawValues에 채워둔 헤더->값)을 그대로 출력한다.
    /// 파일마다 헤더 구성이 다를 수 있으므로, 전체 행에서 등장하는 모든 헤더의 합집합을 열로 쓴다.
    /// </summary>
    private void WriteRawDataSheet(ExcelPackage package, string sheetName)
    {
        var sheet = package.Workbook.Worksheets.Add(sheetName);
        var rowsWithRaw = _settlementRows.Where(d => d.RawValues is { Count: > 0 }).ToList();
        if (rowsWithRaw.Count == 0)
        {
            sheet.Cells[1, 1].Value = "원본 데이터가 없습니다.";
            return;
        }

        var headers = rowsWithRaw.SelectMany(d => d.RawValues!.Keys).Distinct().ToList();
        for (int i = 0; i < headers.Count; i++) sheet.Cells[1, i + 1].Value = headers[i];

        int row = 2;
        foreach (var data in rowsWithRaw)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                sheet.Cells[row, i + 1].Value = data.RawValues!.GetValueOrDefault(headers[i], string.Empty);
            }
            row++;
        }
        sheet.Cells.AutoFitColumns();
    }

    private void OnSettlementGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _settlementGrid.Rows.Count) return;

        var row = _settlementGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not SettlementData data) return;

        if (string.IsNullOrWhiteSpace(data.Msku) || data.Status == "원가 정보 없음")
        {
            // 다크모드에서 기본 글자색이 흰색으로 바뀌어도 강조 배경에서 글자가 보이도록 검은색으로 고정한다.
            row.DefaultCellStyle.BackColor = Color.MistyRose;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else
        {
            row.DefaultCellStyle.BackColor = _settlementGrid.DefaultCellStyle.BackColor;
            row.DefaultCellStyle.ForeColor = _settlementGrid.DefaultCellStyle.ForeColor;
        }
    }

    // ===================== 마감 대조(수기) =====================

    private TabPage CreateReconciliationTab()
    {
        var tabPage = new TabPage("마감 대조(수기)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        _reconcileChannelComboBox = new ComboBox { Size = new Size(160, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _reconcileChannelComboBox.DataSource = _salesChannelRepository.GetAll();
        _reconcileChannelComboBox.DisplayMember = "ChannelName";
        _reconcileChannelComboBox.ValueMember = "ChannelCode";

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 100 };

        var btnLoadOutbound = new Button { Text = "출고내역 조회", Size = new Size(110, 30) };
        var btnLoadStatement = new Button { Text = "거래처 마감내역 불러오기", Size = new Size(170, 30) };
        var btnExportOutbound = new Button { Text = "출고내역 엑셀로 내보내기", Size = new Size(170, 30) };

        btnLoadOutbound.Click += OnLoadOutboundClick;
        btnLoadStatement.Click += OnLoadStatementClick;
        btnExportOutbound.Click += OnExportOutboundClick;

        toolStrip.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolStrip.Controls.Add(_reconcileChannelComboBox);
        toolStrip.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_fromDatePicker);
        toolStrip.Controls.Add(new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        toolStrip.Controls.Add(_toDatePicker);
        toolStrip.Controls.Add(btnLoadOutbound);
        toolStrip.Controls.Add(btnExportOutbound);
        toolStrip.Controls.Add(btnLoadStatement);
        toolStrip.Controls.Add(new Label
        {
            Text = "운송장번호 등록/발송확인 처리는 OFS의 \"발주/출고 이력\" 창으로 옮겼습니다.",
            AutoSize = true,
            Padding = new Padding(15, 6, 0, 0),
            ForeColor = Color.DimGray,
        });

        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };

        _outboundGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.OutboundGrid",
            AutoGenerateColumns = false,
        };
        _outboundGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", Name = "OrderNo", DataPropertyName = "OrderNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", DataPropertyName = "TrackingNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "SKU", Name = "MskuCode", DataPropertyName = "MskuCode", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "납품가", Name = "SupplyPrice", DataPropertyName = "SupplyPrice", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "출고일시", Name = "CreatedAt", DataPropertyName = "CreatedAt", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "발주이력 상태", Name = "Status", DataPropertyName = "Status", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "확정일시", Name = "ConfirmedAt", DataPropertyName = "ConfirmedAt", Width = 130 }
        );

        _statementGrid = new ExcelLikeDataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true, AllowUserToAddRows = false, ReadOnly = true };

        var outboundPanel = new Panel { Dock = DockStyle.Fill };
        outboundPanel.Controls.Add(WithGroupLabel("출고내역(시스템)", _outboundGrid));
        var statementPanel = new Panel { Dock = DockStyle.Fill };
        statementPanel.Controls.Add(WithGroupLabel("거래처 마감내역(외부 파일)", _statementGrid));

        splitContainer.Panel1.Controls.Add(outboundPanel);
        splitContainer.Panel2.Controls.Add(statementPanel);

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(splitContainer, 0, 1);

        tabPage.Controls.Add(mainLayout);
        return tabPage;
    }

    private static Control WithGroupLabel(string title, Control content)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font(Control.DefaultFont, FontStyle.Bold) }, 0, 0);
        content.Dock = DockStyle.Fill;
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private void OnLoadOutboundClick(object? sender, EventArgs e)
    {
        var channelCode = _reconcileChannelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("조회할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var details = _outboundRepository.GetByChannel(channelCode, _fromDatePicker.Value.Date, _toDatePicker.Value.Date.AddDays(1).AddTicks(-1));
        _outboundGrid.DataSource = new BindingList<OutboundDetail>(details);
        _statusLabel.Text = $"출고내역 {details.Count}건 조회됨.";
    }

    private void OnExportOutboundClick(object? sender, EventArgs e)
    {
        if (_outboundGrid.DataSource is not BindingList<OutboundDetail> details || details.Count == 0)
        {
            MessageBox.Show("내보낼 출고내역이 없습니다. 먼저 조회하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"출고내역_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("OutboundExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("OutboundExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("출고내역");

            string[] headers = ["주문번호", "운송장번호", "SKU", "수량", "납품가", "출고일시"];
            for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

            int row = 2;
            foreach (var detail in details)
            {
                sheet.Cells[row, 1].Value = detail.OrderNo;
                sheet.Cells[row, 2].Value = detail.TrackingNo;
                sheet.Cells[row, 3].Value = detail.MskuCode;
                sheet.Cells[row, 4].Value = detail.Qty;
                sheet.Cells[row, 5].Value = detail.SupplyPrice;
                sheet.Cells[row, 6].Value = detail.CreatedAt;
                row++;
            }

            sheet.Cells.AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLoadStatementClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "거래처 제공 마감내역 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("StatementLoad") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _settingsService.SetLastFolder("StatementLoad", Path.GetDirectoryName(ofd.FileName)!);

            using var package = ExcelFileOpener.OpenWithPasswordPrompt(ofd.FileName, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null)
            {
                MessageBox.Show("엑셀 파일에서 데이터를 찾을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var table = new DataTable();
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString() ?? $"열{col}";
                table.Columns.Add(header);
            }

            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var dataRow = table.NewRow();
                for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                {
                    dataRow[col - 1] = worksheet.Cells[row, col].Value?.ToString() ?? string.Empty;
                }
                table.Rows.Add(dataRow);
            }

            _statementGrid.DataSource = table;
            _statusLabel.Text = $"거래처 마감내역 {table.Rows.Count}건을 불러왔습니다. 좌측 출고내역과 수기로 대조하세요.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "로드 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
