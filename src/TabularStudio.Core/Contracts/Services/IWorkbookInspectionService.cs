namespace TabularStudio.Core.Contracts;

public interface IWorkbookInspectionService
{
    Task<WorkbookInspectionResult> InspectAsync(
        WorkbookInspectionRequest request,
        CancellationToken cancellationToken = default);

    Task<WorksheetPreviewResult> GetPreviewAsync(
        WorksheetPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<ColumnValuesResult> GetColumnValuesAsync(ColumnValuesRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(new ColumnValuesResult(false, [],
            new OperationError(OperationErrorCode.InvalidConfiguration, "当前检查服务不支持完整列候选值。")));
}
