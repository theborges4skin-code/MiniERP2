using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MiniERP2.Forms;

/// <summary>
/// 운송장 결과 파일에서 발견됐지만 발주/출고 이력에 아직 없는 건(OFS 누락건)을 검토하고, OFS 창으로
/// 골라서 보내는 비모달 뷰어입니다(운송장파일_미매칭건_OFS역등록_검토_rev2.md §3.2). 등록 자체는 여기서
/// 하지 않고 — 행을 OFS 그리드에 주입만 하면, 채널 지정·CSKU 매핑·발주확정은 기존 OFS 창에서
/// 그대로 진행합니다.
/// </summary>
public class TrackingBackfillViewer : Form
{
    private readonly OutboundRepository _outboundRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();

    private List<TrackingBackfillRow> _allRows = [];
    private ExcelLikeDataGridView _grid = new();
    private ExcelLikeDataGridView _statsGrid = new();
    private ComboBox _labelFilterCombo = new();
    private CheckBox _chkUnregisteredOnly = new();
    private CheckBox _chkExcludeOnline = new();
    private Label _summaryLabel = new();
    private Label _statsHintLabel = new();

    public TrackingBackfillViewer()
    {
        InitializeComponent();
    }

    /// <summary>OutboundHistoryForm에서 파일을 읽고 미매칭 판정까지 마친 뒤 이 창을 열 때 호출합니다.</summary>
    public void LoadRows(List<TrackingBackfillRow> rows)
    {
        _allRows = rows;
        RebuildLabelFilterItems();
        ApplyFilter();
    }

    private void InitializeComponent()
    {
        Text = "운송장 파일 누락건 점검";
        Size = new Size(1100, 700);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

        // ── 툴바 ──────────────────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        toolbar.Controls.Add(new Label { Text = "라벨:", AutoSize = true, Padding = new Padding(0, 7, 2, 0) });
        _labelFilterCombo = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        _labelFilterCombo.SelectedIndexChanged += (_, _) => ApplyFilter();
        toolbar.Controls.Add(_labelFilterCombo);

        _chkUnregisteredOnly = new CheckBox { Text = "미등록만 보기", AutoSize = true, Checked = true, Padding = new Padding(10, 4, 0, 0) };
        _chkUnregisteredOnly.CheckedChanged += (_, _) => ApplyFilter();
        toolbar.Controls.Add(_chkUnregisteredOnly);

        // 주문번호메모가 "#"으로 시작하는 행(온라인주문 — 자동발주 채널로 이미 정상 처리되는 게
        // 보통이라 이 화면이 다루는 "진짜 누락건" 검토와는 관계가 적음)을 그리드와 통계 양쪽에서
        // 뺄 수 있게 한다.
        _chkExcludeOnline = new CheckBox { Text = "온라인주문 제외", AutoSize = true, Padding = new Padding(10, 4, 0, 0) };
        _chkExcludeOnline.CheckedChanged += (_, _) => ApplyFilter();
        toolbar.Controls.Add(_chkExcludeOnline);

        var btnSendToOfs = new Button { Text = "OFS로 보내기", Size = new Size(110, 30), Margin = new Padding(20, 0, 0, 0) };
        btnSendToOfs.Click += OnSendToOfsClick;
        toolbar.Controls.Add(btnSendToOfs);

        var btnAddChannel = new Button { Text = "채널 추가", Size = new Size(90, 30) };
        btnAddChannel.Click += OnAddChannelClick;
        toolbar.Controls.Add(btnAddChannel);

        var btnRefresh = new Button { Text = "새로고침(등록상태)", Size = new Size(130, 30) };
        btnRefresh.Click += OnRefreshClick;
        toolbar.Controls.Add(btnRefresh);

        var btnExport = new Button { Text = "운임통계 내보내기", Size = new Size(130, 30) };
        btnExport.Click += OnExportStatsClick;
        toolbar.Controls.Add(btnExport);

        // ── 메인 그리드 ────────────────────────────────────────────────────
        _grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "접수일자", DataPropertyName = "ReceivedAt", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", DataPropertyName = "TrackingNo", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "수령인", DataPropertyName = "Recipient", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "주소", DataPropertyName = "Address", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "품목명(원문)", DataPropertyName = "ProductName", Width = 220 },
            new DataGridViewTextBoxColumn { HeaderText = "메모", DataPropertyName = "OrderNoMemo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "운임", DataPropertyName = "FreightCost", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "라벨", DataPropertyName = "Label", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "상태", DataPropertyName = "StatusText", Width = 100 }
        );
        _grid.CellDoubleClick += OnGridCellDoubleClick;
        _grid.SelectionChanged += (_, _) => RefreshStats();

        // ── 요약 라벨 ──────────────────────────────────────────────────────
        _summaryLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(5, 2, 0, 0) };

