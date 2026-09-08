using TabularStudio.Core.Contracts;

namespace TabularStudio.Core.Services;

public sealed class BatchFormatStandardizationService : IBatchFormatStandardizationService
{
    private readonly IFormatStandardizationService single;
    public BatchFormatStandardizationService(IFormatStandardizationService? single = null)
        => this.single = single ?? new FormatStandardizationService();

    public async Task<BatchFormatResult> ExecuteAsync(BatchFormatRequest request,
        IProgress<BatchFormatProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);
        ArgumentNullException.ThrowIfNull(request.Options);
        var items = request.Items.ToArray();
        var sources = items.Select(item => FullPath(item?.Source?.FilePath)).Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputs = items.Select(item => FullPath(item?.OutputFilePath)).ToArray();
        var duplicateOutputs = outputs.Where(path => path is not null).GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<BatchFormatItemResult>();
        Report(0);
        for (var index = 0; index < items.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index];
            FormatStandardizationResult result;
            if (item?.Source is null || FullPath(item.Source.FilePath) is null || outputs[index] is null)
                result = Failure(OperationErrorCode.InvalidConfiguration, "批量输入或输出路径无效。");
            else if (sources.Contains(outputs[index]))
                result = Failure(OperationErrorCode.OutputConflictsWithInput, "输出不得覆盖本批次的任何输入文件。");
            else if (duplicateOutputs.Contains(outputs[index]))
                result = Failure(OperationErrorCode.InvalidConfiguration, "本批次存在相同输出路径，请为每个文件选择不同结果路径。");
            else
            {
                try
                {
                    result = await single.ExecuteAsync(new(item.Source, item.OutputFilePath, request.Options, item.OverwriteExistingOutput),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result = new(false, null, null, new(OperationErrorCode.ProcessingFailed, "处理该文件失败。", ex.GetType().Name));
                }
            }
            results.Add(new(index, item!, result));
            Report(results.Count);
        }
        return new(results.AsReadOnly());

        void Report(int count)
        {
            try { progress?.Report(new(count, items.Length)); }
            catch { /* A progress observer must not interrupt file processing. */ }
        }
    }

    private static string? FullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
    private static FormatStandardizationResult Failure(OperationErrorCode code, string message) => new(false, null, null, new(code, message));
}
