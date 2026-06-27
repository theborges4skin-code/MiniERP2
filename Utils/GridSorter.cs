using System.Collections;
using System.Data;
using System.Reflection;

namespace MiniERP2.Utils;

/// <summary>
/// DataGridView 헤더 클릭 정렬을 데이터소스 종류(DataTable/BindingSource/BindingList&lt;T&gt;/일반
/// IList)에 관계없이 항상 동작하게 하는 도우미. BindingList&lt;T&gt;는 IBindingList.SupportsSorting이
/// 항상 false라 DataGridView.Sort()가 예외를 던지므로, 데이터소스를 직접 들여다보고 정렬한다.
/// </summary>
public static class GridSorter
{
    public static bool TrySort(object? dataSource, string propertyName, bool ascending)
    {
        switch (dataSource)
        {
            case DataTable table:
                table.DefaultView.Sort = BuildSortExpression(propertyName, ascending);
                return true;
            case DataView view:
                view.Sort = BuildSortExpression(propertyName, ascending);
                return true;
            case BindingSource bindingSource:
                return TrySortBindingSource(bindingSource, propertyName, ascending);
            case IList list:
                return TrySortList(list, propertyName, ascending);
            default:
                return false;
        }
    }

    private static bool TrySortBindingSource(BindingSource bindingSource, string propertyName, bool ascending)
    {
        if (bindingSource.DataSource is DataTable or DataView)
        {
            bindingSource.Sort = BuildSortExpression(propertyName, ascending);
            return true;
        }

        if (!TrySortList(bindingSource.List, propertyName, ascending)) return false;
        bindingSource.ResetBindings(false);
        return true;
    }

    private static bool TrySortList(IList list, string propertyName, bool ascending)
    {
        if (list.Count == 0) return false;
        var sampleType = list[0]?.GetType();
        if (sampleType == null) return false;

        var property = sampleType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null) return false;

        var ordered = ascending
            ? list.Cast<object>().OrderBy(x => property.GetValue(x), ValueComparer.Instance).ToList()
            : list.Cast<object>().OrderByDescending(x => property.GetValue(x), ValueComparer.Instance).ToList();

        list.Clear();
        foreach (var item in ordered) list.Add(item);
        return true;
    }

    private static string BuildSortExpression(string propertyName, bool ascending) =>
        $"[{propertyName}] {(ascending ? "ASC" : "DESC")}";

    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            if (x.GetType() == y.GetType() && x is IComparable comparable) return comparable.CompareTo(y);
            return string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
