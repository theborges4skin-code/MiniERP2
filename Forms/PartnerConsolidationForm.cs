using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Exporters;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6) — 이익분석 내보내기 결과 xlsx
/// 여러 개를 상호명(DocPartyTable.CompanyName) 단위로 재취합하는 화면. 마감/이익분석 창의
/// 계산 로직(SettlementLoader/ProfitCalculator)은 건드리지 않고 그 결과 파일만 다시 읽는다(§1).
/// 파일 로드·_META 파싱·CSKU 정규화·집계(납품매출액/납품이익액)·배송건수 산정(§6.3)까지 다룬다.
/// </summary>
public class PartnerConsolidationForm : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly DocPartyRepository _docPartyRepository = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly PartnerConsolidationAggregator _aggregator;
    private readonly PartnerConsolidationPriceEntryService _priceEntryService;

    private const decimal DefaultShippingFeePerShipment = 3000m;

    private readonly BindingList<PartnerConsolidationFile> _files = [];
    private readonly BindingList<PartnerConsolidationCompanySummary> _companySummaries = [];
    private readonly BindingList<PartnerConsolidationCskuDetail> _cskuDetails = [];
    private readonly BindingList<PartnerConsolidationCskuDetail> _unassignedPriceRows = [];
    private readonly BindingList<PartnerConsolidationRow> _unmappedExcludedRows = [];
    private readonly BindingList<PartnerConsolidationChannelShipment> _channelShipments = [];

    private ExcelLikeDataGridView _fileGrid = new();
    private ExcelLikeDataGridView _companyGrid = new();
    private ExcelLikeDataGridView _cskuDetailGrid = new();
    private ExcelLikeDataGridView _unassignedGrid = new();
    private ExcelLikeDataGridView _unmappedGrid = new();
    private ExcelLikeDataGridView _channelShipmentGrid = new();
    private Label _statusLabel = new();
    private Label _unassignedStatusLabel = new();

    public PartnerConsolidationForm()
    {
        _aggregator = new PartnerConsolidationAggregator(
            new PartnerSupplyPriceResolver(_channelSkuRepository, _docPartyRepository), _itemRepository, _channelSkuRepository);
        _priceEntryService = new PartnerConsolidationPriceEntryService(_channelSkuRepository, _docPartyRepository);
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
    }

    private void InitializeComponent()
    {
        Text = "온라인 거래처 취합";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        // ── 1행: 파일 추가/제거 + 집계 실행 ────────────────────────────────
        var topPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnAddFiles = new Button { Text = "파일 추가", Size = new Size(90, 30) };
        var btnRemoveFiles = new Button { Text = "선택 파일 제거", Size = new Size(110, 30) };
        var btnReload = new Button { Text = "다시 불러오기", Size = new Size(100, 30) };
        var btnAssignChannel = new Button { Text = "채널 수동 지정...", Size = new Size(120, 30) };
        var btnAggregate = new Button { Text = "집계 실행", Size = new Size(90, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnExport = new Button { Text = "엑셀 내보내기", Size = new Size(100, 30) };
        btnAddFiles.Click += (s, e) => AddFiles();
        btnRemoveFiles.Click += (s, e) => RemoveSelectedFiles();
        btnReload.Click += (s, e) => ReloadAllFiles();
        btnAssignChannel.Click += (s, e) => AssignChannelToSelectedFile();
        btnAggregate.Click += (s, e) => RunAggregate();
        btnExport.Click += (s, e) => ExportToExcel();
        topPanel.Controls.Add(btnAddFiles);
        topPanel.Controls.Add(btnRemoveFiles);
        topPanel.Controls.Add(btnReload);
        topPanel.Controls.Add(btnAssignChannel);
        topPanel.Controls.Add(btnAggregate);
        topPanel.Controls.Add(btnExport);

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

        var companyLabel = new Label { Dock = DockStyle.Fill, Text = "거래처 요약", Padding = new Padding(6, 4, 0, 0), Font = new Font(Font, FontStyle.Bold) };

        // ── 3행: 거래처 요약(§6.5 중단) ────────────────────────────────────
        _companyGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerConsolidationForm.CompanyGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = false,
            ReadOnly = true,
        };
        _companyGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "CompanyName", DataPropertyName = "CompanyName", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "채널수", Name = "ChannelCount", DataPropertyName = "ChannelCount", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "총수량", Name = "TotalQuantity", DataPropertyName = "TotalQuantity", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품매출액", Name = "TotalSupplyRevenue", DataPropertyName = "TotalSupplyRevenue", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품이익액", Name = "TotalSupplyProfit", DataPropertyName = "TotalSupplyProfit", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "배송건수", Name = "ShipmentCount", DataPropertyName = "ShipmentCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "배송비청구액", Name = "ShippingFeeTotal", DataPropertyName = "ShippingFeeTotal", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "미배정건수", Name = "UnassignedPriceCount", DataPropertyName = "UnassignedPriceCount", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _companyGrid.DataSource = _companySummaries;

        // ── 4행: 하단 탭(§6.5) ────────────────────────────────────────────
        var tabs = new TabControl { Dock = DockStyle.Fill };

        _cskuDetailGrid = BuildDetailGrid("PartnerConsolidationForm.CskuDetailGrid");
        _cskuDetailGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 130 },
            // 거래처에는 이 열(ChannelSkuTable.InvoiceDisplayName)이 실제로 전달돼야 한다 — 마스터DB
            // 상품명(ProductName)은 명세표 표기와 다를 수 있어 내부 참고용일 뿐이므로 이 화면에는
            // 아예 올리지 않는다(§ Models/PartnerConsolidationModels.PartnerConsolidationCskuDetail
            // .InvoiceDisplayName 주석 참고). 미등록이면 공란으로 표시된다.
            new DataGridViewTextBoxColumn { HeaderText = "송장표시명", Name = "InvoiceDisplayName", DataPropertyName = "InvoiceDisplayName", Width = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "마스터SKU", Name = "Msku", DataPropertyName = "Msku", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Quantity", DataPropertyName = "Quantity", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품단가", Name = "SupplyPrice", DataPropertyName = "SupplyPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품단가(VAT별도)", Name = "SupplyPriceVatExcluded", DataPropertyName = "SupplyPriceVatExcluded", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "#,##0.##", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단가출처", Name = "PriceSourceDisplay", DataPropertyName = "PriceSourceDisplay", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "납품매출액", Name = "SupplyRevenue", DataPropertyName = "SupplyRevenue", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "제조원가", Name = "CostPrice", DataPropertyName = "CostPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품이익액", Name = "SupplyProfit", DataPropertyName = "SupplyProfit", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _cskuDetailGrid.DataSource = _cskuDetails;

        // 이 탭은 애초에 "단가가 없는 행"만 보여줬으나, 이미 자체/상속 단가가 있는 CSKU도 나중에
        // 납품명·납품단가를 고칠 진입점이 없다는 문제(마감/이익 매핑은 MSKU 단위라 CSKU별 세부값을
        // 여기서 채워야 함)가 있어 전체 CSKU로 확장했다. "입력할..." 두 열만 편집 가능해야 하므로
        // BuildDetailGrid(전체 ReadOnly)를 쓰지 않고 그리드는 편집 가능하게 두되 나머지 열만
        // 개별로 ReadOnly 처리한다. "입력할..." 열은 현재 값으로 미리 채워지며(RunAggregate 참고),
        // 실제로 값을 바꾼 행만 저장된다(SaveEnteredPrices의 변경분 필터 참고).
        _unassignedGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerConsolidationForm.UnassignedGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
        };
        _unassignedGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "CompanyName", DataPropertyName = "CompanyName", Width = 130, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 130, ReadOnly = true },
            // 마스터DB 상품명(ProductName)은 명세표 표기와 다를 수 있어 여기서는 보여주지 않는다 —
            // CSKU 바로 옆에는 실제로 거래처에 전달될 입력값(송장표시명)이 와야 한다.
            new DataGridViewTextBoxColumn { HeaderText = "입력할 송장표시명", Name = "EnteredInvoiceDisplayName", DataPropertyName = "EnteredInvoiceDisplayName", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "마스터SKU", Name = "Msku", DataPropertyName = "Msku", Width = 120, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Quantity", DataPropertyName = "Quantity", Width = 70, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "현재 단가출처", Name = "PriceSourceDisplay", DataPropertyName = "PriceSourceDisplay", Width = 110, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "입력할 납품단가", Name = "EnteredPrice", DataPropertyName = "EnteredPrice", Width = 120, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _unassignedGrid.DataSource = _unassignedPriceRows;

        _unmappedGrid = BuildDetailGrid("PartnerConsolidationForm.UnmappedGrid");
        _unmappedGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "CompanyName", DataPropertyName = "CompanyName", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelCode", DataPropertyName = "ChannelCode", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "상품명", Name = "ProductName", DataPropertyName = "ProductName", Width = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "매핑SKU", Name = "RawMappedSku", DataPropertyName = "RawMappedSku", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "RawStatus", DataPropertyName = "RawStatus", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "분류", Name = "Kind", DataPropertyName = "Kind", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "파일", Name = "SourceFileName", DataPropertyName = "SourceFileName", Width = 180 }
        );
        _unmappedGrid.DataSource = _unmappedExcludedRows;

        _channelShipmentGrid = BuildDetailGrid("PartnerConsolidationForm.ChannelShipmentGrid");
        _channelShipmentGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "CompanyName", DataPropertyName = "CompanyName", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelName", DataPropertyName = "ChannelName", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", Name = "ShipmentCount", DataPropertyName = "ShipmentCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "산정근거", Name = "BasisDisplay", DataPropertyName = "BasisDisplay", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "배송비총액", Name = "ShippingTotal", DataPropertyName = "ShippingTotal", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _channelShipmentGrid.DataSource = _channelShipments;

        var cskuTab = new TabPage("CSKU 상세"); cskuTab.Controls.Add(_cskuDetailGrid);

        // "납품단가/명 입력" 탭 전용: 전체 CSKU가 대상이며(단가 미배정 여부와 무관), 입력란에
        // 값을 채운 뒤 이 버튼으로 대표단가 채널에 저장한다(§8-S8, 확장 배경은 위 그리드 주석 참고).
        var unassignedTab = new TabPage("납품단가/명 입력");
        var unassignedLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        unassignedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        unassignedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var unassignedToolPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        var btnSaveEnteredPrices = new Button { Text = "입력된 단가 저장", Size = new Size(120, 26) };
        btnSaveEnteredPrices.Click += (s, e) => SaveEnteredPrices();
        _unassignedStatusLabel = new Label { AutoSize = true, Padding = new Padding(10, 6, 0, 0) };
        unassignedToolPanel.Controls.Add(btnSaveEnteredPrices);
        unassignedToolPanel.Controls.Add(_unassignedStatusLabel);
        unassignedLayout.Controls.Add(unassignedToolPanel, 0, 0);
        unassignedLayout.Controls.Add(_unassignedGrid, 0, 1);
        unassignedTab.Controls.Add(unassignedLayout);

        var unmappedTab = new TabPage("미매핑·제외"); unmappedTab.Controls.Add(_unmappedGrid);
        var channelShipmentTab = new TabPage("채널별 배송건수"); channelShipmentTab.Controls.Add(_channelShipmentGrid);
        tabs.TabPages.AddRange(cskuTab, unassignedTab, unmappedTab, channelShipmentTab);

        // ── 5행: 상태표시줄 ──────────────────────────────────────────────
        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "파일을 추가한 뒤 '집계 실행'을 누르세요.", Padding = new Padding(6, 4, 0, 0) };

        mainLayout.Controls.Add(topPanel, 0, 0);
        mainLayout.Controls.Add(_fileGrid, 0, 1);
        mainLayout.Controls.Add(companyLabel, 0, 2);
        mainLayout.Controls.Add(_companyGrid, 0, 3);
        mainLayout.Controls.Add(tabs, 0, 4);
        mainLayout.Controls.Add(_statusLabel, 0, 5);

        Controls.Add(mainLayout);
    }

    private static ExcelLikeDataGridView BuildDetailGrid(string persistenceKey) => new()
    {
        Dock = DockStyle.Fill,
        PersistenceKey = persistenceKey,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
        MultiSelect = true,
        ReadOnly = true,
    };

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

            _files.Add(LoadFile(path));
            addedCount++;
        }

        WarnDuplicateChannelFiles();
        _statusLabel.Text = $"파일 {addedCount}개 추가됨. 총 {_files.Count}개 로드됨. '집계 실행'을 눌러 반영하세요.";
    }

    private void RemoveSelectedFiles()
    {
        var selected = _fileGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as PartnerConsolidationFile)
            .Where(f => f != null)
            .ToList();
        foreach (var f in selected) _files.Remove(f!);
    }

    private void ReloadAllFiles()
    {
        var paths = _files.Select(f => f.FilePath).ToList();
        _files.Clear();
        foreach (var path in paths)
            _files.Add(LoadFile(path));
        _statusLabel.Text = $"{paths.Count}개 파일을 다시 불러왔습니다. '집계 실행'을 눌러 반영하세요.";
    }

    /// <summary>
    /// _META의 상호명이 빈 파일(채널을 거래처에 연결하기 전에 내보낸 이익분석 파일)은 지금 DB의
    /// 채널→거래처 연결(DocPartyTable)로 상호명을 채운다 — 이익분석을 다시 내보내지 않아도 된다.
    /// </summary>
    private PartnerConsolidationFile LoadFile(string path)
    {
        var file = PartnerConsolidationFileLoader.Load(path, _channelSkuRepository, _channelConfigService);
        if (string.IsNullOrWhiteSpace(file.CompanyName) && !string.IsNullOrWhiteSpace(file.ChannelCode))
        {
            file.CompanyName = _docPartyRepository.GetByChannelCode(file.ChannelCode)?.CompanyName ?? "";
            foreach (var row in file.Rows) row.CompanyName = file.CompanyName;
        }
        return file;
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

        // 파일 전체를 그 채널 기준으로 다시 읽는다 — 행의 CSKU 정규화가 채널코드에 좌우되므로
        // (수동 지정 전에는 채널을 몰라 원래 행의 '채널' 컬럼 값을 그대로 썼을 수 있다).
        var reloaded = PartnerConsolidationFileLoader.Load(selected.FilePath, _channelSkuRepository, _channelConfigService);
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

        _statusLabel.Text = $"'{reloaded.FileName}'의 채널을 '{channel.ChannelName}'(으)로 지정했습니다. '집계 실행'을 눌러 반영하세요.";
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

    // ── 집계(§6.2) ──────────────────────────────────────────────────────

    private void RunAggregate()
    {
        var allRows = _files.Where(f => !f.LoadFailed).SelectMany(f => f.Rows).ToList();

        var result = _aggregator.Aggregate(allRows);
        var (shipmentByCompany, zeroShippingChannels) = RunShipmentCalculation();

        _companySummaries.Clear();
        foreach (var s in result.CompanySummaries.OrderBy(s => s.CompanyName, StringComparer.Ordinal))
        {
            if (shipmentByCompany.TryGetValue(s.CompanyName, out var shipment))
            {
                s.ShipmentCount = shipment.ShipmentCount;
                s.ShippingFeeTotal = shipment.ShippingFeeTotal;
            }
            _companySummaries.Add(s);
        }

        _cskuDetails.Clear();
        _unassignedPriceRows.Clear();
        var unassignedCount = 0;
        foreach (var d in result.CskuDetails.OrderBy(d => d.CompanyName, StringComparer.Ordinal).ThenBy(d => d.CskuCode, StringComparer.Ordinal))
        {
            // "납품단가/명 입력" 탭 입력란을 현재 값으로 미리 채운다. 단가는 미배정(0)일 때만 비워
            // 둬서 사용자가 실제 값을 입력하게 하고, 그 외에는 현재 값을 보여줘 그대로 두거나 고칠
            // 수 있게 한다(둘 다 사용자가 값을 바꾸지 않으면 SaveEnteredPrices가 저장 대상에서 뺀다).
            d.EnteredPrice = d.IsPriceUnassigned ? null : d.SupplyPrice;
            d.EnteredInvoiceDisplayName = string.IsNullOrWhiteSpace(d.InvoiceDisplayName) ? null : d.InvoiceDisplayName;
            if (d.IsPriceUnassigned) unassignedCount++;

            _cskuDetails.Add(d);
            _unassignedPriceRows.Add(d);
        }

        _unmappedExcludedRows.Clear();
        foreach (var row in allRows.Where(r => r.Kind != PartnerConsolidationRowKind.Mapped))
            _unmappedExcludedRows.Add(row);

        _statusLabel.Text = $"집계 완료 — 거래처 {_companySummaries.Count}곳, CSKU {_cskuDetails.Count}건 " +
            $"(단가 미배정 {unassignedCount}건, 미매핑·제외 {_unmappedExcludedRows.Count}건).";

        if (zeroShippingChannels.Count > 0)
        {
            MessageBox.Show(this,
                $"다음 채널은 배송비 총액이 0이라 배송건수가 0건으로 계산되었습니다 — 정산서 매핑에 송장번호/배송비 필드가 없을 수 있습니다(W3):\n{string.Join(", ", zeroShippingChannels)}",
                "배송건수 0건 경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// §6.3 — 파일(=채널) 단위로 배송건수를 산정하고, 거래처 요약의 ShipmentCount/ShippingFeeTotal을
    /// 채운다. 청구 단가(ShippingFeePerShipment)는 거래처의 대표단가 채널 설정값을 쓴다(대표가 없으면
    /// 기본 3,000원) — D14가 이 값을 상수가 아닌 설정값으로 두라고 했을 뿐 회사 단위로 어느 채널의
    /// 값을 대표로 쓸지는 스펙에 명시돼 있지 않아, 이미 "회사의 대표"로 정의된 대표단가 채널을 그대로
    /// 재사용하기로 정했다(§5의 대표단가 채널과 동일한 개념적 지위).
    /// </summary>
    /// <returns>
    /// 거래처별 (배송건수, 배송비청구액) 딕셔너리와, 배송비 총액이 0이라 W3 경고 대상인 채널명 목록.
    /// </returns>
    private (Dictionary<string, (int ShipmentCount, decimal ShippingFeeTotal)> ByCompany, List<string> ZeroShippingChannels) RunShipmentCalculation()
    {
        _channelShipments.Clear();
        var zeroShippingChannels = new List<string>();
        var byCompany = new Dictionary<string, (int, decimal)>();

        var filesByCompany = _files
            .Where(f => !f.LoadFailed && !string.IsNullOrWhiteSpace(f.ChannelCode))
            .GroupBy(f => string.IsNullOrWhiteSpace(f.CompanyName) ? "" : f.CompanyName);

        foreach (var companyGroup in filesByCompany)
        {
            var companyName = companyGroup.Key;
            var channelResults = new List<PartnerConsolidationChannelShipment>();

            foreach (var file in companyGroup)
            {
                var shippingFeePerShipment = _docPartyRepository.GetByChannelCode(file.ChannelCode)?.ShippingFeePerShipment ?? DefaultShippingFeePerShipment;
                var result = PartnerConsolidationShipmentCalculator.ComputeChannel(
                    companyName, file.ChannelCode, file.ChannelName, file.TrackingNumbers, file.ShippingTotal, shippingFeePerShipment);
                channelResults.Add(result);
                _channelShipments.Add(result);

                if (result.ShippingTotal == 0)
                    zeroShippingChannels.Add(string.IsNullOrWhiteSpace(file.ChannelName) ? file.ChannelCode : file.ChannelName);
            }

            if (string.IsNullOrWhiteSpace(companyName)) continue; // "(미지정)" 그룹은 거래처 요약 자체가 없다.

            var billingRate = _docPartyRepository.GetPriceMasterByCompanyName(companyName)?.ShippingFeePerShipment ?? DefaultShippingFeePerShipment;
            byCompany[companyName] = PartnerConsolidationShipmentCalculator.ComputeCompanyBilling(channelResults, billingRate);
        }

        return (byCompany, zeroShippingChannels);
    }

    // ── 납품단가/명 입력 탭 인라인 저장(§6.5, §8-S8) ─────────────────────────

    /// <summary>
    /// "입력할 납품단가"/"입력할 송장표시명" 중 현재 값과 실제로 달라진 행만 대표단가 채널의
    /// CSKU(SupplyPrice/InvoiceDisplayName)에 저장한다. 이 탭은 전체 CSKU를 보여주며 두 입력란이
    /// 현재 값으로 미리 채워져 있으므로(RunAggregate 참고), 단순히 "값이 있으면 저장"으로는 손대지
    /// 않은 행까지 매번 재저장하게 된다 — 그래서 원본과 같은 값은 여기서 걸러낸다. 두 열 중
    /// 건드리지 않은 쪽은 기존 값을 그대로 둔다(예: 송장표시명만 고치고 싶을 때 납품단가를 다시
    /// 입력할 필요 없음). 대표단가 채널이 없는 거래처는 저장을 막고 채널설정에서 먼저 지정하도록
    /// 안내한다(§6.5). 저장 후에는 전체를 다시 집계한다(변경이 같은 대표채널을 공유하는 다른
    /// CSKU/거래처 표시에도 영향을 줄 수 있으므로 — §6.5 "저장 후 재계산").
    /// </summary>
    private void SaveEnteredPrices()
    {
        var toSave = _unassignedPriceRows
            .Where(r => (r.EnteredPrice.HasValue && r.EnteredPrice.Value != r.SupplyPrice)
                     || (!string.IsNullOrWhiteSpace(r.EnteredInvoiceDisplayName) && !string.Equals(r.EnteredInvoiceDisplayName.Trim(), r.InvoiceDisplayName, StringComparison.Ordinal)))
            .ToList();
        if (toSave.Count == 0)
        {
            _unassignedStatusLabel.Text = "변경된 납품단가/송장표시명이 없습니다.";
            return;
        }

        var savedCount = 0;
        var noMasterCompanies = new HashSet<string>();
        var ambiguousCompanies = new HashSet<string>();

        foreach (var row in toSave)
        {
            var outcome = _priceEntryService.SavePrice(row.CompanyName, row.Msku, row.EnteredPrice, row.EnteredInvoiceDisplayName, reason: "온라인 거래처 취합 화면에서 입력");
            switch (outcome.Result)
            {
                case PartnerConsolidationPriceEntryResult.Saved:
                    savedCount++;
                    break;
                case PartnerConsolidationPriceEntryResult.NoPriceMasterChannel:
                    noMasterCompanies.Add(row.CompanyName);
                    break;
                case PartnerConsolidationPriceEntryResult.AmbiguousMasterCsku:
                    ambiguousCompanies.Add(row.CompanyName);
                    break;
            }
        }

        if (savedCount > 0)
            RunAggregate();

        var message = $"{savedCount}건 저장됨.";
        if (noMasterCompanies.Count > 0)
            message += $" 대표단가 채널이 지정되지 않아 저장하지 못한 거래처(채널설정에서 먼저 지정하세요): {string.Join(", ", noMasterCompanies)}.";
        if (ambiguousCompanies.Count > 0)
            message += $" 대표단가 채널에 같은 마스터SKU의 CSKU가 여러 개 있어 저장하지 못한 거래처(CSKU 관리창에서 직접 정리하세요): {string.Join(", ", ambiguousCompanies)}.";
        _unassignedStatusLabel.Text = message;
    }

    // ── 엑셀 내보내기(§6.6, S9) ────────────────────────────────────────────

    private void ExportToExcel()
    {
        if (_companySummaries.Count == 0)
        {
            MessageBox.Show(this, "내보낼 집계 결과가 없습니다. 먼저 '집계 실행'을 눌러주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"온라인거래처취합_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("PartnerRollupExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        _settingsService.SetLastFolder("PartnerRollupExport", Path.GetDirectoryName(filePath)!);

        try
        {
            decimal ResolveBillingRate(string companyName) =>
                _docPartyRepository.GetPriceMasterByCompanyName(companyName)?.ShippingFeePerShipment ?? DefaultShippingFeePerShipment;

            PartnerConsolidationExporter.Export(_companySummaries, _cskuDetails, _channelShipments,
                _unmappedExcludedRows, _files, ResolveBillingRate, filePath);

            // 저장한 파일 그대로 거래처 마감보드로 보낼지 묻는다. 닫으면 파일만 남고, 검토 후
            // 마감보드의 [온라인취합 불러오기]로 같은 파일을 보내 확정할 수 있다.
            PartnerConsolidationClosingDialog.ShowForFile(this, filePath);

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
