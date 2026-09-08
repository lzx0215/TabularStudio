namespace TabularStudio.Core.Contracts;

public sealed record WorkbookInspectionRequest(string FilePath);

public sealed record WorksheetInfo(string Name);

public sealed record WorkbookInspectionResult(
    bool Success,
    IReadOnlyList<WorksheetInfo> Worksheets,
    OperationError? Error);

public sealed record WorksheetPreviewRequest(WorksheetSource Source);

public sealed record PreviewCell(
    int ColumnNumber,
    string? DisplayValue);

public sealed record PreviewRow(
    int WorksheetRowNumber,
    IReadOnlyList<PreviewCell> Cells);

public sealed record PreviewTable(
    string? WorksheetName,
    int HeaderRowNumber,
    IReadOnlyList<ColumnReference> Columns,
    IReadOnlyList<PreviewRow> Rows);

public sealed record WorksheetPreviewResult(
    bool Success,
    PreviewTable? Preview,
    OperationError? Error);
