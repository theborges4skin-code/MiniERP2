using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
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

    public PartnerConsolidationForm()
    {
        _aggregator = new PartnerConsolidationAggregator(
            new PartnerSupplyPriceResolver(_channelSkuRepository, _docPartyRepository), _itemRepository);
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
        btnAddFiles.Click += (s, e) => AddFiles();
        btnRemoveFiles.Click += (s, e) => RemoveSelectedFiles();
        btnReload.Click += (s, e) => ReloadAllFiles();
        btnAssignChannel.Click += (s, e) => AssignChannelToSelectedFile();
        btnAggregate.Click += (s, e) => RunAggregate();
        topPanel.Controls.Add(btnAddFiles);
        topPanel.Controls.Add(btnRemoveFiles);
        topPanel.Controls.Add(btnReload);
        topPanel.Controls.Add(btnAssignChannel);
        topPanel.Controls.Add(btnAggregate);

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
            new DataGridViewTextBoxColumn { HeaderText = "품목명", Name = "ProductName", DataPropertyName = "ProductName", Width = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "마스터SKU", Name = "Msku", DataPropertyName = "Msku", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Quantity", DataPropertyName = "Quantity", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품단가", Name = "SupplyPrice", DataPropertyName = "SupplyPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단가출처", Name = "PriceSourceDisplay", DataPropertyName = "PriceSourceDisplay", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "납품매출액", Name = "SupplyRevenue", DataPropertyName = "SupplyRevenue", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "제조원가", Name = "CostPrice", DataPropertyName = "CostPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "납품이익액", Name = "SupplyProfit", DataPropertyName = "SupplyProfit", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _cskuDetailGrid.DataSource = _cskuDetails;

        _unassignedGrid = BuildDetailGrid("PartnerConsolidationForm.UnassignedGrid");
        _unassignedGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "CompanyName", DataPropertyName = "CompanyName", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "품목명", Name = "ProductName", DataPropertyName = "ProductName", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "마스터SKU", Name = "Msku", DataPropertyName = "Msku", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Quantity", DataPropertyName = "Quantity", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
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
        var unassignedTab = new TabPage("단가 미배정"); unassignedTab.Controls.Add(_unassignedGrid);
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

            var file = PartnerConsolidationFileLoader.Load(path, _channelSkuRepository, _channelConfigService);
            _files.Add(file);
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
            _files.Add(PartnerConsolidationFileLoader.Load(path, _channelSkuRepository, _channelConfigService));
        _statusLabel.Text = $"{paths.Count}개 파일을 다시 불러왔습니다. '집계 실행'을 눌러 반영하세요.";
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
        foreach (var d in result.CskuDetails.OrderBy(d => d.CompanyName, StringComparer.Ordinal).ThenBy(d => d.CskuCode, StringComparer.Ordinal))
            _cskuDetails.Add(d);

        _unassignedPriceRows.Clear();
        foreach (var d in result.CskuDetails.Where(d => d.IsPriceUnassigned))
            _unassignedPriceRows.Add(d);

        _unmappedExcludedRows.Clear();
        foreach (var row in allRows.Where(r => r.Kind != PartnerConsolidationRowKind.Mapped))
            _unmappedExcludedRows.Add(row);

        _statusLabel.Text = $"집계 완료 — 거래처 {_companySummaries.Count}곳, CSKU {_cskuDetails.Count}건 " +
            $"(단가 미배정 {_unassignedPriceRows.Count}건, 미매핑·제외 {_unmappedExcludedRows.Count}건).";

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
}
