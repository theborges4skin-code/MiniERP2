using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Import;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// 완제품(SKU) 원가 편집. 열 구성은 product_master와 같고, "원료" 열의 연결 유형을 고른다:
/// 수동값 / 제조법 용량 / 원료 소분 / 묶음. 수동값(PM 이관값)은 연결을 바꿔도 지우지 않고 비교용으로 남긴다.
/// </summary>
public class ProductDialog : Form
{
    private readonly AppState _state;
    private readonly CostSnapshot _snap;
    private readonly FinishedProduct _p;
    private readonly bool _show;

    private readonly TextBox _sku = Ui.Text(160);
    private readonly TextBox _name = Ui.Text(360);
    private readonly TextBox _category = Ui.Text(160);
    private readonly TextBox _group = Ui.Text(160);
    private readonly TextBox _consumer = Ui.Text(120);
    private readonly RadioButton _rManual = new() { Text = "수동값", AutoSize = true };
    private readonly RadioButton _rRecipe = new() { Text = "제조법 용량", AutoSize = true };
    private readonly RadioButton _rMaterial = new() { Text = "원료 소분", AutoSize = true };
    private readonly RadioButton _rBundle = new() { Text = "묶음", AutoSize = true };
    private readonly TextBox _manualRaw = Ui.Text(120);
    private readonly ComboBox _recipe = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly ComboBox _size = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Button _materialButton = new() { Width = 360, TextAlign = ContentAlignment.MiddleLeft, Height = 28 };
    private readonly TextBox _materialQty = Ui.Text(80);
    private readonly ComboBox _materialUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
    private readonly DataGridView _components = Ui.Grid(readOnly: false);
    private readonly TextBox _container = Ui.Text(100), _package = Ui.Text(100), _sticker = Ui.Text(100), _etc = Ui.Text(100), _shipping = Ui.Text(100);
    private readonly TextBox _notes = Ui.Text(360, multiline: true);
    private readonly Label _suggestion = new() { AutoSize = false, Width = 600, Height = 40, ForeColor = Ui.LinkFore };
    private readonly Label _breakdown = new() { AutoSize = true, Font = new Font(Control.DefaultFont.FontFamily, 10f), Padding = new Padding(6) };
    private readonly Panel _sourcePanel = new() { Dock = DockStyle.Fill };

    private long? _materialId;
    private List<FinishedProduct> _allProducts = [];

