namespace TabularStudio.Core.Contracts;

public sealed record WorksheetSource(
    string FilePath,
    string WorksheetName,
    int HeaderRowNumber);

public sealed record ColumnReference(
    int ColumnNumber,
    string? HeaderText);

public enum OperationErrorCode
{
    FileNotFound,
    UnsupportedFileType,
    FileLocked,
    WorkbookUnreadable,
    WorksheetNotFound,
    InvalidHeaderRow,
    ColumnNotFound,
    InvalidConfiguration,
    OutputConflictsWithInput,
    OutputAlreadyExists,
    OutputDirectoryNotWritable,
    FormulaCellNotAllowedForMatching,
    IncompleteOutputCleanupFailed,
    ProcessingFailed
}

public sealed record OperationError(
    OperationErrorCode Code,
    string Message,
    string? Detail = null);

public enum OperationStage
{
    Reading,
    Preparing,
    Processing,
    Writing,
    Completed
}

public sealed record OperationProgress(
    OperationStage Stage,
    int? Percent,
    int? ProcessedRows,
    int? TotalRows);
