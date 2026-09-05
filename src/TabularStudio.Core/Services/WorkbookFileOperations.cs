using ClosedXML.Excel;
using TabularStudio.Core.Contracts;

namespace TabularStudio.Core.Services;

// Common read/staging/commit helpers, moved without changing format standardization behavior.
internal static class WorkbookFileOperations
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    internal static OperationError? ProbeExistingOutput(string outputFilePath)
    {
        try
        {
            using var stream = new FileStream(
                outputFilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return null;
        }
        catch (IOException exception) when (IsFileLocked(exception))
        {
            return new OperationError(
                OperationErrorCode.FileLocked,
                "输出文件正在被其它程序占用。",
                outputFilePath);
        }
        catch (UnauthorizedAccessException)
        {
            return new OperationError(
                OperationErrorCode.OutputDirectoryNotWritable,
                "无法覆盖输出文件。",
                outputFilePath);
        }
        catch (IOException exception)
        {
            return new OperationError(
                OperationErrorCode.ProcessingFailed,
                "检查已有输出文件时失败。",
                exception.GetType().Name);
        }
    }

    internal static StagingReservation ReserveStagingFile(string outputFilePath)
    {
        var outputDirectory = Path.GetDirectoryName(outputFilePath)!;
        var outputName = Path.GetFileNameWithoutExtension(outputFilePath);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stagingPath = Path.Combine(
                outputDirectory,
                $".{outputName}.{Guid.NewGuid():N}.staging.xlsx");

            try
            {
                using var stream = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                return new StagingReservation(stagingPath, null);
            }
            catch (UnauthorizedAccessException)
            {
                return new StagingReservation(null, new OperationError(
                    OperationErrorCode.OutputDirectoryNotWritable,
                    "无法写入输出目录。",
                    outputDirectory));
            }
            catch (IOException) when (attempt < 2)
            {
            }
            catch (IOException exception)
            {
                return new StagingReservation(null, new OperationError(
                    OperationErrorCode.OutputDirectoryNotWritable,
                    "无法在输出目录创建临时结果文件。",
                    $"{outputDirectory} ({exception.GetType().Name})"));
            }
        }

        return new StagingReservation(null, new OperationError(
            OperationErrorCode.OutputDirectoryNotWritable,
            "无法在输出目录创建临时结果文件。",
            outputDirectory));
    }

    internal static WorkbookOpenResult TryOpenWorkbook(string filePath)
    {
        FileStream? stream = null;

        try
        {
            stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var workbook = new XLWorkbook(stream);
            return new WorkbookOpenResult(stream, workbook, null);
        }
        catch (FileNotFoundException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.FileNotFound, "输入 Excel 文件不存在。", filePath);
        }
        catch (DirectoryNotFoundException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.FileNotFound, "输入 Excel 文件不存在。", filePath);
        }
        catch (IOException exception) when (IsFileLocked(exception))
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.FileLocked, "输入文件正在被其它程序独占占用。", filePath);
        }
        catch (UnauthorizedAccessException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.WorkbookUnreadable, "输入工作簿无法读取。", filePath);
        }
        catch (IOException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.WorkbookUnreadable, "输入工作簿无法读取或已经损坏。", filePath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.WorkbookUnreadable, "输入工作簿无法读取或已经损坏。", filePath);
        }
    }

    internal static OperationError? CommitStagingFile(
        string stagingPath,
        string outputFilePath,
        bool overwriteExistingOutput)
    {
        try
        {
            File.Move(stagingPath, outputFilePath, overwriteExistingOutput);
            return null;
        }
        catch (IOException exception) when (IsFileLocked(exception))
        {
            return new OperationError(
                OperationErrorCode.FileLocked,
                "输出文件正在被其它程序占用。",
                outputFilePath);
        }
        catch (IOException) when (!overwriteExistingOutput && File.Exists(outputFilePath))
        {
            return new OperationError(
                OperationErrorCode.OutputAlreadyExists,
                "输出文件已存在，且本次未确认覆盖。",
                outputFilePath);
        }
        catch (UnauthorizedAccessException)
        {
            return new OperationError(
                OperationErrorCode.OutputDirectoryNotWritable,
                "无法提交输出文件。",
                outputFilePath);
        }
        catch (IOException exception)
        {
            return new OperationError(
                OperationErrorCode.ProcessingFailed,
                "提交输出文件失败。",
                exception.GetType().Name);
        }
    }

    internal static OperationError? TryCleanupStagingFile(string? stagingPath)
    {
        if (string.IsNullOrEmpty(stagingPath) || !File.Exists(stagingPath))
        {
            return null;
        }

        try
        {
            File.Delete(stagingPath);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new OperationError(
                OperationErrorCode.IncompleteOutputCleanupFailed,
                "处理失败，且临时结果文件无法清理。",
                stagingPath);
        }
    }

    internal static bool IsXlsxPath(string path) =>
        string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    internal static bool IsFileLocked(IOException exception)
    {
        var nativeErrorCode = exception.HResult & 0xFFFF;
        return nativeErrorCode is SharingViolation or LockViolation;
    }

    internal static WorkbookOpenResult WorkbookOpenFailure(
        OperationErrorCode code,
        string message,
        string detail) =>
        new(null, null, new OperationError(code, message, detail));

    internal sealed record StagingReservation(
        string? Path,
        OperationError? Error);

    internal sealed record WorkbookOpenResult(
        FileStream? Stream,
        XLWorkbook? Workbook,
        OperationError? Error);
}
