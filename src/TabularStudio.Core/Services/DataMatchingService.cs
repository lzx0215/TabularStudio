using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using static TabularStudio.Core.Services.ApprovedValueNormalization;
using static TabularStudio.Core.Services.WorkbookFileOperations;

namespace TabularStudio.Core.Services;

public sealed class DataMatchingService : IDataMatchingService
{
    private static readonly FormatStandardizationOptions ComparisonOptions = new();

    public Task<DataMatchingResult> ExecuteAsync(
        DataMatchingRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Execute(request, progress, cancellationToken), cancellationToken);
    }

    private static DataMatchingResult Execute(
        DataMatchingRequest? request, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        string? stagingPath = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var error = ValidateRequest(request);
            if (error is not null) return Failure(error);
            var configuration = request!;
            var outputPath = Path.GetFullPath(configuration.OutputFilePath);
            if (File.Exists(outputPath))
            {
                if (!configuration.OverwriteExistingOutput)
                    return Failure(Error(OperationErrorCode.OutputAlreadyExists, "输出文件已存在，且本次未确认覆盖。", outputPath));
                error = ProbeExistingOutput(outputPath);
                if (error is not null) return Failure(error);
            }

            var reservation = ReserveStagingFile(outputPath);
            if (reservation.Error is not null) return Failure(reservation.Error);
            stagingPath = reservation.Path!;
            Report(progress, OperationStage.Reading, null, null, null);
            token.ThrowIfCancellationRequested();
            var masterOpen = TryOpenWorkbook(configuration.Master.FilePath);
            if (masterOpen.Error is not null) return FailureAfterCleanup(masterOpen.Error, stagingPath);
            using var masterStream = masterOpen.Stream!;
            using var masterBook = masterOpen.Workbook!;
            token.ThrowIfCancellationRequested();
            // Separate read-only instances also support two sheets from the same input file.
            var referenceOpen = TryOpenWorkbook(configuration.Reference.FilePath);
            if (referenceOpen.Error is not null) return FailureAfterCleanup(referenceOpen.Error, stagingPath);
            using var referenceStream = referenceOpen.Stream!;
            using var referenceBook = referenceOpen.Workbook!;
            token.ThrowIfCancellationRequested();

            var master = ReadSheet(masterBook, configuration.Master, out error);
            if (error is not null) return FailureAfterCleanup(error, stagingPath);
            var reference = ReadSheet(referenceBook, configuration.Reference, out error);
            if (error is not null) return FailureAfterCleanup(error, stagingPath);
            var masterColumns = configuration.Conditions.Select(c => c.MasterColumn).ToArray();
            var referenceColumns = configuration.Conditions.Select(c => c.ReferenceColumn).ToArray();
            var filter = configuration.MasterFilter;
            var filterColumns = filter is null ? Array.Empty<ColumnReference>() : new[] { filter.Column };
            var validatedMasterColumns = masterColumns.Concat(filterColumns).ToArray();
            error = ValidateColumns(master!, validatedMasterColumns)
                ?? ValidateColumns(reference!, referenceColumns.Concat(configuration.ReturnFields));
            if (error is not null) return FailureAfterCleanup(error, stagingPath);
            if ((long)master!.LastColumn + configuration.ReturnFields.Count + (configuration.StatusColumn.Enabled ? 1 : 0) > XLHelper.MaxColumnNumber)
                return FailureAfterCleanup(Error(OperationErrorCode.InvalidConfiguration, "工作表没有足够的可追加列。"), stagingPath);

            var total = master.LastRow - master.HeaderRow;
            Report(progress, OperationStage.Preparing, null, null, total);
            token.ThrowIfCancellationRequested();
            error = CheckFormulas(master, validatedMasterColumns, token)
                ?? CheckFormulas(reference!, referenceColumns.Concat(configuration.ReturnFields), token);
            if (error is not null) return FailureAfterCleanup(error, stagingPath);
            var safeMaster = SafeNumericColumns(master, masterColumns, configuration.NormalizeComparisonKeys, token);
            var safeReference = SafeNumericColumns(reference!, referenceColumns, configuration.NormalizeComparisonKeys, token);
            var safeFilter = SafeNumericColumns(master, filterColumns, configuration.NormalizeComparisonKeys, token);
            var expectedFilter = filter is null ? null : new CompositeKey(new[]
            {
                ReadTextPart(filter.EqualsValue, safeFilter.Contains(filter.Column.ColumnNumber), configuration.NormalizeComparisonKeys)
            });
            var index = new Dictionary<CompositeKey, int>();
            for (var row = reference!.HeaderRow + 1; row <= reference.LastRow; row++)
            {
                token.ThrowIfCancellationRequested();
                var key = ReadKey(reference, row, referenceColumns, safeReference, configuration.NormalizeComparisonKeys);
                if (key is null) continue;
                // Zero marks a duplicate, regardless of the returned values.
                if (!index.TryAdd(key, row)) index[key] = 0;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var column = 1; column <= master.LastColumn; column++)
            {
                var name = Header(master.Sheet.Cell(master.HeaderRow, column));
                if (name is not null) names.Add(name);
            }
            var mappings = configuration.ReturnFields.Select(column =>
            {
                var actualHeader = Header(reference.Sheet.Cell(reference.HeaderRow, column.ColumnNumber))!;
                return new ReturnedFieldMapping(new ColumnReference(column.ColumnNumber, actualHeader), UniqueName(actualHeader, names));
            }).ToArray();
            for (var i = 0; i < mappings.Length; i++)
                master.Sheet.Cell(master.HeaderRow, master.LastColumn + i + 1).Value = mappings[i].ActualOutputColumnName;
            var statusName = configuration.StatusColumn.Enabled ? UniqueName(configuration.StatusColumn.ColumnName, names) : null;
            var statusNumber = master.LastColumn + mappings.Length + 1;
            if (statusName is not null) master.Sheet.Cell(master.HeaderRow, statusNumber).Value = statusName;

            int matched = 0, unmatched = 0, duplicate = 0, empty = 0, skipped = 0;
            Report(progress, OperationStage.Processing, 0, 0, total);
            for (var row = master.HeaderRow + 1; row <= master.LastRow; row++)
            {
                token.ThrowIfCancellationRequested();
                var participates = filter is null || expectedFilter!.Equals(
                    ReadKey(master, row, filterColumns, safeFilter, configuration.NormalizeComparisonKeys));
                var key = participates ? ReadKey(master, row, masterColumns, safeMaster, configuration.NormalizeComparisonKeys) : null;
                string status;
                if (!participates) { skipped++; status = "未参与匹配"; }
                else if (key is null) { empty++; status = "匹配键为空"; }
                else if (!index.TryGetValue(key, out var referenceRow)) { unmatched++; status = "未匹配"; }
                else if (referenceRow == 0) { duplicate++; status = "重复"; }
                else
                {
                    matched++;
                    status = "匹配成功";
                    for (var i = 0; i < mappings.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var source = reference.Sheet.Cell(referenceRow, mappings[i].RequestedColumn.ColumnNumber);
                        var destination = master.Sheet.Cell(row, master.LastColumn + i + 1);
                        destination.Value = source.Value;
                        destination.Style.NumberFormat = source.Style.NumberFormat;
                    }
                }
                if (statusName is not null) master.Sheet.Cell(row, statusNumber).Value = status;
                var processed = row - master.HeaderRow;
                Report(progress, OperationStage.Processing, (int)((long)processed * 100 / total), processed, total);
            }

            token.ThrowIfCancellationRequested();
            Report(progress, OperationStage.Writing, null, total, total);
            token.ThrowIfCancellationRequested();
            using (var staging = new FileStream(stagingPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                masterBook.SaveAs(staging);
            token.ThrowIfCancellationRequested();
            if (configuration.OverwriteExistingOutput && File.Exists(outputPath))
            {
                error = ProbeExistingOutput(outputPath);
                if (error is not null) return FailureAfterCleanup(error, stagingPath);
            }
            error = CommitStagingFile(stagingPath, outputPath, configuration.OverwriteExistingOutput);
            if (error is not null) return FailureAfterCleanup(error, stagingPath);
            stagingPath = null;
            var result = new DataMatchingResult(true, outputPath,
                new DataMatchingSummary(total, matched, unmatched, duplicate, empty, clock.Elapsed) { SkippedCount = skipped }, mappings, statusName, null);
            // Output is committed. Observer failures must not turn a completed operation into a failure.
            try { Report(progress, OperationStage.Completed, 100, total, total); }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
            return result;
        }
        catch (OperationCanceledException exception)
        {
            var cleanup = TryCleanupStagingFile(stagingPath);
            if (cleanup is not null) exception.Data[nameof(OperationErrorCode.IncompleteOutputCleanupFailed)] = cleanup.Detail;
            throw;
        }
        catch (IOException exception) when (IsFileLocked(exception))
        {
            return FailureAfterCleanup(Error(OperationErrorCode.FileLocked, "文件正在被其它程序占用。", exception.GetType().Name), stagingPath);
        }
        catch (UnauthorizedAccessException)
        {
            return FailureAfterCleanup(Error(OperationErrorCode.OutputDirectoryNotWritable, "无法写入输出目录。", request?.OutputFilePath), stagingPath);
        }
        catch (Exception exception)
        {
            return FailureAfterCleanup(Error(OperationErrorCode.ProcessingFailed, "数据匹配处理失败。", exception.GetType().Name), stagingPath);
        }
    }

    private static OperationError? ValidateRequest(DataMatchingRequest? request)
    {
        if (request?.Master is null || request.Reference is null || request.StatusColumn is null
            || request.Conditions is null || request.Conditions.Count == 0
            || request.ReturnFields is null || request.ReturnFields.Count == 0
            || request.Conditions.Any(c => c?.MasterColumn is null || c.ReferenceColumn is null)
            || request.ReturnFields.Any(c => c is null)
            || string.IsNullOrWhiteSpace(request.OutputFilePath)
            || (request.StatusColumn.Enabled && string.IsNullOrWhiteSpace(request.StatusColumn.ColumnName)))
            return Error(OperationErrorCode.InvalidConfiguration, "数据匹配配置不完整。");
        if (request.MasterFilter is { } filter && (filter.Column is null || string.IsNullOrWhiteSpace(filter.EqualsValue)
            || (request.NormalizeComparisonKeys && NormalizeText(filter.EqualsValue, ComparisonOptions).Length == 0)))
            return Error(OperationErrorCode.InvalidConfiguration, "请选择主表筛选列并填写非空比较值。");
        try
        {
            var output = Path.GetFullPath(request.OutputFilePath);
            foreach (var source in new[] { request.Master, request.Reference })
            {
                if (string.IsNullOrWhiteSpace(source.FilePath) || string.IsNullOrWhiteSpace(source.WorksheetName))
                    return Error(OperationErrorCode.InvalidConfiguration, "输入文件路径和工作表名称不能为空。");
                if (source.HeaderRowNumber < 1 || source.HeaderRowNumber > XLHelper.MaxRowNumber)
                    return Error(OperationErrorCode.InvalidHeaderRow, "表头行号无效。");
                var input = Path.GetFullPath(source.FilePath);
                if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                    return Error(OperationErrorCode.OutputConflictsWithInput, "输出不能覆盖任何输入文件。", output);
                if (!IsXlsxPath(input) || !IsXlsxPath(output))
                    return Error(OperationErrorCode.UnsupportedFileType, "输入和输出文件都必须为 .xlsx。");
                if (!File.Exists(input)) return Error(OperationErrorCode.FileNotFound, "输入文件不存在。", input);
            }
            if (!Directory.Exists(Path.GetDirectoryName(output)))
                return Error(OperationErrorCode.OutputDirectoryNotWritable, "输出目录不存在或不可用。", output);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Error(OperationErrorCode.InvalidConfiguration, "文件路径无效。", exception.GetType().Name);
        }
        return null;
    }

    private static SheetData? ReadSheet(XLWorkbook workbook, WorksheetSource source, out OperationError? error)
    {
        error = null;
        var sheet = workbook.Worksheets.FirstOrDefault(s => string.Equals(s.Name, source.WorksheetName, StringComparison.Ordinal));
        if (sheet is null)
        {
            error = Error(OperationErrorCode.WorksheetNotFound, "指定工作表不存在。", source.WorksheetName);
            return null;
        }
        var cells = sheet.CellsUsed(XLCellsUsedOptions.Contents).Where(c => c.Address.RowNumber >= source.HeaderRowNumber).ToArray();
        if (cells.Length == 0)
        {
            error = Error(OperationErrorCode.InvalidHeaderRow, "表头行及其下方没有有效数据范围。");
            return null;
        }
        return new SheetData(sheet, source.HeaderRowNumber, cells.Max(c => c.Address.RowNumber),
            cells.Min(c => c.Address.ColumnNumber), cells.Max(c => c.Address.ColumnNumber));
    }

    private static OperationError? ValidateColumns(SheetData sheet, IEnumerable<ColumnReference> columns)
    {
        foreach (var column in columns)
        {
            if (column.ColumnNumber < sheet.FirstColumn || column.ColumnNumber > sheet.LastColumn)
                return Error(OperationErrorCode.ColumnNotFound, "指定物理列不存在。", $"{sheet.Sheet.Name}: {column.ColumnNumber}");
            if (Header(sheet.Sheet.Cell(sheet.HeaderRow, column.ColumnNumber)) is null)
                return Error(OperationErrorCode.InvalidConfiguration, "空表头列不能用作匹配键或返回字段。", $"{sheet.Sheet.Name}: {column.ColumnNumber}");
        }
        return null;
    }

    private static OperationError? CheckFormulas(SheetData sheet, IEnumerable<ColumnReference> columns, CancellationToken token)
    {
        foreach (var column in columns.Select(c => c.ColumnNumber).Distinct())
            for (var row = sheet.HeaderRow + 1; row <= sheet.LastRow; row++)
            {
                token.ThrowIfCancellationRequested();
                if (sheet.Sheet.Cell(row, column).HasFormula)
                    return Error(OperationErrorCode.FormulaCellNotAllowedForMatching, "匹配键或返回字段的数据行包含公式。", $"{sheet.Sheet.Name}!{sheet.Sheet.Cell(row, column).Address}");
            }
        return null;
    }

    private static HashSet<int> SafeNumericColumns(SheetData sheet, IEnumerable<ColumnReference> columns, bool normalize, CancellationToken token)
    {
        var safe = new HashSet<int>();
        if (!normalize) return safe;
        foreach (var column in columns.Select(c => c.ColumnNumber).Distinct())
        {
            var eligible = true;
            for (var row = sheet.HeaderRow + 1; row <= sheet.LastRow; row++)
            {
                token.ThrowIfCancellationRequested();
                var cell = sheet.Sheet.Cell(row, column);
                if (cell.DataType is XLDataType.Blank or XLDataType.Number) continue;
                if (cell.DataType != XLDataType.Text) { eligible = false; break; }
                var text = NormalizeText(cell.GetString(), ComparisonOptions);
                if (text.Length != 0 && !TryGetComparisonNumber(text, out _)) { eligible = false; break; }
            }
            if (eligible) safe.Add(column);
        }
        return safe;
    }

    private static CompositeKey? ReadKey(SheetData sheet, int row, ColumnReference[] columns, HashSet<int> safeNumbers, bool normalize)
    {
        var parts = new KeyPart[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var cell = sheet.Sheet.Cell(row, columns[i].ColumnNumber);
            if (cell.DataType == XLDataType.Blank) return null;
            if (cell.DataType == XLDataType.Text)
            {
                var part = ReadTextPart(cell.GetString(), safeNumbers.Contains(columns[i].ColumnNumber), normalize);
                if (part.Kind == "Text" && (string)part.Value == "") return null;
                parts[i] = part;
            }
            else if (normalize && cell.DataType == XLDataType.DateTime)
            {
                var date = cell.GetDateTime();
                parts[i] = DatePart(date, HasTimeComponent(cell, date));
            }
            else parts[i] = cell.DataType switch
            {
                XLDataType.Number => new KeyPart("Number", cell.GetDouble()),
                XLDataType.Boolean => new KeyPart("Boolean", cell.GetBoolean()),
                XLDataType.DateTime => new KeyPart("RawDateTime", cell.GetDateTime()),
                XLDataType.TimeSpan => new KeyPart("TimeSpan", cell.GetTimeSpan()),
                XLDataType.Error => new KeyPart("Error", cell.Value.GetError()),
                _ => throw new InvalidOperationException("Unsupported cell value type.")
            };
        }
        return new CompositeKey(parts);
    }

    private static KeyPart ReadTextPart(string value, bool safeNumeric, bool normalize)
    {
        var text = normalize ? NormalizeText(value, ComparisonOptions) : value;
        if (normalize && safeNumeric && TryGetComparisonNumber(text, out var number)) return new KeyPart("Number", number);
        if (normalize && TryGetApprovedDate(text, out var date, out var hasTime)) return DatePart(date, hasTime);
        return new KeyPart("Text", text);
    }

    private static KeyPart DatePart(DateTime date, bool hasTime) => hasTime
        ? new KeyPart("DateTime", date.Ticks / TimeSpan.TicksPerSecond)
        : new KeyPart("Date", date.Date.Ticks);

    private static bool TryGetComparisonNumber(string text, out double number)
    {
        if (!TryGetSafeExcelNumber(text, out number)) return false;
        // decimal.TryParse can round values beyond its scale even when significant digits
        // are few. Verify the original decimal spelling before allowing cross-type equality.
        var parsed = decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var original = text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
        if (!string.Equals(original, parsed.ToString("0.############################", CultureInfo.InvariantCulture), StringComparison.Ordinal)) return false;
        // Parse the verified decimal directly, avoiding a second decimal-to-double rounding.
        number = double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool HasTimeComponent(IXLCell cell, DateTime date)
    {
        var format = cell.Style.NumberFormat.Format;
        var hasDateToken = false;
        var quoted = false;
        var bracketed = false;
        for (var i = 0; i < format.Length; i++)
        {
            var character = char.ToLowerInvariant(format[i]);
            if (character == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (character is '\\' or '_' or '*') { i++; continue; }
            if (character == '[') { bracketed = true; continue; }
            if (character == ']') { bracketed = false; continue; }
            if (bracketed) continue;
            if (character is 'h' or 's') return true;
            if (character is 'y' or 'd') hasDateToken = true;
        }
        if (hasDateToken) return false;
        // Built-in Excel date/time formats may have no custom format string.
        var id = cell.Style.NumberFormat.NumberFormatId;
        if (id is >= 14 and <= 17) return false;
        if (id is >= 18 and <= 22 or >= 45 and <= 47) return true;
        return date.TimeOfDay != TimeSpan.Zero;
    }

    private static string? Header(IXLCell cell)
    {
        // Formula headers are context only; never evaluate formulas while naming columns.
        var text = cell.HasFormula ? "=" + cell.FormulaA1 : cell.GetFormattedString(CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string UniqueName(string original, HashSet<string> names)
    {
        var candidate = original;
        for (var suffix = 1; !names.Add(candidate); suffix++)
            candidate = original + "_匹配" + (suffix == 1 ? "" : suffix.ToString(CultureInfo.InvariantCulture));
        return candidate;
    }

    private sealed record SheetData(IXLWorksheet Sheet, int HeaderRow, int LastRow, int FirstColumn, int LastColumn);
    private readonly record struct KeyPart(string Kind, object Value);
    private sealed class CompositeKey(KeyPart[] parts) : IEquatable<CompositeKey>
    {
        private readonly KeyPart[] _parts = parts;
        public bool Equals(CompositeKey? other) => other is not null && _parts.AsSpan().SequenceEqual(other._parts);
        public override bool Equals(object? other) => other is CompositeKey key && Equals(key);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var part in _parts) hash.Add(part);
            return hash.ToHashCode();
        }
    }

    private static OperationError Error(OperationErrorCode code, string message, string? detail = null) => new(code, message, detail);
    private static DataMatchingResult Failure(OperationError error) => new(false, null, null, [], null, error);
    private static DataMatchingResult FailureAfterCleanup(OperationError error, string? path) => Failure(TryCleanupStagingFile(path) ?? error);
    private static void Report(
        IProgress<OperationProgress>? progress,
        OperationStage stage,
        int? percent,
        int? processedRows,
        int? totalRows) =>
        progress?.Report(new OperationProgress(stage, percent, processedRows, totalRows));

}
