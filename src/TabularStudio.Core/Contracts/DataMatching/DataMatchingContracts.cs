namespace TabularStudio.Core.Contracts;

public sealed record MasterRowFilter(ColumnReference Column, string EqualsValue)
{
    public ColumnFilterValue? SelectedValue { get; init; }
}

public sealed record MatchingCondition(
    ColumnReference MasterColumn,
    ColumnReference ReferenceColumn);

public sealed record MatchingStatusColumnOptions
{
    public bool Enabled { get; init; } = true;
    public string ColumnName { get; init; } = "匹配状态";
}

public sealed record DataMatchingRequest(
    WorksheetSource Master,
    WorksheetSource Reference,
    IReadOnlyList<MatchingCondition> Conditions,
    IReadOnlyList<ColumnReference> ReturnFields,
    bool NormalizeComparisonKeys,
    MatchingStatusColumnOptions StatusColumn,
    string OutputFilePath,
    bool OverwriteExistingOutput)
{
    public MasterRowFilter? MasterFilter { get; init; }
}

public sealed record ReturnedFieldMapping(
    ColumnReference RequestedColumn,
    string ActualOutputColumnName);

public sealed record DataMatchingSummary(
    int TotalMasterDataRowCount,
    int MatchedCount,
    int UnmatchedCount,
    int DuplicateCount,
    int EmptyKeyCount,
    TimeSpan Elapsed)
{
    public int SkippedCount { get; init; }
}

public sealed record DataMatchingResult(
    bool Success,
    string? OutputFilePath,
    DataMatchingSummary? Summary,
    IReadOnlyList<ReturnedFieldMapping> ReturnedFields,
    string? ActualStatusColumnName,
    OperationError? Error);
