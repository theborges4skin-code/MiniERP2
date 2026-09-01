using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// ⚠ 임시(실험용) 화면 — 문서관리_메인창_통합_견적서출력_기획.md 참고. 기존 문서관리 기능
/// (DocsForm/PriceQuoteForm 등)과 완전히 독립된 <see cref="DocLineHistoryRepository"/> 위에서
/// 채널×CSKU×기간 통합 조회를 단독으로 개발·검증하는 화면이다. 레벨1(CSKU별 요약)에서 CSKU를
/// 골라 레벨2(개별 이력)에서 "견적서 담기"로 장바구니에 모으고, 장바구니에서 바로 견적서를
/// 발행할 수 있다(<see cref="QuoteExportDialog"/>). 검증이 끝나면 실제 문서관리 기능에 편입하거나
/// 이 화면 자체를 폐기한다.
/// </summary>
public class DocLineHistoryForm : Form
{
    private readonly DocLineHistoryRepository _repo = new();
    private readonly SalesChannelRepository _channelRepo = new();
    private readonly ChannelSkuRepository _cskuRepo = new();

    private ComboBox _channelCombo = new();
    private CheckBox _hideOneOffChannelsCheck = new();
    private TextBox _cskuFilterBox = new();
    private ComboBox _docTypeCombo = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();

    private DataGridView _summaryGrid = new();
    private DataGridView _detailGrid = new();
    private ListBox _cartList = new();
    private Label _cartStatusLabel = new();
    private Label _statusLabel = new();

    private List<DocLineHistoryCskuSummary> _summaryRows = new();
    private DocLineHistoryCskuSummary? _selectedSummary;
    private readonly List<QuoteCartLine> _cart = new();

    private static readonly string[] DocTypeLabels = { "(전체)", "견적", "거래명세표", "가격조정" };

    /// <summary>채널 콤보 맨 아래 "+ 신규채널 등록..." 항목을 구분하는 가짜 채널코드.</summary>
    private const string NewChannelSentinelCode = "__NEW_CHANNEL__";

    /// <summary>신규채널 등록을 취소했을 때 되돌아갈, 직전에 실제로 선택돼 있던 채널.</summary>
    private string? _lastValidChannelCode = "";

