namespace TabularStudio.Core.Contracts;

public sealed record BatchFormatItem(WorksheetSource Source, string OutputFilePath, bool OverwriteExistingOutput = false);
public sealed record BatchFormatRequest(IReadOnlyList<BatchFormatItem> Items, FormatStandardizationOptions Options);
public sealed record BatchFormatItemResult(int Index, BatchFormatItem Item, FormatStandardizationResult Result);
public sealed record BatchFormatResult(IReadOnlyList<BatchFormatItemResult> Items)
{
    public int SucceededCount => Items.Count(item => item.Result.Success);
    public int FailedCount => Items.Count - SucceededCount;
}
public sealed record BatchFormatProgress(int CompletedCount, int TotalCount);

public interface IBatchFormatStandardizationService
{
    Task<BatchFormatResult> ExecuteAsync(BatchFormatRequest request,
        IProgress<BatchFormatProgress>? progress = null, CancellationToken cancellationToken = default);
}
