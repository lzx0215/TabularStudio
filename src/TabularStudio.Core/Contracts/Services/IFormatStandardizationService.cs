namespace TabularStudio.Core.Contracts;

public interface IFormatStandardizationService
{
    Task<FormatStandardizationResult> ExecuteAsync(
        FormatStandardizationRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
