namespace TabularStudio.Core.Contracts;

public sealed record ComparisonSource(string FilePath, string? WorksheetName);
public sealed record TableComparisonRequest(ComparisonSource Left, ComparisonSource Right);
public sealed record ComparisonValue(string Type, string Value);
public sealed record CellDifference(string Address, ComparisonValue Left, ComparisonValue Right);
public sealed record ComparisonExtent(int Rows, int Columns);
public sealed record TableComparisonResult(
    bool Success, TableComparisonRequest Request, ComparisonExtent? LeftExtent,
    ComparisonExtent? RightExtent, long ComparedCellCount,
    IReadOnlyList<CellDifference> Differences, OperationError? Error)
{
    public bool AreEqual => Success && Differences.Count == 0;
}

public interface ITableComparisonService
{
    Task<TableComparisonResult> CompareAsync(TableComparisonRequest request,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationError?> ExportAsync(TableComparisonResult result, string outputFilePath,
        CancellationToken cancellationToken = default);
}
