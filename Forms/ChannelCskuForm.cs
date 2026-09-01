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
/// 거래처(채널) 하나를 먼저 고른 뒤 그 채널이 취급하는 CSKU 전체를 한 번에 조회·수정하는 화면.
/// 기존 `CSkuForm`("채널 SKU 관리")은 반대 방향(마스터SKU 1개 → 그 SKU를 파는 여러 채널)이라,
/// "이 거래처가 취급하는 품목·단가를 쭉 보고 싶다"는 요구에는 맞지 않아 별도로 신설했다. 저장/삭제/
/// 이력조회/엑셀내보내기 로직은 CSkuForm과 동일한 ChannelSkuRepository를 그대로 재사용한다.
/// </summary>
public class ChannelCskuForm : Form
{
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly SettingsService _settingsService = new();

    private ComboBox _channelCombo = new();
    private ExcelLikeDataGridView _cskuGrid = new();
    private BindingList<ChannelSkuModel> _cskus = new();
    private Label _statusLabel = new();

    // "제조원가"는 ChannelSkuModel에 없는 필드(마스터SKU=ItemTable 소속 값)라 그리드 열에는 직접
    // 바인딩할 수 없다 — Msku 기준으로 옆에서 캐시해뒀다가 CellFormatting/저장 시 반영한다
    // (OutboundHistoryForm의 FreightCost 열과 같은 관례).
    private Dictionary<string, decimal> _costPriceByMsku = new();
    private readonly HashSet<string> _dirtyCostMskus = [];

    private readonly string? _initialChannelCode;

    public ChannelCskuForm() : this(null) { }

    /// <summary>거래처 마감보드의 수동 주문 추가 등, 이미 채널이 정해진 화면에서 "CSKU가 없으면
    /// 바로 등록창 열기"로 진입할 때 그 채널을 미리 선택해둔다.</summary>
    public ChannelCskuForm(string? initialChannelCode)
    {
        _initialChannelCode = initialChannelCode;
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        LoadChannelCombo();
    }