        // ── 집계 패널 ──────────────────────────────────────────────────────
        var statsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statsHintLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "택배운임 통계 — 선택한 행이 있으면 선택 행 기준, 없으면 라벨 필터 기준(등록 여부와 무관하게 파일 단위로 집계).",
            ForeColor = Color.DimGray,
            AutoSize = false,
        };

        _statsGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
        };
        _statsGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "단가", DataPropertyName = "UnitPriceLabel", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", DataPropertyName = "Count", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "금액", DataPropertyName = "Amount", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );

        statsPanel.Controls.Add(_statsHintLabel, 0, 0);
        statsPanel.Controls.Add(_statsGrid, 0, 1);

        mainLayout.Controls.Add(toolbar, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(_summaryLabel, 0, 2);
        mainLayout.Controls.Add(statsPanel, 0, 3);
        Controls.Add(mainLayout);

        Shown += OnShown;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        // §3.2: 뷰어를 열면 OFS 창을 항상 확보하고, ManualOrderDialog와 같은 방식으로 그 오른쪽에 붙인다.
        FormManager.Show<OfsForm>();
        var ofs = Application.OpenForms.OfType<OfsForm>().FirstOrDefault();
        if (ofs != null)
        {
            Location = new Point(ofs.Right + 4, ofs.Top);
        }
    }

    private void RebuildLabelFilterItems()
    {
        var labels = _allRows.Select(r => r.Label).Distinct().OrderBy(l => l).ToList();
        _labelFilterCombo.Items.Clear();
        _labelFilterCombo.Items.Add("전체");
        foreach (var l in labels) _labelFilterCombo.Items.Add(l);
        _labelFilterCombo.SelectedIndex = 0;
    }

    /// <summary>라벨 필터 + 온라인주문 제외만 반영한다(미등록만 보기는 뺀다 — 운임 통계는 등록
    /// 여부와 무관하게 파일 단위로 봐야 하므로, 그리드용 최종 필터와 통계용 기준 집합을 분리한다).</summary>
    private IEnumerable<TrackingBackfillRow> RowsMatchingBaseFilters()
    {
        IEnumerable<TrackingBackfillRow> rows = _allRows;
        if (_chkExcludeOnline.Checked) rows = rows.Where(r => r.Label != TrackingLabelClassifier.OnlineOrder);

        var selectedLabel = _labelFilterCombo.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedLabel) && selectedLabel != "전체") rows = rows.Where(r => r.Label == selectedLabel);

        return rows;
    }

    private void ApplyFilter()
    {
        var filtered = RowsMatchingBaseFilters();
        if (_chkUnregisteredOnly.Checked) filtered = filtered.Where(r => !r.IsRegistered);

        var list = filtered.ToList();
        _grid.DataSource = new BindingList<TrackingBackfillRow>(list);

        var registeredCount = _allRows.Count(r => r.IsRegistered);
        var missingCount = _allRows.Count(r => !r.IsRegistered);
        _summaryLabel.Text = $"전체 {_allRows.Count}건 (미등록 {missingCount}건 / 등록됨 {registeredCount}건) — 현재 목록 {list.Count}건";

        RefreshStats();
    }

    private void RefreshStats()
    {
        // 행 하나를 클릭해 내용만 살펴보는(단순 커서 이동) 흔한 조작까지 "선택 기준 통계"로
        // 잡히면 통계가 그 한 줄로 줄어들어 사용자가 오작동으로 오해한다 — 2건 이상을 일부러
        // 골랐을 때만 "선택 행 기준"으로 전환한다.
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as TrackingBackfillRow)
            .Where(r => r != null)
            .Cast<TrackingBackfillRow>()
            .ToList();

        var source = selected.Count > 1 ? selected : RowsMatchingBaseFilters().ToList();
        _statsGrid.DataSource = new BindingList<FreightStatRow>(BuildFreightStats(source));
        _statsHintLabel.Text = selected.Count > 1
            ? $"택배운임 통계 — 선택한 {selected.Count}건 기준."
            : "택배운임 통계 — 라벨/온라인주문 제외 필터 기준(등록 여부와 무관하게 파일 단위로 집계). 2건 이상 선택하면 그 선택 기준으로 바뀝니다.";
    }

    private static List<FreightStatRow> BuildFreightStats(IEnumerable<TrackingBackfillRow> rows)
    {
        var withFreight = rows.Where(r => r.FreightCost > 0).ToList();
        var result = withFreight
            .GroupBy(r => r.FreightCost)
            .OrderBy(g => g.Key)
            .Select(g => new FreightStatRow { UnitPriceLabel = g.Key.ToString("N0"), Count = g.Count(), Amount = g.Sum(r => r.FreightCost) })
            .ToList();

        if (result.Count > 0)
        {
            result.Add(new FreightStatRow { UnitPriceLabel = "총계", Count = withFreight.Count, Amount = withFreight.Sum(r => r.FreightCost) });
        }
        return result;
    }

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not TrackingBackfillRow row) return;
        SendToOfs([row]);
    }

    private void OnSendToOfsClick(object? sender, EventArgs e)
    {
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as TrackingBackfillRow)
            .Where(r => r != null)
            .Cast<TrackingBackfillRow>()
            .ToList();

        if (rows.Count == 0)
        {
            MessageBox.Show("OFS로 보낼 행을 먼저 선택하세요(더블클릭도 가능합니다).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SendToOfs(rows);
    }

    private void SendToOfs(List<TrackingBackfillRow> rows)
    {
        FormManager.Show<OfsForm>();
        var ofs = Application.OpenForms.OfType<OfsForm>().FirstOrDefault();
        if (ofs == null) return;

        var items = rows.Select(r => new OfsOrderItem
        {
            TrackingNo = r.TrackingNo,
            ShipmentGroupId = $"TRK|{r.TrackingNo}",
            OrderNo = $"TRK-{r.TrackingNo}",
            Recipient = r.Recipient,
            Address = r.Address,
            ProductName = r.ProductName,
            Quantity = 1,
            Remark = $"운송장파일 소급 {r.SourceFileName} {r.SourceRowNumber}행",
        }).ToList();

        ofs.AddLoadedOrders(items);

        foreach (var r in rows) r.JustSent = true;
        _grid.Refresh();

        ofs.BringToFront();
    }

    private void OnAddChannelClick(object? sender, EventArgs e)
    {
        var existingChannels = _salesChannelRepository.GetAll();
        using var dialog = new AddChannelDialog(existingChannels);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        var newChannelCode = ChannelCodeGenerator.GenerateNext(existingChannels.Select(c => c.ChannelCode));
        _salesChannelRepository.Upsert(new SalesChannel { ChannelCode = newChannelCode, ChannelName = dialog.ChannelName });

        MessageBox.Show(
            $"채널 '{dialog.ChannelName}'을(를) 추가했습니다.\nOFS 창의 채널 콤보에 바로 안 보이면 OFS 창을 닫았다 다시 열어주세요.",
            "채널 추가 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        var existing = _outboundRepository.GetExistingTrackingNos(_allRows.Select(r => r.TrackingNo));
        foreach (var row in _allRows)
        {
            row.IsRegistered = existing.Contains(row.TrackingNo);
            if (row.IsRegistered) row.JustSent = false;
        }
        ApplyFilter();
    }

    private void OnExportStatsClick(object? sender, EventArgs e)
    {
        var stats = (_statsGrid.DataSource as BindingList<FreightStatRow>)?.ToList() ?? [];
        if (stats.Count == 0)
        {
            MessageBox.Show("내보낼 운임 통계가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"택배운임통계_{DateTime.Now:yyyyMMdd}.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("운임통계");
            string[] headers = ["단가", "건수", "금액"];
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cells[1, c + 1];
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 114, 196));
                cell.Style.Font.Color.SetColor(Color.White);
            }
            for (int r = 0; r < stats.Count; r++)
            {
                ws.Cells[r + 2, 1].Value = stats[r].UnitPriceLabel;
                ws.Cells[r + 2, 2].Value = stats[r].Count;
                ws.Cells[r + 2, 3].Value = stats[r].Amount;
            }
            if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private class FreightStatRow
    {
        public string UnitPriceLabel { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Amount { get; set; }
    }
}
