using System.Globalization;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;

namespace TabularStudio.Core.Services;

public sealed class ProcessingProfileValidator(IWorkbookInspectionService inspection) : IProcessingProfileValidator
{
    public IReadOnlyList<string> Validate(ProcessingProfile profile) => ValidateStructure(profile);

    internal static IReadOnlyList<string> ValidateStructure(ProcessingProfile? profile)
    {
        var errors = new List<string>();
        if (profile is null) return ["配置内容为空。"];
        if (profile.Version != 1) errors.Add($"不支持配置版本 {profile.Version}，当前仅支持版本 1。");
        if (string.IsNullOrWhiteSpace(profile.Name)) errors.Add("请输入非空配置名称。");
        if (!Enum.IsDefined(profile.Kind)) errors.Add("配置功能类型无效。");
        if (profile.Kind == ProcessingProfileKind.FormatStandardization)
        {
            if (profile.Format is null || profile.Matching is not null) errors.Add("格式统一配置结构无效。");
            else if (!profile.Format.HasAnyRule) errors.Add("请至少选择一项格式统一规则。");
        }
        else if (profile.Kind == ProcessingProfileKind.DataMatching)
        {
            var settings = profile.Matching;
            if (settings is null || profile.Format is not null) return [.. errors, "数据匹配配置结构无效。"];
            if (settings.Conditions is null || settings.Conditions.Count == 0) errors.Add("请至少设置一条完整匹配条件。");
            else foreach (var condition in settings.Conditions)
            {
                CheckColumn(condition?.MasterColumn, "主表条件", errors);
                CheckColumn(condition?.ReferenceColumn, "参照表条件", errors);
            }
            if (settings.ReturnFields is null || settings.ReturnFields.Count == 0) errors.Add("请至少选择一个返回字段。");
            else
            {
                foreach (var column in settings.ReturnFields) CheckColumn(column, "返回字段", errors);
                if (settings.ReturnFields.Where(c => c is not null).Select(c => c.ColumnNumber).Distinct().Count() != settings.ReturnFields.Count)
                    errors.Add("返回字段不能重复选择同一物理列。");
            }
            if (settings.StatusColumn is null || settings.StatusColumn.ColumnName is null ||
                (settings.StatusColumn.Enabled && string.IsNullOrWhiteSpace(settings.StatusColumn.ColumnName)))
                errors.Add("启用状态列时请输入非空列名。");
            if (settings.MasterFilter is { } filter)
            {
                CheckColumn(filter.Column, "主表筛选", errors);
                if (!ValidFilter(filter)) errors.Add("主表筛选值为空、类型无效或与类型不符，请重新选择实际候选值。");
            }
        }
        return errors;
    }

    public async Task<ProfileApplicationResult> PrepareMatchingAsync(ProcessingProfile profile, WorksheetSource master,
        IReadOnlyList<ColumnReference> masterColumns, IReadOnlyList<ColumnReference> referenceColumns,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = ValidateStructure(profile).ToList();
        if (profile?.Kind != ProcessingProfileKind.DataMatching) errors.Add("请选择数据匹配配置。");
        if (errors.Count > 0) return Failure(errors);
        var settings = profile!.Matching!;
        foreach (var condition in settings.Conditions)
        {
            CheckCurrentColumn(condition.MasterColumn, masterColumns, "主表", errors);
            CheckCurrentColumn(condition.ReferenceColumn, referenceColumns, "参照表", errors);
        }
        foreach (var column in settings.ReturnFields) CheckCurrentColumn(column, referenceColumns, "参照表返回字段", errors);
        if (settings.MasterFilter is { } filterColumn) CheckCurrentColumn(filterColumn.Column, masterColumns, "主表筛选", errors);
        if (errors.Count > 0) return Failure(errors.Distinct().ToArray());
        if (settings.MasterFilter is not { } filter) return new(true, [], [], null);
        try
        {
            var values = await inspection.GetColumnValuesAsync(new(master, filter.Column), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!values.Success) return Failure([$"读取主表筛选候选失败：{values.Error?.Message ?? "未返回候选。"}"]);
            var selected = values.Values.FirstOrDefault(item => item.Value == filter.ToValue());
            if (selected is null) return Failure([$"主表筛选列 {filter.Column.ColumnNumber}「{filter.Column.HeaderText}」中不存在配置保存的同类型值「{filter.RawValue}」（{filter.Kind}）；未应用配置。"]);
            return new(true, [], values.Values, selected);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Failure([$"读取主表筛选候选失败：{ex.Message}"]);
        }
    }

    private static ProfileApplicationResult Failure(IReadOnlyList<string> errors) => new(false, errors, [], null);

    private static void CheckColumn(ColumnReference? column, string role, List<string> errors)
    {
        if (column is null || column.ColumnNumber < 1 || string.IsNullOrWhiteSpace(column.HeaderText))
            errors.Add($"{role}必须引用具有非空原始表头的有效物理列。");
    }

    private static void CheckCurrentColumn(ColumnReference expected, IReadOnlyList<ColumnReference> current, string role, List<string> errors)
    {
        var actual = current.FirstOrDefault(c => c.ColumnNumber == expected.ColumnNumber);
        if (actual is null || string.IsNullOrWhiteSpace(actual.HeaderText) || !string.Equals(expected.HeaderText, actual.HeaderText, StringComparison.Ordinal))
            errors.Add($"{role}第 {expected.ColumnNumber} 列：配置要求「{expected.HeaderText}」，当前为「{(actual is null ? "列不存在" : string.IsNullOrWhiteSpace(actual.HeaderText) ? "空表头" : actual.HeaderText)}」；未应用配置。");
    }

    private static bool ValidFilter(ProfileFilterSettings filter)
    {
        if (string.IsNullOrWhiteSpace(filter.RawValue) || !Enum.IsDefined(filter.Kind)) return false;
        if (filter.HasTime && filter.Kind != ColumnValueKind.DateTime) return false;
        return filter.Kind switch
        {
            ColumnValueKind.Text => true,
            ColumnValueKind.Number => double.TryParse(filter.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
            ColumnValueKind.Boolean => bool.TryParse(filter.RawValue, out _),
            ColumnValueKind.DateTime => DateTime.TryParseExact(filter.RawValue, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            ColumnValueKind.TimeSpan => TimeSpan.TryParseExact(filter.RawValue, "c", CultureInfo.InvariantCulture, out _),
            ColumnValueKind.Error => Enum.TryParse<XLError>(filter.RawValue, out var error) && Enum.IsDefined(error),
            _ => false
        };
    }
}