    private void InitializeComponent()
    {
        Text = "거래처별 CSKU 관리";
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            RowStyles = { new RowStyle(SizeType.Absolute, 40), new RowStyle(SizeType.Percent, 100) },
        };

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        _channelCombo = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(SalesChannel.ChannelName) };
        _channelCombo.SelectedIndexChanged += (s, e) => LoadData();

        var btnAddCsku = new Button { Text = "CSKU 추가", Size = new Size(90, 30) };
        btnAddCsku.Click += OnAddCskuClick;
        var btnDeleteCsku = new Button { Text = "CSKU 삭제", Size = new Size(90, 30) };
        btnDeleteCsku.Click += OnDeleteCskuClick;
        var btnSave = new Button { Text = "저장", Size = new Size(90, 30) };
        btnSave.Click += OnSaveClick;
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };
        btnExport.Click += OnExportClick;
        var btnFindOrphans = new Button { Text = "마스터SKU 미등록 CSKU 찾기", AutoSize = true };
        btnFindOrphans.Click += OnFindOrphanCskuClick;

        toolStrip.Controls.Add(new Label { Text = "거래처:", AutoSize = true, Padding = new Padding(0, 7, 2, 0) });
        toolStrip.Controls.Add(_channelCombo);
        toolStrip.Controls.Add(btnAddCsku);
        toolStrip.Controls.Add(btnDeleteCsku);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnFindOrphans);
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.DarkGreen };
        toolStrip.Controls.Add(_statusLabel);

        _cskuGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "ChannelCskuForm.CskuGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        _cskuGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU 코드", DataPropertyName = "CskuCode", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "Msku", HeaderText = "마스터SKU", DataPropertyName = "Msku", Width = 130 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "제조원가", DataPropertyName = string.Empty, Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewCheckBoxColumn { Name = "IsOverride", HeaderText = "개별관리", DataPropertyName = string.Empty, Width = 60 },
            new DataGridViewTextBoxColumn { Name = "InvoiceDisplayName", HeaderText = "송장표시명", DataPropertyName = "InvoiceDisplayName", Width = 180 },
            new DataGridViewTextBoxColumn { Name = "SupplyPrice", HeaderText = "납품가", DataPropertyName = "SupplyPrice", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "단위", DataPropertyName = "Unit", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Packing", HeaderText = "포장단위", DataPropertyName = "Packing", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "비고", DataPropertyName = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "UpdatedAt", HeaderText = "마지막 수정", DataPropertyName = "UpdatedAt", Width = 130, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm" } }
        );

        // 마스터SKU 칸 편집이 끝났을 때 CSKU 코드가 비어있으면 "채널명 앞 3글자_마스터SKU" 기본값을
        // 제안한다(CSkuForm의 ChannelCode 트리거와 같은 관례 — 여기선 채널이 이미 고정이라 Msku가 트리거).
        _cskuGrid.CellEndEdit += OnCskuGridCellEndEdit;
        _cskuGrid.CellFormatting += OnCskuGridCellFormatting;
        _cskuGrid.CellBeginEdit += OnCskuGridCellBeginEdit;
        _cskuGrid.CellValueChanged += OnCskuGridCellValueChanged;
        _cskuGrid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            // 체크박스 열은 클릭 즉시 커밋해야 CellValueChanged가 바로 발생한다(WinForms 관례).
            if (_cskuGrid.CurrentCell is DataGridViewCheckBoxCell && _cskuGrid.IsCurrentCellDirty)
                _cskuGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        SetupContextMenu();
        _cskuGrid.UserDeletingRow += OnUserDeletingRow;

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_cskuGrid, 0, 1);
        Controls.Add(mainLayout);

        FormClosing += (s, e) => _cskuGrid.SaveLayout();
    }

    private void SetupContextMenu()
    {
        var historyMenuItem = new ToolStripMenuItem("변경 이력 보기(&H)");
        historyMenuItem.Click += OnHistoryMenuItemClick;

        // TempSkuGenerator로 임시 등록된(예: "TEMP004") 마스터SKU를 실제 카탈로그 SKU로 정식
        // 교체하기 위한 진입점(§3 — "정식 등록").
        var assignMasterSkuItem = new ToolStripMenuItem("마스터SKU 지정/변경(정식 등록)...");
        assignMasterSkuItem.Click += OnAssignMasterSkuClick;

        _cskuGrid.ContextMenuStrip!.Items.Add(new ToolStripSeparator());
        _cskuGrid.ContextMenuStrip.Items.Add(historyMenuItem);
        _cskuGrid.ContextMenuStrip.Items.Add(assignMasterSkuItem);
        _cskuGrid.ContextMenuStrip.Opening += (s, e) =>
        {
            historyMenuItem.Enabled = _cskuGrid.SelectedRows.Count == 1;
            assignMasterSkuItem.Enabled = _cskuGrid.SelectedRows.Count == 1;
        };
    }

    /// <summary>
    /// "임시 SKU 등록"으로 급하게 만든 CSKU(코드도 "채널_TEMP005"처럼 임시 형태)를 정식 마스터SKU에
    /// 연결하고, 필요하면 CSKU 코드도 정식 값으로 함께 바꾼다. 기본키 변경이 걸려있어 그리드의
    /// 일반 [저장] 흐름(Upsert)으로는 처리할 수 없으므로, 여기서는 확인 즉시 RenameCsku로 반영하고
    /// 그 코드를 가리키던 매핑 규칙도 RetargetRules로 함께 옮긴다.
    /// </summary>
    private void OnAssignMasterSkuClick(object? sender, EventArgs e)
    {
        if (_cskuGrid.SelectedRows.Count != 1) return;
        var selectedRow = _cskuGrid.SelectedRows[0];
        if (selectedRow.IsNewRow) return;
        if (selectedRow.DataBoundItem is not ChannelSkuModel csku) return;

        var channel = SelectedChannel;
        if (channel == null) return;

        using var dlg = new AssignMasterSkuDialog(csku.Msku, csku.InvoiceDisplayName, csku.CskuCode, channel.ChannelName);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.SelectedSku == null) return;

        var oldCskuCode = csku.CskuCode;
        var codeChanged = !string.Equals(oldCskuCode, dlg.SelectedCskuCode, StringComparison.Ordinal);
        var updated = new ChannelSkuModel
        {
            ChannelCode = csku.ChannelCode,
            CskuCode = dlg.SelectedCskuCode,
            Msku = dlg.SelectedSku,
            SupplyPrice = csku.SupplyPrice,
            InvoiceDisplayName = csku.InvoiceDisplayName,
            Note = csku.Note,
            Unit = csku.Unit,
            Packing = csku.Packing,
            CostPriceOverride = csku.CostPriceOverride,
        };

        try
        {
            _cskuRepository.RenameCsku(channel.ChannelCode, oldCskuCode, updated);
            if (codeChanged) _mappingRepository.RetargetRules(channel.ChannelCode, oldCskuCode, updated.CskuCode);

            LoadData();
            SelectRowByCskuCode(updated.CskuCode);
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = codeChanged
                ? $"'{oldCskuCode}' → '{updated.CskuCode}'(으)로 정식 등록하고 마스터SKU '{dlg.SelectedSku}'를 연결했습니다."
                : $"마스터SKU를 '{dlg.SelectedSku}'(으)로 지정했습니다.";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = $"저장 중 오류가 발생했습니다: {ex.Message}";
        }
    }

    /// <summary>
    /// 그리드 맨 아래 빈 행에 직접 타이핑해도 CSKU 등록은 되지만, 마스터SKU를 검색하거나 그
    /// 자리에서 새로 만들 수가 없어 매번 마스터SKU 관리창을 오가야 했다(사용자 요청 — 마스터SKU
    /// 관리의 "새 마스터SKU 추가"와 같은 이유). NewChannelCskuDialog로 마스터SKU 지정/신규등록과
    /// CSKU 정보 입력을 한 창에서 받아 즉시 등록한다(Upsert는 바로 반영, 그리드 "저장" 버튼을
    /// 기다릴 필요 없음).
    /// </summary>
    private void OnAddCskuClick(object? sender, EventArgs e)
    {
        var channel = SelectedChannel;
        if (channel == null)
        {
            MessageBox.Show("거래처를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new NewChannelCskuDialog(channel.ChannelCode, channel.ChannelName);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.SelectedMasterSku == null) return;

        _cskuRepository.Upsert(new ChannelSkuModel
        {
            ChannelCode = channel.ChannelCode,
            CskuCode = dlg.CskuCode,
            Msku = dlg.SelectedMasterSku,
            SupplyPrice = dlg.SupplyPrice,
            InvoiceDisplayName = dlg.InvoiceDisplayName,
            Unit = dlg.Unit,
            Packing = dlg.Packing,
            Note = dlg.Note,
        });

        LoadData();
        SelectRowByCskuCode(dlg.CskuCode);
        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"CSKU '{dlg.CskuCode}'를 추가했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>
    /// 채널 불문하고 마스터SKU가 등록되지 않은(=원가를 조회할 수 없는) CSKU를 전부 찾아 보여준다
    /// (§ 개선방안 4 — 기존 orphan 정리용 진단 도구). 목록에서 고르면 그 채널로 바로 이동한다.
    /// </summary>
    private void OnFindOrphanCskuClick(object? sender, EventArgs e)
    {
        var validMskus = _itemRepository.GetAll().Select(i => i.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = _cskuRepository.GetAll().Where(c => !validMskus.Contains(c.Msku)).ToList();

        if (orphans.Count == 0)
        {
            MessageBox.Show("마스터SKU가 등록되지 않은 CSKU가 없습니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new OrphanCskuFinderDialog(orphans, _salesChannelRepository.GetAll());
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.SelectedChannelCode == null || dlg.SelectedCskuCode == null) return;

        if (_channelCombo.DataSource is List<SalesChannel> channels)
        {
            var channel = channels.FirstOrDefault(c => c.ChannelCode == dlg.SelectedChannelCode);
            if (channel != null) _channelCombo.SelectedItem = channel;
        }
        SelectRowByCskuCode(dlg.SelectedCskuCode);
    }

    private void SelectRowByCskuCode(string cskuCode)
    {
        foreach (DataGridViewRow row in _cskuGrid.Rows)
        {
            if (row.DataBoundItem is not ChannelSkuModel csku || csku.CskuCode != cskuCode) continue;
            _cskuGrid.ClearSelection();
            row.Selected = true;
            _cskuGrid.CurrentCell = row.Cells[0];
            _cskuGrid.FirstDisplayedScrollingRowIndex = row.Index;
            break;
        }
    }

    private void OnHistoryMenuItemClick(object? sender, EventArgs e)
    {
        if (_cskuGrid.SelectedRows.Count != 1) return;
        var selectedRow = _cskuGrid.SelectedRows[0];
        if (selectedRow.IsNewRow) return;

        if (selectedRow.DataBoundItem is not ChannelSkuModel csku || string.IsNullOrWhiteSpace(csku.CskuCode))
        {
            MessageBox.Show("CSKU 코드가 없는 항목은 이력을 조회할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var historyForm = new ChannelSkuHistoryForm(csku.ChannelCode, csku.CskuCode);
        FormManager.ApplyBoundsTracking(historyForm);
        FormManager.ShowDialogSafe(historyForm, this);
    }

    private void LoadChannelCombo()
    {
        var channels = _salesChannelRepository.GetAll().OrderBy(c => c.ChannelName).ToList();
        _channelCombo.DataSource = channels;

        var preselect = _initialChannelCode != null ? channels.FirstOrDefault(c => c.ChannelCode == _initialChannelCode) : null;
        if (preselect != null) _channelCombo.SelectedItem = preselect;
        else if (channels.Count > 0) _channelCombo.SelectedIndex = 0;
        else LoadData();
    }

    private SalesChannel? SelectedChannel => _channelCombo.SelectedItem as SalesChannel;

    private void LoadData()
    {
        var channel = SelectedChannel;
        if (channel == null)
        {
            _cskus = new BindingList<ChannelSkuModel>();
            _cskuGrid.DataSource = _cskus;
            return;
        }

        _cskus = new BindingList<ChannelSkuModel>(_cskuRepository.GetAllByChannel(channel.ChannelCode));
        _cskuGrid.DataSource = _cskus;
        _dirtyCostMskus.Clear();
        _costPriceByMsku = _cskus
            .Select(c => c.Msku)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToDictionary(m => m, m => _itemRepository.GetBySku(m)?.CostPrice ?? 0m);
        _statusLabel.Text = $"{channel.ChannelName} — CSKU {_cskus.Count}건";
        _statusLabel.ForeColor = Color.DarkGreen;
    }

    private void OnCskuGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _cskuGrid.Rows.Count) return;
        if (_cskuGrid.Rows[e.RowIndex].DataBoundItem is not ChannelSkuModel csku) return;
        var columnName = _cskuGrid.Columns[e.ColumnIndex].Name;

        if (columnName == "IsOverride")
        {
            e.Value = csku.CostPriceOverride.HasValue;
            return;
        }

        if (columnName != "CostPrice") return;

        // FormattingApplied=true는 e.Value가 "이미 완성된 표시용 문자열"일 때만 써야 한다 — 이
        // 열은 DefaultCellStyle.Format="N0"로 숫자 서식을 그리드가 대신 입혀야 하므로, 원시
        // decimal 값만 넣고 FormattingApplied는 켜지 않는다(켜면 FormatException 발생).
        var masterCost = _costPriceByMsku.TryGetValue(csku.Msku, out var cost) ? cost : 0m;
        e.Value = csku.CostPriceOverride ?? masterCost;

        // 마스터 연동 상태(개별관리 미체크)면 회색으로 표시해 "마스터 공유 값"임을 눈에 띄게 한다.
        if (e.CellStyle != null) e.CellStyle.ForeColor = csku.CostPriceOverride.HasValue ? Color.Black : Color.Gray;
    }

    /// <summary>
    /// 개별관리(체크 해제 상태)인 CSKU의 제조원가를 직접 고치려 하면, 그 값이 마스터DB 공유
    /// 원가라서 저장 시 같은 마스터SKU를 쓰는 다른 채널/CSKU까지 함께 바뀐다는 것을 먼저 알리고
    /// 선택하게 한다(CSKU제조원가_개별관리_개발기획서.md §4.5, 부작용 2.3-①·② 대응).
    /// </summary>
    private void OnCskuGridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (_cskuGrid.Columns[e.ColumnIndex].Name != "CostPrice") return;
        if (e.RowIndex < 0 || e.RowIndex >= _cskuGrid.Rows.Count) return;
        if (_cskuGrid.Rows[e.RowIndex].DataBoundItem is not ChannelSkuModel csku) return;
        if (csku.CostPriceOverride.HasValue) return; // 이미 개별관리 상태 — 그대로 편집 허용

        e.Cancel = true;
        if (string.IsNullOrWhiteSpace(csku.Msku)) return;

        var masterCost = _costPriceByMsku.GetValueOrDefault(csku.Msku, 0m);
        var affected = _cskuRepository.GetAllByMsku(csku.Msku);
        var channelCount = affected.Select(a => a.ChannelCode).Distinct().Count();

        var choice = MessageBox.Show(
            $"이 값은 마스터DB 공유 원가입니다. 저장하면 이 마스터SKU('{csku.Msku}')를 사용하는 " +
            $"{channelCount}개 채널 / {affected.Count}개 CSKU의 원가가 함께 바뀌고, 아직 원가 스냅샷이 없는 " +
            "미확정 출고 라인의 손익도 재계산됩니다.\n\n" +
            "[예] 마스터 원가를 변경합니다\n[아니오] 이 CSKU만 개별관리로 전환합니다\n[취소] 아무 것도 하지 않습니다",
            "마스터 원가 수정", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        if (choice == DialogResult.Yes)
        {
            using var prompt = new SimpleTextPromptDialog("마스터 원가 변경",
                $"'{csku.Msku}' 마스터 원가 (현재 {masterCost:N0}원):", masterCost.ToString("0.####"), ValidateNumeric);
            if (FormManager.ShowDialogSafe(prompt, this) != DialogResult.OK || !decimal.TryParse(prompt.Value, out var newCost)) return;

            _costPriceByMsku[csku.Msku] = newCost;
            _dirtyCostMskus.Add(csku.Msku);
            _cskuGrid.InvalidateRow(e.RowIndex);
        }
        else if (choice == DialogResult.No)
        {
            csku.CostPriceOverride = masterCost;
            _cskuGrid.InvalidateRow(e.RowIndex);
        }
    }

    private static string? ValidateNumeric(string value) => decimal.TryParse(value, out _) ? null : "숫자를 입력하세요.";

    /// <summary>개별관리 체크박스 토글: 켜면 현재 마스터 원가를 복사해 편집 가능 상태로 전환하고,
    /// 끄면 확인 후 마스터 연동으로 되돌린다(§4.5).</summary>
    private void OnCskuGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _cskuGrid.Rows.Count) return;
        if (_cskuGrid.Columns[e.ColumnIndex].Name != "IsOverride") return;
        if (_cskuGrid.Rows[e.RowIndex].DataBoundItem is not ChannelSkuModel csku) return;

        var isChecked = _cskuGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is true;

        if (isChecked && !csku.CostPriceOverride.HasValue)
        {
            csku.CostPriceOverride = _costPriceByMsku.GetValueOrDefault(csku.Msku, 0m);
            _cskuGrid.InvalidateRow(e.RowIndex);
        }
        else if (!isChecked && csku.CostPriceOverride.HasValue)
        {
            var masterCost = _costPriceByMsku.GetValueOrDefault(csku.Msku, 0m);
            var result = MessageBox.Show(
                $"이 CSKU의 개별 원가를 삭제하고 마스터DB 원가({masterCost:N0}원)를 따르게 합니다.\n계속하시겠습니까?",
                "연동 복귀 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                csku.CostPriceOverride = null;
            }
            // 취소 시 모델은 그대로 두고 다시 그려서 체크 표시를 원복한다(값을 직접 되돌리지 않음 —
            // CellFormatting이 모델 상태를 기준으로 항상 다시 그리므로 이 방식이 재귀 위험이 없다).
            _cskuGrid.InvalidateRow(e.RowIndex);
        }
    }

    private void OnCskuGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _cskuGrid.Rows.Count) return;
        if (_cskuGrid.Rows[e.RowIndex].DataBoundItem is not ChannelSkuModel csku) return;
        var columnName = _cskuGrid.Columns[e.ColumnIndex].Name;

        // "제조원가"는 개별관리(체크) 상태에서만 직접 편집이 여기까지 도달한다 — 연동 상태의
        // 편집 시도는 OnCskuGridCellBeginEdit에서 먼저 가로채 취소하거나 마스터/개별관리 경로로
        // 분기하기 때문이다. 개별관리 값은 ChannelSkuModel 소속이라 저장 시 CSKU Upsert에 함께 실린다.
        if (columnName == "CostPrice")
        {
            if (!csku.CostPriceOverride.HasValue) return;
            var raw = _cskuGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            csku.CostPriceOverride = decimal.TryParse(raw, out var parsed) ? parsed : 0m;
            return;
        }

        if (columnName != "Msku") return;
        if (!string.IsNullOrWhiteSpace(csku.CskuCode) || string.IsNullOrWhiteSpace(csku.Msku)) return;

        var channel = SelectedChannel;
        if (channel == null) return;

        csku.CskuCode = CskuCodeGenerator.BuildDefault(channel.ChannelName, csku.Msku);
        _cskuGrid.InvalidateRow(e.RowIndex);
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var channel = SelectedChannel;
        if (channel == null)
        {
            MessageBox.Show("거래처를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // [CSKU 추가]/[마스터SKU 지정/변경] 다이얼로그는 마스터SKU 존재 여부를 검증하지만, 그리드에
            // 직접 타이핑하는 이 저장 경로는 검증하지 않았다. 그 결과 존재하지 않는 마스터SKU 코드가
            // 그대로 CSKU에 연결되면 매핑 규칙은 정상 동작하는데 정산/이익분석에서는 원가를 찾지 못해
            // "원가 정보 없음"으로 남는 유령 CSKU가 생긴다(2026-08-03 실사례: TEMP005 → 존재하지 않는
            // "mbc200x5"로 잘못 수정되어 저장됨). 다른 두 경로와 동일하게 여기서도 저장 전에 막는다.
            var unregisteredCskus = new List<string>();
            foreach (var csku in _cskus)
            {
                if (string.IsNullOrWhiteSpace(csku.Msku)) continue; // 마스터SKU 없이는 저장 대상이 아님(신규 빈 행 등)

                if (_itemRepository.GetBySku(csku.Msku) == null)
                {
                    unregisteredCskus.Add(string.IsNullOrWhiteSpace(csku.CskuCode) ? csku.Msku : csku.CskuCode);
                    continue;
                }

                csku.ChannelCode = channel.ChannelCode;

                if (string.IsNullOrWhiteSpace(csku.CskuCode))
                    csku.CskuCode = CskuCodeGenerator.BuildDefault(channel.ChannelName, csku.Msku);

                _cskuRepository.Upsert(csku);
            }

            // 제조원가는 마스터SKU(ItemTable) 소속 값이라 CSKU Upsert와 별도로 반영한다. 아직 정식
            // 등록 안 된 임시 마스터SKU(예: TEMP004가 ItemTable에 없는 경우)는 저장할 곳이 없으므로
            // 건너뛰고 안내한다 — 먼저 [마스터SKU 지정/변경(정식 등록)]으로 실제 SKU에 연결해야 한다.
            var unregisteredMskus = new List<string>();
            foreach (var msku in _dirtyCostMskus)
            {
                var existing = _itemRepository.GetBySku(msku);
                if (existing == null) { unregisteredMskus.Add(msku); continue; }
                existing.CostPrice = _costPriceByMsku[msku];
                _itemRepository.Upsert(existing);
            }

            LoadData();
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"성공적으로 저장되었습니다. ({DateTime.Now:HH:mm:ss})";
            if (unregisteredCskus.Count > 0)
            {
                _statusLabel.ForeColor = Color.Red;
                _statusLabel.Text += $" (저장 안 됨: {string.Join(", ", unregisteredCskus)} — 등록되지 않은 마스터SKU입니다. [마스터SKU 지정/변경] 또는 [새 마스터SKU 만들기]로 먼저 등록하세요)";
            }
            if (unregisteredMskus.Count > 0)
            {
                _statusLabel.ForeColor = Color.DarkOrange;
                _statusLabel.Text += $" (제조원가 미반영: {string.Join(", ", unregisteredMskus)} — 마스터SKU 미등록, [마스터SKU 지정/변경]으로 먼저 연결하세요)";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = $"저장 중 오류가 발생했습니다: {ex.Message}";
        }
    }

    private void OnUserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
    {
        if (e.Row is null || e.Row.IsNewRow) return;
        if (e.Row.DataBoundItem is not ChannelSkuModel csku || string.IsNullOrWhiteSpace(csku.CskuCode))
            return; // 아직 저장 안 된 행은 그리드에서만 지우면 됨.

        var result = MessageBox.Show(
            $"CSKU '{csku.CskuCode}'를 삭제하시겠습니까?\n관련된 모든 가격 변경 이력도 함께 삭제됩니다.",
            "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
            _cskuRepository.Delete(csku.ChannelCode, csku.CskuCode);
        else
            e.Cancel = true;
    }

    /// <summary>
    /// 그리드 행 선택 후 Delete 키로 지우는 방법(OnUserDeletingRow)이 잘 드러나지 않아, [CSKU 추가]와
    /// 대칭으로 명시적인 버튼을 둔다. 여러 행을 한꺼번에 선택해 지울 수 있다.
    /// </summary>
    private void OnDeleteCskuClick(object? sender, EventArgs e)
    {
        var targets = _cskuGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.DataBoundItem as ChannelSkuModel)
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c!.CskuCode))
            .Cast<ChannelSkuModel>()
            .DistinctBy(c => (c.ChannelCode, c.CskuCode))
            .ToList();

        if (targets.Count == 0)
        {
            MessageBox.Show("삭제할 CSKU를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var codeList = string.Join(", ", targets.Select(c => c.CskuCode));
        var result = MessageBox.Show(
            $"CSKU {targets.Count}건을 삭제하시겠습니까?\n{codeList}\n관련된 모든 가격 변경 이력도 함께 삭제됩니다.",
            "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        foreach (var csku in targets)
        {
            _cskuRepository.Delete(csku.ChannelCode, csku.CskuCode);
        }

        LoadData();
        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"CSKU {targets.Count}건을 삭제했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        var channel = SelectedChannel;
        if (channel == null || _cskuGrid.Rows.Count == 0)
        {
            MessageBox.Show("내보낼 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"CSKU_{channel.ChannelName}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("ChannelCskuExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("ChannelCskuExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("ChannelSKU");

            string[] headers = { "CSKU 코드", "마스터SKU", "송장표시명", "납품가", "단위", "포장단위", "비고" };
            for (var i = 0; i < headers.Length; i++) worksheet.Cells[1, i + 1].Value = headers[i];

            for (var i = 0; i < _cskus.Count; i++)
            {
                var csku = _cskus[i];
                var row = i + 2;
                worksheet.Cells[row, 1].Value = csku.CskuCode;
                worksheet.Cells[row, 2].Value = csku.Msku;
                worksheet.Cells[row, 3].Value = csku.InvoiceDisplayName;
                worksheet.Cells[row, 4].Value = (double)csku.SupplyPrice;
                worksheet.Cells[row, 5].Value = csku.Unit;
                worksheet.Cells[row, 6].Value = csku.Packing;
                worksheet.Cells[row, 7].Value = csku.Note;
            }
            if (worksheet.Dimension != null) worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            ExportHelper.SaveExcel(package, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