    public DocLineHistoryForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        Load += (s, e) => OnQueryClick(this, EventArgs.Empty);
    }

    private void InitializeComponent()
    {
        Text = "(임시) 문서이력 통합조회 — 채널×CSKU×기간";
        Size = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        mainLayout.Controls.Add(BuildToolbar(), 0, 0);
        mainLayout.Controls.Add(BuildSummaryGroup(), 0, 1);
        mainLayout.Controls.Add(BuildDetailGroup(), 0, 2);
        mainLayout.Controls.Add(BuildCartGroup(), 0, 3);

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "조회 버튼을 눌러 문서 이력을 불러오세요.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };
        mainLayout.Controls.Add(_statusLabel, 0, 4);

        Controls.Add(mainLayout);
    }

    private Control BuildToolbar()
    {
        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        _channelCombo = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(SalesChannel.ChannelName) };
        // 전화주문 등 1회성 거래처도 실제 채널로 등록하는 걸 권장하기로 했으나(사용자 상담), 그러면
        // 채널 목록이 계속 늘어나 콤보가 지저분해질 수 있다는 우려가 있었다 — 문서 1건뿐인(=한 번도
        // 재주문이 없었던) 채널을 걸러 보여주는 체크박스로 대응한다(GetDocCountByChannel 참고).
        _hideOneOffChannelsCheck = new CheckBox { Text = "1회성 채널 숨기기", AutoSize = true, Padding = new Padding(4, 5, 0, 0) };
        _hideOneOffChannelsCheck.CheckedChanged += (s, e) => LoadChannelCombo(_lastValidChannelCode);
        LoadChannelCombo();
        _channelCombo.SelectedIndexChanged += OnChannelComboSelectedIndexChanged;

        _cskuFilterBox = new TextBox { Width = 110, PlaceholderText = "CSKU 포함검색" };

        _docTypeCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        _docTypeCombo.Items.AddRange(DocTypeLabels);
        _docTypeCombo.SelectedIndex = 0;

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        var btnQuickDate = DateRangeQuickSelect.CreateButton(_fromDatePicker, _toDatePicker);
        DateRangeQuickSelect.AddExtraItem(btnQuickDate, "전체기간", (_, _) =>
        {
            _fromDatePicker.Value = new DateTime(2000, 1, 1);
            _toDatePicker.Value = DateTime.Today;
        });

        var btnQuery = new Button { Text = "조회", Size = new Size(70, 28) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 28) };
        var btnSeedSample = new Button { Text = "샘플 데이터 채우기(테스트용)", Size = new Size(180, 28) };
        var btnOpenDocsForm = new Button { Text = "전체 문서 작성 화면 열기", Size = new Size(150, 28) };
        btnQuery.Click += OnQueryClick;
        btnExport.Click += OnExportClick;
        btnSeedSample.Click += OnSeedSampleClick;
        // 거래명세표/가격조정/매출장, 그리고 CSKU 없는 자유 품목 위주 견적서처럼 정형화되지 않은
        // 케이스는 계속 기존 DocsForm이 맡는다(문서관리_메인창_통합_견적서출력_기획.md §2.2).
        btnOpenDocsForm.Click += (s, e) => FormManager.Show<DocsForm>();

        toolStrip.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolStrip.Controls.Add(_channelCombo);
        toolStrip.Controls.Add(_hideOneOffChannelsCheck);
        toolStrip.Controls.Add(new Label { Text = "CSKU:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_cskuFilterBox);
        toolStrip.Controls.Add(new Label { Text = "문서유형:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_docTypeCombo);
        toolStrip.Controls.Add(new Label { Text = "귀속일:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_fromDatePicker);
        toolStrip.Controls.Add(new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        toolStrip.Controls.Add(_toDatePicker);
        toolStrip.Controls.Add(btnQuickDate);
        toolStrip.Controls.Add(btnQuery);
        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnSeedSample);
        toolStrip.Controls.Add(btnOpenDocsForm);
        return toolStrip;
    }

    private Control BuildSummaryGroup()
    {
        var group = new GroupBox { Text = "CSKU별 요약 — 더블클릭하면 아래 상세 이력이 뜹니다", Dock = DockStyle.Fill };

        _summaryGrid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _summaryGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelName", DataPropertyName = "ChannelName", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCodeLabel", DataPropertyName = "CskuCodeLabel", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "최근 품목명", Name = "LatestItemNameSnap", DataPropertyName = "LatestItemNameSnap", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "문서건수", Name = "DocCount", DataPropertyName = "DocCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "최초단가", Name = "FirstUnitPrice", DataPropertyName = "FirstUnitPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "최근단가", Name = "LastUnitPrice", DataPropertyName = "LastUnitPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "증감", Name = "PriceChangeLabel", DataPropertyName = "PriceChangeLabel", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "최근발행일", Name = "LastIssueDateText", DataPropertyName = "LastIssueDateText", Width = 90 }
        );
        _summaryGrid.CellDoubleClick += OnSummaryGridDoubleClick;

        group.Controls.Add(_summaryGrid);
        return group;
    }

    private Control BuildDetailGroup()
    {
        var group = new GroupBox { Text = "선택한 CSKU의 상세 이력", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        _detailGrid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        _detailGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "문서유형", Name = "DocTypeLabel", DataPropertyName = "DocTypeLabel", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "귀속일", Name = "IssueDateText", DataPropertyName = "IssueDateText", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단가", Name = "UnitPrice", DataPropertyName = "UnitPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "합계", Name = "Total", DataPropertyName = "Total", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "문서번호", Name = "DocNo", DataPropertyName = "DocNo", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "비고", Name = "Note", DataPropertyName = "Note", Width = 120, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );

        var detailToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 2, 4, 0) };
        var btnAddToCart = new Button { Text = "견적서 담기", Size = new Size(100, 26) };
        btnAddToCart.Click += OnAddToCartClick;
        detailToolbar.Controls.Add(btnAddToCart);

        layout.Controls.Add(_detailGrid, 0, 0);
        layout.Controls.Add(detailToolbar, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildCartGroup()
    {
        var group = new GroupBox { Text = "견적서 작성함 (여러 CSKU를 담아 한 번에 발행)", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));

        _cartList = new ListBox { Dock = DockStyle.Fill };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6, 4, 0, 0) };
        var btnRemove = new Button { Text = "선택 빼기", Size = new Size(120, 28) };
        var btnClear = new Button { Text = "비우기", Size = new Size(120, 28) };
        var btnCreateQuote = new Button { Text = "견적서 작성", Size = new Size(120, 32) };
        btnRemove.Click += OnRemoveFromCartClick;
        btnClear.Click += OnClearCartClick;
        btnCreateQuote.Click += OnCreateQuoteClick;
        buttonPanel.Controls.Add(btnRemove);
        buttonPanel.Controls.Add(btnClear);
        buttonPanel.Controls.Add(new Label { Height = 8 });
        buttonPanel.Controls.Add(btnCreateQuote);

        layout.Controls.Add(_cartList, 0, 0);
        layout.Controls.Add(buttonPanel, 1, 0);
        group.Controls.Add(layout);

        _cartStatusLabel = new Label { Dock = DockStyle.Bottom, Height = 18, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
        group.Controls.Add(_cartStatusLabel);
        UpdateCartDisplay();
        return group;
    }

    private static DocLineHistoryType? SelectedDocType(int comboIndex) => comboIndex switch
    {
        1 => DocLineHistoryType.Quote,
        2 => DocLineHistoryType.Statement,
        3 => DocLineHistoryType.PriceAdjustment,
        _ => null,
    };

    private string? SelectedChannelCode() =>
        _channelCombo.SelectedItem is SalesChannel ch && !string.IsNullOrEmpty(ch.ChannelCode) && ch.ChannelCode != NewChannelSentinelCode
            ? ch.ChannelCode
            : null;

    /// <summary>
    /// 채널 콤보를 (전체) + 실제 채널 + 맨 아래 "+ 신규채널 등록..." 항목으로 다시 채운다.
    /// <paramref name="selectChannelCode"/>가 주어지면 그 채널을 선택 상태로 만든다(신규 등록 직후
    /// 방금 만든 채널을 바로 보여주기 위함).
    /// </summary>
    private void LoadChannelCombo(string? selectChannelCode = null)
    {
        var channels = new List<SalesChannel> { new() { ChannelCode = "", ChannelName = "(전체)" } };
        var allChannels = _channelRepo.GetAll();
        if (_hideOneOffChannelsCheck.Checked)
        {
            // 문서 1건 이하(=한 번도 재주문이 없었던) 채널만 숨긴다. 지금 선택돼 있는 채널은
            // 조건에 걸려도 그대로 보여준다 — 체크박스를 켜는 순간 화면에서 선택이 사라지면
            // 방금 보던 채널의 조회 결과를 잃어버리게 되므로.
            var docCounts = _repo.GetDocCountByChannel();
            allChannels = allChannels
                .Where(c => docCounts.GetValueOrDefault(c.ChannelCode, 0) > 1 || c.ChannelCode == selectChannelCode)
                .ToList();
        }
        channels.AddRange(allChannels);
        channels.Add(new SalesChannel { ChannelCode = NewChannelSentinelCode, ChannelName = "+ 신규채널 등록..." });

        _channelCombo.DataSource = channels;

        var target = channels.FirstOrDefault(c => c.ChannelCode == selectChannelCode) ?? channels[0];
        _channelCombo.SelectedItem = target;
        _lastValidChannelCode = target.ChannelCode;
    }

    /// <summary>
    /// 채널 콤보 맨 아래 "+ 신규채널 등록..."을 고르면 PriceQuoteForm의 "채널 추가..."(OnAddChannelClick)와
    /// 같은 방식으로 새 채널을 만든다 — 채널설정 화면을 오가지 않고 이 화면에서 바로 만들고, 만들자마자
    /// 콤보에서 그 채널을 선택한다. 발주서/정산서 필드매핑 등 채널설정 전용 항목은 복사하지 않는다
    /// (이름만 있으면 충분한 가벼운 채널 생성).
    /// </summary>
    private void OnChannelComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_channelCombo.SelectedItem is not SalesChannel selected) return;
        if (selected.ChannelCode != NewChannelSentinelCode)
        {
            _lastValidChannelCode = selected.ChannelCode;
            return;
        }

        var existingChannels = _channelRepo.GetAll();
        using var dialog = new AddChannelDialog(existingChannels);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK)
        {
            LoadChannelCombo(_lastValidChannelCode);
            return;
        }

        var newChannelCode = ChannelCodeGenerator.GenerateNext(existingChannels.Select(c => c.ChannelCode));
        var newChannel = new SalesChannel { ChannelCode = newChannelCode, ChannelName = dialog.ChannelName };
        _channelRepo.Upsert(newChannel);

        LoadChannelCombo(newChannelCode);
        _statusLabel.Text = $"새 채널 '{dialog.ChannelName}'을(를) 추가하고 선택했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void OnQueryClick(object? sender, EventArgs e)
    {
        var channelCode = SelectedChannelCode();
        var docType = SelectedDocType(_docTypeCombo.SelectedIndex);

        _summaryRows = _repo.GetCskuSummary(channelCode: channelCode, docType: docType);

        // 귀속기간·CSKU 텍스트 필터는 요약 자체엔 없으니(전체 이력 기준 최초/최근단가를 보여줘야
        // 의미가 있어서) 화면에 보여줄 행만 걸러낸다 — 필터가 걸려도 "최초/최근단가"는 그대로
        // 전체 이력 기준으로 유지된다.
        var from = _fromDatePicker.Value.Date;
        var to = _toDatePicker.Value.Date;
        var cskuFilter = _cskuFilterBox.Text.Trim();

        var filtered = _summaryRows
            .Where(s => s.LastIssueDate.Date >= from && s.FirstIssueDate.Date <= to)
            .Where(s => string.IsNullOrEmpty(cskuFilter) || s.CskuCode.Contains(cskuFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _summaryGrid.DataSource = new BindingList<SummaryRow>(filtered.Select(s => new SummaryRow(s)).ToList());
        _detailGrid.DataSource = null;
        _selectedSummary = null;

        _statusLabel.Text = $"CSKU {filtered.Count}건 조회됨.";
    }

    private void OnSummaryGridDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_summaryGrid.Rows[e.RowIndex].DataBoundItem is not SummaryRow row) return;

        _selectedSummary = row.Source;
        var details = string.IsNullOrEmpty(row.Source.CskuCode)
            ? _repo.Query(channelCode: row.Source.ChannelCode, cskuCodeIsUnmappedOnly: true)
            : _repo.Query(channelCode: row.Source.ChannelCode, cskuCode: row.Source.CskuCode);

        _detailGrid.DataSource = new BindingList<DetailRow>(details.OrderBy(d => d.IssueDate).Select(d => new DetailRow(d)).ToList());
    }

    private void OnAddToCartClick(object? sender, EventArgs e)
    {
        if (_selectedSummary == null || _detailGrid.SelectedRows.Count == 0)
        {
            MessageBox.Show("담을 상세 이력 줄을 먼저 선택하세요(CSKU 요약행을 더블클릭해 상세를 연 뒤).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var csku = string.IsNullOrEmpty(_selectedSummary.CskuCode)
            ? null
            : _cskuRepo.GetByChannelAndCskuCode(_selectedSummary.ChannelCode, _selectedSummary.CskuCode);

        foreach (DataGridViewRow row in _detailGrid.SelectedRows)
        {
            if (row.DataBoundItem is not DetailRow detail) continue;
            _cart.Add(new QuoteCartLine
            {
                ChannelCode = _selectedSummary.ChannelCode,
                ChannelName = _selectedSummary.ChannelName,
                CskuCode = _selectedSummary.CskuCode,
                ItemName = csku?.InvoiceDisplayName ?? detail.Source.ItemNameSnap,
                Unit = csku?.Unit ?? "",
                Packing = csku?.Packing ?? "",
                UnitPrice = detail.Source.UnitPrice,
                Qty = 1,
            });
        }

        UpdateCartDisplay();
        _statusLabel.Text = $"장바구니에 {_detailGrid.SelectedRows.Count}건 담았습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void OnRemoveFromCartClick(object? sender, EventArgs e)
    {
        var indices = _cartList.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        if (indices.Count == 0) return;
        foreach (var i in indices) _cart.RemoveAt(i);
        UpdateCartDisplay();
    }

    private void OnClearCartClick(object? sender, EventArgs e)
    {
        _cart.Clear();
        UpdateCartDisplay();
    }

    private void UpdateCartDisplay()
    {
        _cartList.Items.Clear();
        foreach (var line in _cart)
        {
            var cskuLabel = string.IsNullOrEmpty(line.CskuCode) ? "(미매핑)" : line.CskuCode;
            _cartList.Items.Add($"[{line.ChannelName}] {cskuLabel} — {line.ItemName} × {line.Qty} @ {line.UnitPrice:N0}원");
        }
        var total = _cart.Sum(l => Math.Round(l.Qty * l.UnitPrice, 0, MidpointRounding.AwayFromZero));
        _cartStatusLabel.Text = $"담긴 줄 {_cart.Count}건 / 합계(세전) {total:N0}원";
    }

    private void OnCreateQuoteClick(object? sender, EventArgs e)
    {
        if (_cart.Count == 0)
        {
            MessageBox.Show("담긴 품목이 없습니다. 상세 이력에서 먼저 담아주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new QuoteExportDialog(_cart.ToList());
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.ResultDoc == null || dialog.ResultFilePath == null) return;

        // 발행에 성공했으니, 방금 나간 견적서 줄들을 그대로 이 임시 이력 표에 다시 쌓는다 — 그래야
        // 이 창의 조회 결과가 계속 최신 상태를 반영한다(문서관리_메인창_통합_견적서출력_기획.md §3.1-5).
        var docNo = _repo.GenerateNextTempQuoteNo(dialog.ResultDoc.IssueDate);
        var isVatExcluded = dialog.ResultDoc.IsVatExcluded;
        var newLines = _cart.Select(line =>
        {
            var supply = Math.Round(line.Qty * line.UnitPrice, 0, MidpointRounding.AwayFromZero);
            var tax = isVatExcluded ? Math.Round(supply * 0.1m, 0, MidpointRounding.AwayFromZero) : 0m;
            return new DocLineHistory
            {
                DocGroupKey = docNo,
                DocNo = docNo,
                DocType = DocLineHistoryType.Quote,
                ChannelCode = line.ChannelCode,
                ChannelName = line.ChannelName,
                CskuCode = line.CskuCode,
                ItemNameSnap = line.ItemName,
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                SupplyAmount = supply,
                Tax = tax,
                Total = supply + tax,
                IssueDate = dialog.ResultDoc.IssueDate,
                SourceRef = dialog.ResultFilePath,
                Note = line.Note,
                CreatedAt = DateTime.Now,
            };
        }).ToList();

        _repo.AddRange(newLines);
        _cart.Clear();
        UpdateCartDisplay();
        OnQueryClick(this, EventArgs.Empty);
        _statusLabel.Text = $"견적서 '{docNo}'를 발행하고 이력에 반영했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        if (_summaryGrid.Rows.Count == 0)
        {
            MessageBox.Show("내보낼 항목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"문서이력_CSKU요약_{DateTime.Now:yyyyMMdd}.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("CSKU별 요약");

            string[] headers = { "채널", "CSKU", "최근 품목명", "문서건수", "최초단가", "최근단가", "증감", "최근발행일" };
            for (int c = 0; c < headers.Length; c++) ws.Cells[1, c + 1].Value = headers[c];

            var rows = ((BindingList<SummaryRow>)_summaryGrid.DataSource).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var row = i + 2;
                ws.Cells[row, 1].Value = r.ChannelName;
                ws.Cells[row, 2].Value = r.CskuCodeLabel;
                ws.Cells[row, 3].Value = r.LatestItemNameSnap;
                ws.Cells[row, 4].Value = r.DocCount;
                ws.Cells[row, 5].Value = (double)r.FirstUnitPrice;
                ws.Cells[row, 6].Value = (double)r.LastUnitPrice;
                ws.Cells[row, 7].Value = r.PriceChangeLabel;
                ws.Cells[row, 8].Value = r.LastIssueDateText;
            }
            if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();

            ExportHelper.SaveExcel(package, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 실패: {ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 실제 문서관리 화면과 연결되기 전, 조회 기능(CSKU 요약·상세 드릴다운·미매핑 버킷·장바구니·
    /// 견적서 발행)이 제대로 동작하는지 바로 눈으로 확인할 수 있도록 3종 문서·여러 채널·여러
    /// 달에 걸친 합성 데이터를 채워 넣는다.
    /// </summary>
    private void OnSeedSampleClick(object? sender, EventArgs e)
    {
        if (MessageBox.Show(
                "테스트용 샘플 데이터를 추가하시겠습니까?\n(기존 임시 이력 데이터는 모두 지우고 새로 채웁니다.)",
                "샘플 데이터 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _repo.DeleteAll();

        var channels = new[] { ("CH001", "투유"), ("CH002", "푸디") };
        var cskus = new[] { ("CSKU-A", "샴푸 500ml"), ("CSKU-B", "린스 500ml"), ("", "자유품목(미매핑)") };
        var months = new[] { new DateTime(2026, 5, 15), new DateTime(2026, 6, 15), new DateTime(2026, 7, 15) };
        var docTypes = new[] { DocLineHistoryType.Quote, DocLineHistoryType.Statement, DocLineHistoryType.PriceAdjustment };

        var seed = new List<DocLineHistory>();
        var seq = 1;
        foreach (var docType in docTypes)
        foreach (var (channelCode, channelName) in channels)
        foreach (var issueDate in months)
        foreach (var (cskuCode, itemName) in cskus)
        {
            var qty = 10 + seq % 5;
            // 월이 지날수록 단가가 조금씩 오르게 해서 요약행의 "증감" 표시를 바로 확인할 수 있게 한다.
            var unitPrice = 1000 + seq % 5 * 37 + (issueDate.Month - 5) * 50;
            var supply = qty * unitPrice;
            var tax = Math.Round(supply * 0.1m, 0);
            seed.Add(new DocLineHistory
            {
                DocGroupKey = $"SEED-{docType}-{channelCode}-{issueDate:yyyyMM}",
                DocNo = $"SEED{seq:0000}",
                DocType = docType,
                ChannelCode = channelCode,
                ChannelName = channelName,
                CskuCode = cskuCode,
                ItemNameSnap = itemName,
                Qty = qty,
                UnitPrice = unitPrice,
                SupplyAmount = supply,
                Tax = tax,
                Total = supply + tax,
                IssueDate = issueDate,
                SourceRef = "(샘플 데이터 — 실제 문서 아님)",
                Note = "테스트용 샘플",
                CreatedAt = DateTime.Now,
            });
            seq++;
        }

        _repo.AddRange(seed);
        OnQueryClick(this, EventArgs.Empty);
        _statusLabel.Text = $"샘플 {seed.Count}건을 채웠습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private sealed class SummaryRow(DocLineHistoryCskuSummary source)
    {
        public DocLineHistoryCskuSummary Source { get; } = source;
        public string ChannelName { get; } = source.ChannelName;
        public string CskuCodeLabel { get; } = string.IsNullOrEmpty(source.CskuCode) ? "(미매핑)" : source.CskuCode;
        public string LatestItemNameSnap { get; } = source.LatestItemNameSnap;
        public int DocCount { get; } = source.DocCount;
        public decimal FirstUnitPrice { get; } = source.FirstUnitPrice;
        public decimal LastUnitPrice { get; } = source.LastUnitPrice;
        public string PriceChangeLabel { get; } = source.PriceChange switch
        {
            > 0 => $"▲{source.PriceChange:N0}",
            < 0 => $"▼{-source.PriceChange:N0}",
            _ => "-",
        };
        public string LastIssueDateText { get; } = source.LastIssueDate.ToString("yyyy-MM-dd");
    }

    private sealed class DetailRow(DocLineHistory source)
    {
        public DocLineHistory Source { get; } = source;
        public string DocTypeLabel { get; } = source.DocType switch
        {
            DocLineHistoryType.Quote => "견적",
            DocLineHistoryType.Statement => "거래명세표",
            DocLineHistoryType.PriceAdjustment => "가격조정",
            _ => source.DocType.ToString(),
        };
        public string IssueDateText { get; } = source.IssueDate.ToString("yyyy-MM-dd");
        public decimal Qty { get; } = source.Qty;
        public decimal UnitPrice { get; } = source.UnitPrice;
        public decimal Total { get; } = source.Total;
        public string DocNo { get; } = source.DocNo;
        public string Note { get; } = source.Note;
    }
}