    public ProductDialog(AppState state, CostSnapshot snap, FinishedProduct product)
    {
        _state = state;
        _snap = snap;
        _p = product;
        _show = state.ShowVatIncluded;
        Text = $"완제품 원가 — {(product.Id == 0 ? "신규" : product.ToString())}";
        StartPosition = FormStartPosition.CenterParent;
        Width = 900;
        Height = 820;
        _materialUnit.Items.AddRange(["ml", "L", "g", "kg"]);
        _allProducts = snap.Products.Values.Where(x => x.Id != product.Id).OrderBy(x => x.SortOrder).ToList();

        var vat = state.VatLabel;
        var basic = Ui.Form(("SKU(MiniERP2)", _sku), ("상품명", _name), ("구분", _category), ("상품그룹", _group), ("소비자가(VAT포함)", _consumer));

        var radios = Ui.Bar(Ui.Label("원료 열 연결:", bold: true), _rManual, _rRecipe, _rMaterial, _rBundle);
        foreach (var rb in new[] { _rManual, _rRecipe, _rMaterial, _rBundle }) rb.CheckedChanged += (_, _) => { if (rb.Checked) ShowSourcePanel(); };

        _recipe.Items.Add("(제조법 선택)");
        foreach (var r in snap.Recipes.Where(r => !r.IsArchived || r.Sizes.Any(s => s.Id == product.RecipeSizeId))) _recipe.Items.Add(r);
        _recipe.SelectedIndexChanged += (_, _) => FillSizes();
        _size.SelectedIndexChanged += (_, _) => UpdateBreakdown();
        _materialButton.Click += (_, _) => PickMaterial();
        _components.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "product", HeaderText = "구성품(SKU 상품명)", Width = 480, DataSource = _allProducts.Select(x => new ProductItem(x.Id, x.ToString())).ToList(),
            ValueMember = nameof(ProductItem.Id), DisplayMember = nameof(ProductItem.Text), FlatStyle = FlatStyle.Flat,
        });
        _components.Columns.Add(Ui.Col("qty", "수량", 70, right: true));
        _components.AllowUserToAddRows = true;
        _components.AllowUserToDeleteRows = true;
        _components.CellEndEdit += (_, _) => UpdateBreakdown();
        _components.UserDeletedRow += (_, _) => UpdateBreakdown();
        _components.DataError += (_, e) => e.ThrowException = false;

        var sourceBox = new GroupBox { Text = "원료 열", Dock = DockStyle.Top, Height = 230 };
        sourceBox.Controls.Add(_sourcePanel);
        sourceBox.Controls.Add(radios);

        var extra = Ui.Form(($"용기({vat})", _container), ($"패키지({vat})", _package), ($"스티커 등({vat})", _sticker), ($"기타({vat})", _etc), ($"택배비({vat})", _shipping));
        foreach (var tb in new[] { _container, _package, _sticker, _etc, _shipping, _manualRaw, _materialQty }) tb.TextChanged += (_, _) => UpdateBreakdown();
        _materialUnit.SelectedIndexChanged += (_, _) => UpdateBreakdown();
        var extraBox = new GroupBox { Text = "추가 비용(product_master 용기~택배비 열)", Dock = DockStyle.Top, Height = 190 };
        extraBox.Controls.Add(extra);

        var notesBox = new GroupBox { Text = "비고", Dock = DockStyle.Top, Height = 110 };
        _notes.Dock = DockStyle.Fill;
        notesBox.Controls.Add(_notes);

        var sugBox = new GroupBox { Text = "연결 후보(이관 시 유추 — 확인 후 직접 연결)", Dock = DockStyle.Top, Height = 70 };
        sugBox.Controls.Add(_suggestion);
        _suggestion.Dock = DockStyle.Fill;

        var resultBox = new GroupBox { Text = "원가 계산", Dock = DockStyle.Fill };
        resultBox.Controls.Add(_breakdown);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(6) };
        bar.Controls.AddRange([
            new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel },
            Ui.Button("저장", (_, _) => SaveAndClose(confirm: false), 90),
            Ui.Button("저장 후 확정", (_, _) => SaveAndClose(confirm: true), 110),
            Ui.Button("미확정으로", (_, _) => SetDraft(), 100),
        ]);

        Controls.Add(resultBox);
        Controls.Add(notesBox);
        Controls.Add(extraBox);
        Controls.Add(sourceBox);
        Controls.Add(sugBox);
        Controls.Add(basic);
        Controls.Add(bar);

        LoadValues();
    }

    private sealed record ProductItem(long Id, string Text);

    private void LoadValues()
    {
        _sku.Text = _p.Sku;
        _name.Text = _p.Name;
        _category.Text = _p.Category;
        _group.Text = _p.ProductGroup;
        _consumer.Text = _p.ConsumerPrice is { } cp ? Fmt.Num(cp) : "";
        _manualRaw.Text = Fmt.Num(Math.Round(Vat.ToView(_p.ManualRaw, _show), 4));
        _container.Text = Fmt.Num(Math.Round(Vat.ToView(_p.ContainerCost, _show), 4));
        _package.Text = Fmt.Num(Math.Round(Vat.ToView(_p.PackageCost, _show), 4));
        _sticker.Text = Fmt.Num(Math.Round(Vat.ToView(_p.StickerCost, _show), 4));
        _etc.Text = Fmt.Num(Math.Round(Vat.ToView(_p.EtcCost, _show), 4));
        _shipping.Text = Fmt.Num(Math.Round(Vat.ToView(_p.ShippingCost, _show), 4));
        _notes.Text = _p.Notes.Replace("\n", Environment.NewLine);
        _suggestion.Text = _p.SuggestedLink.Length > 0 ? _p.SuggestedLink : "(없음)";
        _materialId = _p.MaterialId;
        _materialQty.Text = _p.MaterialQty is { } q ? Fmt.Num(q) : "";
        _materialUnit.SelectedItem = _p.MaterialQtyUnit;
        if (_materialUnit.SelectedIndex < 0) _materialUnit.SelectedIndex = 0;
        UpdateMaterialButton();

        _recipe.SelectedIndex = 0;
        if (_p.RecipeSizeId is { } sid && _snap.SizesById.TryGetValue(sid, out var x))
        {
            _recipe.SelectedItem = _recipe.Items.Cast<object>().OfType<Recipe>().FirstOrDefault(r => r.Id == x.Recipe.Id);
            FillSizes();
            _size.SelectedItem = _size.Items.Cast<object>().OfType<SizeItem>().FirstOrDefault(s => s.Id == sid);
        }
        else if (MappingHints.FirstRecipeCode(_p.SuggestedLink) is { } code)
        {
            // 연결 전이면 연결 후보 제조법을 미리 골라 둔다(유형은 바꾸지 않음).
            _recipe.SelectedItem = _recipe.Items.Cast<object>().OfType<Recipe>().FirstOrDefault(r => MfgCodes.RecipeShort(r.Code) == code);
            FillSizes();
        }

        foreach (var c in _p.Components)
        {
            if (_allProducts.Any(x => x.Id == c.ComponentProductId)) _components.Rows.Add(c.ComponentProductId, Fmt.Num(c.Quantity));
        }

        (_p.SourceType switch
        {
            ProductSourceType.RecipeSize => _rRecipe,
            ProductSourceType.Material => _rMaterial,
            ProductSourceType.Bundle => _rBundle,
            _ => _rManual,
        }).Checked = true;
        ShowSourcePanel();
    }

    private sealed record SizeItem(long Id, string Text)
    {
        public override string ToString() => Text;
    }

    private void FillSizes()
    {
        _size.Items.Clear();
        if (_recipe.SelectedItem is not Recipe r) { UpdateBreakdown(); return; }
        foreach (var s in r.Sizes.OrderBy(s => s.SortOrder))
        {
            var unit = _snap.SizeUnitCost(s.Id, _show);
            _size.Items.Add(new SizeItem(s.Id, $"{Fmt.Num(s.VolumeMl)}ml — 완제품 단가 {Fmt.Won(unit)}원({_state.VatLabel})"));
        }
        if (_size.Items.Count > 0) _size.SelectedIndex = 0;
        UpdateBreakdown();
    }

    private void ShowSourcePanel()
    {
        _sourcePanel.Controls.Clear();
        Control content;
        if (_rRecipe.Checked)
        {
            content = Ui.Form(("제조법", _recipe), ("용량", _size), ("", Ui.Label("원료 열 = 제조법 용량의 완제품 단가(원액비+용량표의 부자재·가공비).\n용량표에 이미 용기 등이 들어 있으면 아래 추가 비용의 용기 칸은 0으로 두세요(이중 계산 주의).")));
        }
        else if (_rMaterial.Checked)
        {
            var qtyRow = new FlowLayoutPanel { AutoSize = true };
            qtyRow.Controls.AddRange([_materialQty, _materialUnit]);
            content = Ui.Form(("원료", _materialButton), ("수량", qtyRow), ("", Ui.Label("ml·L는 원료 비중을 곱해 kg으로 환산합니다(g·kg은 그대로).")));
        }
        else if (_rBundle.Checked)
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(_components);
            panel.Controls.Add(Ui.Bar(Ui.Label("원료 열 = Σ(구성품 합계 × 수량). 맨 아래 빈 줄에 구성품을 추가하세요.")));
            content = panel;
        }
        else
        {
            content = Ui.Form(($"원료 금액({_state.VatLabel})", _manualRaw), ("", Ui.Label("product_master에서 가져온 값입니다. 제조법이 확정되면 '제조법 용량'으로 연결하세요.")));
        }
        content.Dock = DockStyle.Fill;
        _sourcePanel.Controls.Add(content);
        UpdateBreakdown();
    }

    private void PickMaterial()
    {
        using var picker = new MaterialPicker(_state);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is null) return;
        _materialId = picker.Selected.Id;
        if (!_snap.Materials.ContainsKey(picker.Selected.Id)) _snap.Materials[picker.Selected.Id] = picker.Selected;
        UpdateMaterialButton();
        UpdateBreakdown();
    }

    private void UpdateMaterialButton() =>
        _materialButton.Text = _materialId is { } id && _snap.Materials.TryGetValue(id, out var m) ? $"{m.Name} [{m.Code}]  (클릭해서 변경)" : "(클릭해서 원료 선택)";

    private static double ParseOr0(TextBox tb) => Fmt.TryParse(tb.Text, out var v) ? v : 0;

    /// <summary>화면 입력값을 제품 객체에 반영(저장 전 계산·저장 공용).</summary>
    private FinishedProduct BuildFromInputs()
    {
        var p = new FinishedProduct
        {
            Id = _p.Id, Sku = _sku.Text.Trim().Length == 0 ? null : _sku.Text.Trim(), Name = _name.Text.Trim(), Category = _category.Text.Trim(),
            ProductGroup = _group.Text.Trim(), ConsumerPrice = Fmt.TryParse(_consumer.Text, out var cp) ? cp : null,
            ManualRaw = Vat.FromView(ParseOr0(_manualRaw), _show), ContainerCost = Vat.FromView(ParseOr0(_container), _show),
            PackageCost = Vat.FromView(ParseOr0(_package), _show), StickerCost = Vat.FromView(ParseOr0(_sticker), _show),
            EtcCost = Vat.FromView(ParseOr0(_etc), _show), ShippingCost = Vat.FromView(ParseOr0(_shipping), _show),
            Notes = _notes.Text.Replace("\r\n", "\n").Trim(), SuggestedLink = _p.SuggestedLink, Status = _p.Status,
            ConfirmedTotal = _p.ConfirmedTotal, ConfirmedAt = _p.ConfirmedAt, LastPushedCost = _p.LastPushedCost, LastPushedAt = _p.LastPushedAt,
            SortOrder = _p.SortOrder, IsActive = _p.IsActive,
            RecipeSizeId = _size.SelectedItem is SizeItem si ? si.Id : _p.RecipeSizeId,
            MaterialId = _materialId, MaterialQty = Fmt.TryParse(_materialQty.Text, out var q) ? q : null,
            MaterialQtyUnit = _materialUnit.SelectedItem as string ?? "ml",
            SourceType = _rRecipe.Checked ? ProductSourceType.RecipeSize : _rMaterial.Checked ? ProductSourceType.Material : _rBundle.Checked ? ProductSourceType.Bundle : ProductSourceType.Manual,
        };
        foreach (DataGridViewRow r in _components.Rows)
        {
            if (r.IsNewRow || r.Cells["product"].Value is not long pid) continue;
            p.Components.Add(new FinishedProductComponent { ComponentProductId = pid, Quantity = Fmt.TryParse(r.Cells["qty"].Value?.ToString(), out var cq) && cq > 0 ? cq : 1 });
        }
        return p;
    }

    private ProductCostResult Calculate(FinishedProduct p, bool showIncluded)
    {
        // 편집 중인 값으로 계산하도록 스냅샷의 자기 자신을 임시로 바꿔 끼운다(묶음 순환 검사 포함).
        var had = _snap.Products.TryGetValue(p.Id, out var original);
        if (p.Id != 0) _snap.Products[p.Id] = p;
        try { return _snap.ProductCalculator(showIncluded).Calculate(p, showIncluded); }
        finally { if (had) _snap.Products[p.Id] = original!; }
    }

    private void UpdateBreakdown()
    {
        if (!IsHandleCreated && Controls.Count == 0) return;
        var p = BuildFromInputs();
        var r = Calculate(p, _show);
        var incl = Calculate(p, true);
        var lines = new List<string>
        {
            $"원료 {Fmt.Won(r.Raw)} + 용기 {Fmt.Won(r.Container)} + 패키지 {Fmt.Won(r.Package)} + 스티커 {Fmt.Won(r.Sticker)} + 기타 {Fmt.Won(r.Etc)} + 택배비 {Fmt.Won(r.Shipping)}",
            $"= 합계 {Fmt.Won(r.Total)}원 ({_state.VatLabel})      VAT포함 {Fmt.Won(incl.Total)}원  ← MiniERP2로 보내는 값",
        };
        if (p.SourceType != ProductSourceType.Manual) lines.Add($"(참고) 수동값/PM 이관 원료 금액: {Fmt.Won(Vat.ToView(p.ManualRaw, _show))}원");
        if (p.ConsumerPrice is > 0 && incl.Total > 0) lines.Add($"마진율(소비자가 대비, VAT포함 기준): {Fmt.Pct(1 - incl.Total / p.ConsumerPrice.Value)}");
        if (_p.ConfirmedTotal is { } ct) lines.Add($"확정 원가(VAT포함): {Fmt.Won(ct)}  확정일 {_p.ConfirmedAt}" + (Math.Abs(ct - incl.Total) > 0.5 ? "  ← 지금 계산과 다름(재확정 필요)" : ""));
        if (r.Problem is not null) lines.Add("⚠ " + r.Problem);
        _breakdown.Text = string.Join(Environment.NewLine, lines);
        _breakdown.ForeColor = r.Problem is null ? SystemColors.ControlText : Ui.WarnFore;
    }

    private void SaveAndClose(bool confirm)
    {
        var p = BuildFromInputs();
        if (p.Name.Length == 0) { Ui.Warn(this, "상품명을 입력하세요."); return; }
        if (p.Sku is not null && _snap.Products.Values.Any(x => x.Id != p.Id && x.Sku == p.Sku)) { Ui.Warn(this, $"SKU '{p.Sku}'는 다른 품목에 이미 있습니다."); return; }
        if (p.SourceType == ProductSourceType.RecipeSize && p.RecipeSizeId is null) { Ui.Warn(this, "제조법과 용량을 선택하세요."); return; }
        if (p.SourceType == ProductSourceType.Material && (p.MaterialId is null || p.MaterialQty is null)) { Ui.Warn(this, "원료와 수량을 입력하세요."); return; }

        var incl = Calculate(p, true);
        if (confirm)
        {
            if (incl.Problem is not null) { Ui.Warn(this, $"계산에 문제가 있어 확정할 수 없습니다:\n{incl.Problem}"); return; }
            if (p.SourceType == ProductSourceType.RecipeSize && p.RecipeSizeId is { } sid && _snap.SizesById.TryGetValue(sid, out var x) && !x.Recipe.IsConfirmed &&
                !Ui.Confirm(this, $"연결된 제조법 {x.Recipe.Code}은(는) 아직 미확정입니다. 그래도 이 품목을 확정할까요?")) return;
            p.Status = RecipeStatus.Confirmed;
            p.ConfirmedTotal = incl.Total;
            p.ConfirmedAt = MfgDb.Now();
        }
        else if (p.IsConfirmed && p.ConfirmedTotal is { } ct && Math.Abs(ct - incl.Total) > 0.5)
        {
            if (!Ui.Confirm(this, "확정된 품목의 원가가 바뀌었습니다. 저장하면 '미확정'으로 돌아갑니다(MiniERP2 전송 대상에서 빠짐). 저장할까요?")) return;
            p.Status = RecipeStatus.Draft;
        }
        new ProductRepository().Save(p);
        _p.Id = p.Id;
        _state.NotifyDataChanged();
        DialogResult = DialogResult.OK;
    }

    private void SetDraft()
    {
        if (!_p.IsConfirmed || _p.Id == 0) return;
        var p = BuildFromInputs();
        p.Status = RecipeStatus.Draft;
        new ProductRepository().Save(p);
        _state.NotifyDataChanged();
        DialogResult = DialogResult.OK;
    }
}
