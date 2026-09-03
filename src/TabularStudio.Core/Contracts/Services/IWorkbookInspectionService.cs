namespace TabularStudio.Core.Contracts;

public interface IWorkbookInspectionService
{
    Task<WorkbookInspectionResult> InspectAsync(
        WorkbookInspectionRequest request,
        CancellationToken cancellationToken = default);

    Task<WorksheetPreviewResult> GetPreviewAsync(
        WorksheetPreviewRequest request,
        CancellationToken cancellationToken = default);
}
