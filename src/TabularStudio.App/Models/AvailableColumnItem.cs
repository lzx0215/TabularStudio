using TabularStudio.Core.Contracts;

namespace TabularStudio.App.Models;

/// <summary>
/// 用于 UI 下拉与列表展示的字段项模型。
/// 封装物理列身份 ColumnReference，展示形式如 "表头名称 (C列)"，保留物理列唯一身份。
/// </summary>
public sealed record AvailableColumnItem
{
    public ColumnReference Reference { get; }

    public int ColumnNumber => Reference.ColumnNumber;

    public string HeaderText => Reference.HeaderText ?? string.Empty;

    public string ColumnLetter { get; }

    public string DisplayName { get; }

    public AvailableColumnItem(ColumnReference reference)
    {
        Reference = reference ?? throw new System.ArgumentNullException(nameof(reference));
        ColumnLetter = ToExcelColumnLetter(reference.ColumnNumber);
        DisplayName = !string.IsNullOrWhiteSpace(reference.HeaderText)
            ? $"{reference.HeaderText} ({ColumnLetter}列)"
            : $"第 {ColumnLetter} 列";
    }

    public override string ToString() => DisplayName;

    public static string ToExcelColumnLetter(int columnNumber)
    {
        string colName = string.Empty;
        int col = columnNumber;
        while (col > 0)
        {
            int modulo = (col - 1) % 26;
            colName = Convert.ToChar('A' + modulo) + colName;
            col = (col - modulo) / 26;
        }
        return colName;
    }
}
