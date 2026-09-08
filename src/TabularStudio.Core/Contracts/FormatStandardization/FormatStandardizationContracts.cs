namespace TabularStudio.Core.Contracts;

public sealed record FormatStandardizationOptions
{
    public bool TrimOuterWhitespace { get; init; } = true;
    public bool RemoveTabsNewLinesAndHiddenCharacters { get; init; } = true;
    public bool NormalizeFullWidthHalfWidth { get; init; } = true;
    public bool NormalizeUnicode { get; init; } = true;
    public bool NormalizeSafeNumbers { get; init; } = true;
    public bool NormalizeUnambiguousDates { get; init; } = true;
}

public sealed record FormatStandardizationRequest(
    WorksheetSource Source,
    string OutputFilePath,
    FormatStandardizationOptions Options,
    bool OverwriteExistingOutput);

public sealed record FormatStandardizationSummary(
    string? ProcessedWorksheetName,
    int ProcessedDataRowCount,
    TimeSpan Elapsed);

public sealed record FormatStandardizationResult(
    bool Success,
    string? OutputFilePath,
    FormatStandardizationSummary? Summary,
    OperationError? Error);
