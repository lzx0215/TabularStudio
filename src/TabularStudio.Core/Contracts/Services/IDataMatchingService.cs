namespace TabularStudio.Core.Contracts;

public interface IDataMatchingService
{
    Task<DataMatchingResult> ExecuteAsync(
        DataMatchingRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
