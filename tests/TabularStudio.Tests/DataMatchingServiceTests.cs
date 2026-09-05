using System.Security.Cryptography;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class DataMatchingServiceTests
{
    private readonly DataMatchingService _service = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchesTwoFilesOrTwoSheetsAndPreservesInputs(bool sameFile)
    {
        using var files = new Files(sameFile: sameFile);
        var result = await _service.ExecuteAsync(files.Request);
        AssertSuccess(result, 1, 1, 0, 0, 0);
        Assert.Equal("匹配状态", result.ActualStatusColumnName);
        using var output = new XLWorkbook(result.OutputFilePath!);
        Assert.Equal(sameFile ? 3 : 2, output.Worksheets.Count);
        Assert.Equal("answer", output.Worksheet("Master").Cell(2, 3).GetString());
        Assert.Equal("匹配成功", output.Worksheet("Master").Cell(2, 4).GetString());
        Assert.Equal("unchanged", output.Worksheet("Other").Cell(1, 1).GetString());
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task CompositeAndKeysDoNotCollideOrMatchPartially()
    {
        using var files = new Files(
            master: s => Rows(s, ["ab", "c"], ["a", "bc"], ["ab", "bc"], ["AB", "c"], ["ab*", "c"]),
            reference: s => Rows(s, ["a", "bc", "second"], ["ab", "c", "first"]));
        var request = files.Request with
        {
            Conditions = [new(new(1, "stale"), new(1, "stale")), new(new(2, "stale"), new(2, "stale"))],
            ReturnFields = [new(3, "wrong")]
        };
        var result = await _service.ExecuteAsync(request);
        AssertSuccess(result, 5, 2, 3, 0, 0);
        using var output = new XLWorkbook(result.OutputFilePath!);
        Assert.Equal("first", output.Worksheet("Master").Cell(2, 3).GetString());
        Assert.Equal("second", output.Worksheet("Master").Cell(3, 3).GetString());
        files.AssertInputsUnchanged();
    }

    public static IEnumerable<object[]> Comparisons()
    {
        yield return [" \u00A0Ａ\t\r\n\u200B\uFEFFＢ\u3000", "AB", true, true];
        yield return [" ＡＢ ", "AB", false, false];
        yield return ["①", "1", true, true];
        yield return ["①", "1", false, false];
        yield return ["A B", "AB", true, false];
        yield return ["a", "A", true, false];
        yield return ["A\u200CB", "AB", true, false];
        yield return ["A\u200DB", "AB", true, false];
        yield return ["abc", "ab", true, false];
        yield return ["123", 123d, true, true];
        yield return [123d, "123", true, true];
        yield return ["123", 123d, false, false];
        yield return ["00123", 123d, true, false];
        yield return ["00123", "00123", true, true];
        yield return [" ００１２３ ", "00123", true, true];
        yield return ["123456789012345678", "123456789012345678", true, true];
        yield return ["123456789012345678", "123456789012345679", true, false];
        yield return ["123456789012345678", 123456789012345680d, true, false];
        yield return ["123456789012345", 123456789012345d, true, true];
        yield return ["1234567890123456", 1234567890123456d, true, false];
        yield return ["12.50", 12.5d, true, true];
        yield return ["0.00000000000000000000000000001", 0d, true, false];
        yield return ["0.000000000000000000000000000123", 1e-28, true, false];
        yield return ["0.0000000000000000000000000001", 1e-28, true, true];
        yield return ["-12.5", -12.5d, true, true];
        yield return ["-0", 0d, true, false];
        yield return ["+123", 123d, true, false];
        yield return ["1E3", 1000d, true, false];
        yield return ["1,234", 1234d, true, false];
        yield return ["12%", 0.12d, true, false];
        yield return ["2026-09-05", "2026/09/05", true, true];
        yield return ["2026-09-05", "2026/09/05", false, false];
        yield return ["2026-09-05 12:13:14", "2026/09/05 12:13:14", true, true];
        yield return ["2026-09-05 12:13:14", "2026/09/05 12:13:15", true, false];
        yield return ["2026-09-05", "2026/09/05 00:00:00", true, false];
        yield return ["01/02/2026", new DateTime(2026, 1, 2), true, false];
        yield return ["01/02/2026", "01/02/2026", true, true];
        yield return ["20260905", new DateTime(2026, 9, 5), true, false];
        yield return ["2026-9-05", "2026-09-05", true, false];
        yield return ["2026-02-30", "2026-02-28", true, false];
        yield return ["2026-09-05T12:13:14", "2026-09-05 12:13:14", true, false];
        yield return ["2026-09-05 12:13:14.000", "2026-09-05 12:13:14", true, false];
        yield return [true, 1d, true, false];
        yield return [true, true, false, true];
        yield return [XLError.DivisionByZero, XLError.DivisionByZero, true, true];
    }

    [Theory]
    [MemberData(nameof(Comparisons))]
    public async Task ComparisonRulesAreTypedExactAndInMemory(object master, object reference, bool normalize, bool matches)
    {
        using var files = new Files(master: s => s.Cell(2, 1).Value = Value(master), reference: s => s.Cell(2, 1).Value = Value(reference));
        var result = await _service.ExecuteAsync(files.Request with { NormalizeComparisonKeys = normalize });
        AssertSuccess(result, 1, matches ? 1 : 0, matches ? 0 : 1, 0, 0);
        using var before = new XLWorkbook(files.MasterPath);
        using var output = new XLWorkbook(result.OutputFilePath!);
        Assert.Equal(before.Worksheet("Master").Cell(2, 1).Value, output.Worksheet("Master").Cell(2, 1).Value);
        files.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData("00123")]
    [InlineData("123456789012345678")]
    [InlineData("mixed")]
    [InlineData("01/02/2026")]
    public async Task EntireTextColumnCounterEvidenceBlocksNumericConversion(string counterEvidence)
    {
        using var files = new Files(master: s => Rows(s, ["123", "row1"], [counterEvidence, "row2"]),
            reference: s => s.Cell(2, 1).Value = 123d);
        AssertSuccess(await _service.ExecuteAsync(files.Request), 2, 0, 2, 0, 0);
        files.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReferenceColumnCounterEvidenceIsCheckedBeyondFirstMatchingRow(bool counterEvidence)
    {
        using var files = new Files(master: s => s.Cell(2, 1).Value = 123d,
            reference: s => { s.Cell(2, 1).Value = "123"; if (counterEvidence) Rows(s, ["123", "answer"], ["00123", "unused"]); });
        AssertSuccess(await _service.ExecuteAsync(files.Request), 1, counterEvidence ? 0 : 1, counterEvidence ? 1 : 0, 0, 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExcelDateTimeSecondsAreNormalizedOnlyWhenEnabled(bool normalize)
    {
        using var files = new Files(master: s => s.Cell(2, 1).Value = new DateTime(2026, 9, 5, 12, 13, 14).AddMilliseconds(100),
            reference: s => s.Cell(2, 1).Value = new DateTime(2026, 9, 5, 12, 13, 14).AddMilliseconds(800));
        AssertSuccess(await _service.ExecuteAsync(files.Request with { NormalizeComparisonKeys = normalize }), 1, normalize ? 1 : 0, normalize ? 0 : 1, 0, 0);
    }

    [Theory]
    [InlineData("yyyy-MM-dd", "2026/09/05", 0)]
    [InlineData("yyyy-MM-dd HH:mm:ss", "2026/09/05 12:13:14", 12)]
    [InlineData("yyyy-MM-dd HH:mm:ss", "2026/09/05 00:00:00", 0)]
    [InlineData("yyyy-MM-dd", "2026/09/05", 12)]
    public async Task ExcelDatesUseTheirExplicitGranularity(string format, string text, int hour)
    {
        using var files = new Files(master: s =>
        {
            s.Cell(2, 1).Value = new DateTime(2026, 9, 5, hour, hour == 0 ? 0 : 13, hour == 0 ? 0 : 14);
            s.Cell(2, 1).Style.NumberFormat.Format = format;
        }, reference: s => s.Cell(2, 1).Value = text);
        AssertSuccess(await _service.ExecuteAsync(files.Request), 1, 1, 0, 0, 0);
    }

    [Fact]
    public async Task AllFourStatusesPreserveBlankPhysicalRowsAndDoNotChooseDuplicates()
    {
        using var files = new Files(master: s => Rows(s, ["dup", "first"], ["unique", "second"], ["none", "third"], [null, null], [" \t\u200B", "fifth"], ["unique", "sixth"]),
            reference: s => Rows(s, ["dup", "same"], ["dup", "same"], [null, "empty1"], [null, "empty2"], ["unique", "hit"]));
        var result = await _service.ExecuteAsync(files.Request);
        AssertSuccess(result, 6, 2, 1, 1, 2);
        using var output = new XLWorkbook(result.OutputFilePath!);
        var sheet = output.Worksheet("Master");
        Assert.Equal(new[] { "重复", "匹配成功", "未匹配", "匹配键为空", "匹配键为空", "匹配成功" }, Enumerable.Range(2, 6).Select(r => sheet.Cell(r, 4).GetString()));
        Assert.Equal(new[] { "", "hit", "", "", "", "hit" }, Enumerable.Range(2, 6).Select(r => sheet.Cell(r, 3).GetString()));
        Assert.Equal("first", sheet.Cell(2, 2).GetString());
        Assert.Equal("sixth", sheet.Cell(7, 2).GetString());
        files.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhitespaceBecomesEmptyOnlyWhenNormalizationEnabled(bool normalize)
    {
        using var files = new Files(master: s => s.Cell(2, 1).Value = " \t\u200B", reference: s => s.Cell(2, 1).Value = " \t\u200B");
        AssertSuccess(await _service.ExecuteAsync(files.Request with { NormalizeComparisonKeys = normalize }), 1, normalize ? 0 : 1, 0, 0, normalize ? 1 : 0);
    }

    [Fact]
    public async Task AnyEmptyCompositePartExcludesReferenceRowsAndMarksMasterEmpty()
    {
        using var files = new Files(master: s => Rows(s, ["key", null], ["key", "part"]),
            reference: s => Rows(s, ["key", null, "ignored"], ["key", null, "ignored"], ["key", "part", "answer"]));
        var request = files.Request with { Conditions = [new(new(1, ""), new(1, "")), new(new(2, ""), new(2, ""))], ReturnFields = [new(3, "")] };
        AssertSuccess(await _service.ExecuteAsync(request), 2, 1, 0, 0, 1);
    }

    [Fact]
    public async Task ReturnedFieldsRespectPhysicalIdentityOrderAndNameCollisions()
    {
        using var files = new Files(master: s =>
        {
            s.Cell(1, 2).Value = "value"; s.Cell(1, 3).Value = "value_匹配"; s.Cell(1, 4).Value = "value_匹配2";
        }, reference: s =>
        {
            s.Cell(1, 2).Value = "value"; s.Cell(1, 3).Value = "value";
            s.Cell(2, 2).Value = "second"; s.Cell(2, 3).Value = "third";
        });
        var result = await _service.ExecuteAsync(files.Request with { ReturnFields = [new(3, "wrong"), new(2, null)], StatusColumn = new() { ColumnName = "value" } });
        AssertSuccess(result, 1, 1, 0, 0, 0);
        Assert.Equal(new[] { 3, 2 }, result.ReturnedFields.Select(f => f.RequestedColumn.ColumnNumber));
        Assert.All(result.ReturnedFields, f => Assert.Equal("value", f.RequestedColumn.HeaderText));
        Assert.Equal(new[] { "value_匹配3", "value_匹配4" }, result.ReturnedFields.Select(f => f.ActualOutputColumnName));
        Assert.Equal("value_匹配5", result.ActualStatusColumnName);
        using var output = new XLWorkbook(result.OutputFilePath!);
        Assert.Equal("third", output.Worksheet("Master").Cell(2, 5).GetString());
        Assert.Equal("second", output.Worksheet("Master").Cell(2, 6).GetString());
        files.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatusColumnCanBeDisabledOrAvoidOriginalAndReturnedNames(bool enabled)
    {
        using var files = new Files(master: s => s.Cell(1, 2).Value = "匹配状态", reference: s => s.Cell(1, 2).Value = "匹配状态");
        var result = await _service.ExecuteAsync(files.Request with { StatusColumn = new() { Enabled = enabled } });
        AssertSuccess(result, 1, 1, 0, 0, 0);
        Assert.Equal(enabled ? "匹配状态_匹配2" : null, result.ActualStatusColumnName);
        Assert.Equal("匹配状态_匹配", result.ReturnedFields[0].ActualOutputColumnName);
        using var output = new XLWorkbook(result.OutputFilePath!);
        Assert.Equal(enabled ? 4 : 3, output.Worksheet("Master").LastColumnUsed()!.ColumnNumber());
    }

    [Theory]
    [InlineData("master")]
    [InlineData("reference")]
    [InlineData("return")]
    public async Task FormulaInAnySelectedDataRowFailsEvenWhenRowCannotMatch(string location)
    {
        using var files = new Files(master: s => { if (location == "master") s.Cell(9, 1).FormulaA1 = "1+1"; },
            reference: s => { if (location != "master") s.Cell(9, location == "return" ? 2 : 1).FormulaA1 = "1+1"; });
        var stages = new List<OperationProgress>();
        var result = await _service.ExecuteAsync(files.Request, new ProgressSink(stages.Add));
        AssertFailure(result, OperationErrorCode.FormulaCellNotAllowedForMatching);
        Assert.DoesNotContain(stages, p => p.Stage == OperationStage.Completed);
        Assert.False(File.Exists(files.OutputPath));
        Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task PreservesUnselectedFormulaStyleAndRowsBeforeHeader()
    {
        using var files = new Files(master: s =>
        {
            s.Cell(1, 1).Value = "title"; s.Cell(2, 1).Value = "key"; s.Cell(2, 2).Value = "formula";
            s.Cell(3, 1).Value = "k"; s.Cell(3, 2).FormulaA1 = "1+1";
            s.Cell(3, 1).Style.Fill.BackgroundColor = XLColor.Yellow;
            s.Column(1).Width = 25; s.Row(3).Height = 30;
        }, reference: s =>
        {
            s.Cell(1, 1).FormulaA1 = "1+1"; s.Cell(2, 1).Value = "title";
            s.Cell(3, 1).Value = "key"; s.Cell(3, 2).Value = "return";
            s.Cell(4, 1).Value = "k"; s.Cell(4, 2).Value = new DateTime(2026, 9, 5);
            s.Cell(4, 2).Style.NumberFormat.Format = "yyyy-MM-dd";
        });
        var request = files.Request with { Master = files.Request.Master with { HeaderRowNumber = 2 }, Reference = files.Request.Reference with { HeaderRowNumber = 3 } };
        var result = await _service.ExecuteAsync(request);
        AssertSuccess(result, 1, 1, 0, 0, 0);
        using var output = new XLWorkbook(result.OutputFilePath!);
        var sheet = output.Worksheet("Master");
        Assert.Equal("title", sheet.Cell(1, 1).GetString());
        Assert.True(sheet.Cell(1, 3).IsEmpty());
        Assert.Equal("1+1", sheet.Cell(3, 2).FormulaA1);
        Assert.Equal(XLColor.Yellow, sheet.Cell(3, 1).Style.Fill.BackgroundColor);
        Assert.Equal(25, sheet.Column(1).Width); Assert.Equal(30, sheet.Row(3).Height);
        Assert.Equal(XLDataType.DateTime, sheet.Cell(3, 3).DataType);
        Assert.Equal("yyyy-MM-dd", sheet.Cell(3, 3).Style.NumberFormat.Format);
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task HeaderOnlyMasterHasZeroSummaryAndCompleteMappings()
    {
        using var files = new Files(master: s => s.Row(2).Clear());
        var result = await _service.ExecuteAsync(files.Request);
        AssertSuccess(result, 0, 0, 0, 0, 0);
        Assert.Single(result.ReturnedFields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputNeverOverwritesEitherInput(bool reference)
    {
        using var files = new Files();
        var result = await _service.ExecuteAsync(files.Request with { OutputFilePath = reference ? files.ReferencePath : files.MasterPath, OverwriteExistingOutput = true });
        AssertFailure(result, OperationErrorCode.OutputConflictsWithInput);
        files.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingOutputRequiresExplicitOverwrite(bool overwrite)
    {
        using var files = new Files();
        File.WriteAllText(files.OutputPath, "existing output sentinel");
        var result = await _service.ExecuteAsync(files.Request with { OverwriteExistingOutput = overwrite });
        if (overwrite) AssertSuccess(result, 1, 1, 0, 0, 0);
        else
        {
            AssertFailure(result, OperationErrorCode.OutputAlreadyExists);
            Assert.Equal("existing output sentinel", File.ReadAllText(files.OutputPath));
        }
        files.AssertInputsUnchanged();
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        foreach (var kind in new[] { "null", "master-null", "reference-null", "conditions-null", "conditions-empty", "condition-null", "column-null", "returns-null", "returns-empty", "return-null", "status-null", "status-name", "blank-path", "invalid-path", "blank-sheet", "output-blank" })
            yield return [kind, OperationErrorCode.InvalidConfiguration];
        foreach (var kind in new[] { "sheet", "reference-sheet" }) yield return [kind, OperationErrorCode.WorksheetNotFound];
        foreach (var kind in new[] { "header-zero", "header-high", "header-empty" }) yield return [kind, OperationErrorCode.InvalidHeaderRow];
        foreach (var kind in new[] { "column-zero", "column-high", "return-high" }) yield return [kind, OperationErrorCode.ColumnNotFound];
        foreach (var kind in new[] { "missing", "reference-missing" }) yield return [kind, OperationErrorCode.FileNotFound];
        foreach (var kind in new[] { "extension", "output-extension" }) yield return [kind, OperationErrorCode.UnsupportedFileType];
        yield return ["directory", OperationErrorCode.OutputDirectoryNotWritable];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequestsReturnStructuredFailures(string kind, OperationErrorCode expected)
    {
        using var files = new Files();
        var r = files.Request;
        var request = kind switch
        {
            "null" => null,
            "master-null" => r with { Master = null! },
            "reference-null" => r with { Reference = null! },
            "conditions-null" => r with { Conditions = null! },
            "conditions-empty" => r with { Conditions = [] },
            "condition-null" => r with { Conditions = [null!] },
            "column-null" => r with { Conditions = [new(null!, new(1, "key"))] },
            "returns-null" => r with { ReturnFields = null! },
            "returns-empty" => r with { ReturnFields = [] },
            "return-null" => r with { ReturnFields = [null!] },
            "status-null" => r with { StatusColumn = null! },
            "status-name" => r with { StatusColumn = new() { ColumnName = " " } },
            "blank-path" => r with { Master = r.Master with { FilePath = " " } },
            "invalid-path" => r with { OutputFilePath = "\0" },
            "blank-sheet" => r with { Master = r.Master with { WorksheetName = " " } },
            "sheet" => r with { Master = r.Master with { WorksheetName = "Missing" } },
            "reference-sheet" => r with { Reference = r.Reference with { WorksheetName = "Missing" } },
            "header-zero" => r with { Master = r.Master with { HeaderRowNumber = 0 } },
            "header-high" => r with { Reference = r.Reference with { HeaderRowNumber = int.MaxValue } },
            "header-empty" => r with { Master = r.Master with { HeaderRowNumber = 99 } },
            "column-zero" => r with { Conditions = [new(new(0, "key"), new(1, "key"))] },
            "column-high" => r with { Conditions = [new(new(1, "key"), new(int.MaxValue, "key"))] },
            "return-high" => r with { ReturnFields = [new(100, "return")] },
            "missing" => r with { Master = r.Master with { FilePath = Path.Combine(files.DirectoryPath, "absent.xlsx") } },
            "reference-missing" => r with { Reference = r.Reference with { FilePath = Path.Combine(files.DirectoryPath, "absent.xlsx") } },
            "extension" => r with { Reference = r.Reference with { FilePath = Path.ChangeExtension(files.ReferencePath, ".csv") } },
            "output-extension" => r with { OutputFilePath = Path.ChangeExtension(files.OutputPath, ".csv") },
            "output-blank" => r with { OutputFilePath = " " },
            "directory" => r with { OutputFilePath = Path.Combine(files.DirectoryPath, "missing", "output.xlsx") },
            _ => throw new InvalidOperationException()
        };
        AssertFailure(await _service.ExecuteAsync(request!), expected);
        Assert.False(File.Exists(files.OutputPath));
        Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
        files.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData("master")]
    [InlineData("reference")]
    [InlineData("return")]
    public async Task ActualEmptyHeaderIsRejectedEvenWithForgedRequestText(string side)
    {
        using var files = new Files(master: s => { if (side == "master") s.Cell(1, 1).Clear(); },
            reference: s => { if (side != "master") s.Cell(1, side == "return" ? 2 : 1).Clear(); });
        AssertFailure(await _service.ExecuteAsync(files.Request), OperationErrorCode.InvalidConfiguration);
        files.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptWorkbookIsUnreadableAndInputsArePreserved(bool reference)
    {
        using var files = new Files();
        File.WriteAllText(reference ? files.ReferencePath : files.MasterPath, "synthetic corrupt xlsx");
        files.RefreshHashes();
        AssertFailure(await _service.ExecuteAsync(files.Request), OperationErrorCode.WorkbookUnreadable);
        files.AssertInputsUnchanged();
        Assert.False(File.Exists(files.OutputPath));
    }

    [Theory]
    [InlineData("master")]
    [InlineData("reference")]
    [InlineData("output")]
    public async Task ExclusiveFileLockReturnsFileLocked(string target)
    {
        using var files = new Files();
        if (target == "output") File.WriteAllText(files.OutputPath, "existing");
        using (var locked = new FileStream(target == "master" ? files.MasterPath : target == "reference" ? files.ReferencePath : files.OutputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            AssertFailure(await _service.ExecuteAsync(files.Request with { OverwriteExistingOutput = true }), OperationErrorCode.FileLocked);
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task ProgressUsesApprovedOrderedStagesAndCompleteSummary()
    {
        using var files = new Files();
        var progress = new List<OperationProgress>();
        AssertSuccess(await _service.ExecuteAsync(files.Request, new ProgressSink(progress.Add)), 1, 1, 0, 0, 0);
        Assert.Equal(new[] { OperationStage.Reading, OperationStage.Preparing, OperationStage.Processing, OperationStage.Writing, OperationStage.Completed }, progress.Select(p => p.Stage).Distinct());
        Assert.All(progress.Where(p => p.Percent.HasValue), p => Assert.InRange(p.Percent!.Value, 0, 100));
        Assert.Equal(new OperationProgress(OperationStage.Completed, 100, 1, 1), progress[^1]);
    }

    [Theory]
    [InlineData(OperationStage.Reading)]
    [InlineData(OperationStage.Preparing)]
    [InlineData(OperationStage.Processing)]
    [InlineData(OperationStage.Writing)]
    public async Task CancellationPropagatesAndCleansStaging(OperationStage stage)
    {
        using var files = new Files();
        using var cancellation = new CancellationTokenSource();
        var progress = new List<OperationProgress>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExecuteAsync(files.Request, new ProgressSink(p =>
        {
            progress.Add(p);
            if (p.Stage == stage) cancellation.Cancel();
        }), cancellation.Token));
        Assert.DoesNotContain(progress, p => p.Stage == OperationStage.Completed);
        Assert.False(File.Exists(files.OutputPath));
        Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task PreCanceledTokenPropagatesWithoutOutput()
    {
        using var files = new Files();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExecuteAsync(files.Request, cancellationToken: new CancellationToken(true)));
        Assert.False(File.Exists(files.OutputPath));
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task FailedWriteCleansPartialFileAndPreservesExistingOutput()
    {
        using var files = new Files();
        File.WriteAllText(files.OutputPath, "existing");
        var result = await _service.ExecuteAsync(files.Request with { OverwriteExistingOutput = true }, new ProgressSink(p =>
        {
            if (p.Stage != OperationStage.Writing) return;
            File.WriteAllText(Assert.Single(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx")), "partial");
            throw new IOException("synthetic write failure");
        }));
        AssertFailure(result, OperationErrorCode.ProcessingFailed);
        Assert.Equal("existing", File.ReadAllText(files.OutputPath));
        Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task CleanupFailureReportsIncompletePathAndNoSuccessData()
    {
        using var files = new Files();
        FileStream? locked = null;
        try
        {
            var result = await _service.ExecuteAsync(files.Request, new ProgressSink(p =>
            {
                if (p.Stage != OperationStage.Writing) return;
                var staging = Assert.Single(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
                File.WriteAllText(staging, "incomplete");
                locked = new FileStream(staging, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                throw new IOException("synthetic failure");
            }));
            AssertFailure(result, OperationErrorCode.IncompleteOutputCleanupFailed);
            Assert.Equal(locked!.Name, result.Error!.Detail);
            Assert.False(File.Exists(files.OutputPath));
            files.AssertInputsUnchanged();
        }
        finally { locked?.Dispose(); }
    }

    [Fact]
    public async Task OutputAppearingDuringProcessingIsNotSilentlyOverwritten()
    {
        using var files = new Files();
        var result = await _service.ExecuteAsync(files.Request, new ProgressSink(p =>
        {
            if (p.Stage == OperationStage.Writing) File.WriteAllText(files.OutputPath, "racing output");
        }));
        AssertFailure(result, OperationErrorCode.OutputAlreadyExists);
        Assert.Equal("racing output", File.ReadAllText(files.OutputPath));
        Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task SparseColumnsAreLocatedByNumberAndAppendedAfterLastOriginalField()
    {
        using var files = new Files(master: s =>
        {
            s.Clear(); s.Cell(1, 3).Value = "same"; s.Cell(1, 5).Value = "same";
            s.Cell(2, 3).Value = "wrong"; s.Cell(2, 5).Value = "key";
        }, reference: s =>
        {
            s.Clear(); s.Cell(1, 2).Value = "same"; s.Cell(1, 4).Value = "same";
            s.Cell(2, 2).Value = "key"; s.Cell(2, 4).Value = "returned";
            s.Cell(1, 6).Value = "unselected formula"; s.Cell(2, 6).FormulaA1 = "1+1";
        });
        var request = files.Request with { Conditions = [new(new(5, "forged"), new(2, "forged"))], ReturnFields = [new(4, "forged")] };
        var result = await _service.ExecuteAsync(request);
        AssertSuccess(result, 1, 1, 0, 0, 0);
        using var output = new XLWorkbook(result.OutputFilePath!);
        Assert.Equal("returned", output.Worksheet("Master").Cell(2, 6).GetString());
        Assert.Equal("same_匹配", result.ReturnedFields[0].ActualOutputColumnName);
        AssertFailure(await _service.ExecuteAsync(request with { OutputFilePath = Path.Combine(files.DirectoryPath, "invalid.xlsx"), Conditions = [new(new(1, "same"), new(2, "same"))] }), OperationErrorCode.ColumnNotFound);
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task NoRoomForAppendedColumnsIsInvalidConfiguration()
    {
        using var files = new Files(master: s => s.Cell(1, XLHelper.MaxColumnNumber).Value = "last");
        AssertFailure(await _service.ExecuteAsync(files.Request), OperationErrorCode.InvalidConfiguration);
        Assert.False(File.Exists(files.OutputPath));
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task CancellationDuringRowsPreservesExistingOutput()
    {
        using var files = new Files(master: s => Rows(s, ["k", "1"], ["k", "2"], ["k", "3"]));
        File.WriteAllText(files.OutputPath, "existing");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExecuteAsync(files.Request with { OverwriteExistingOutput = true }, new ProgressSink(p =>
        {
            if (p.Stage == OperationStage.Processing && p.ProcessedRows == 1) cancellation.Cancel();
        }), cancellation.Token));
        Assert.Equal("existing", File.ReadAllText(files.OutputPath));
        Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
        files.AssertInputsUnchanged();
    }

    [Fact]
    public async Task CommitFailureAfterWorkbookSerializationCleansStaging()
    {
        using var files = new Files();
        FileStream? locked = null;
        try
        {
            var result = await _service.ExecuteAsync(files.Request with { OverwriteExistingOutput = true }, new ProgressSink(p =>
            {
                if (p.Stage != OperationStage.Writing) return;
                File.WriteAllText(files.OutputPath, "existing");
                locked = new FileStream(files.OutputPath, FileMode.Open, FileAccess.Read, FileShare.None);
            }));
            AssertFailure(result, OperationErrorCode.FileLocked);
            Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx"));
            files.AssertInputsUnchanged();
        }
        finally { locked?.Dispose(); }
        Assert.Equal("existing", File.ReadAllText(files.OutputPath));
    }

    [Fact]
    public async Task CancellationWithLockedStagingPropagatesAndIdentifiesCleanupFailure()
    {
        using var files = new Files();
        using var cancellation = new CancellationTokenSource();
        FileStream? locked = null;
        try
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExecuteAsync(files.Request, new ProgressSink(p =>
            {
                if (p.Stage != OperationStage.Writing) return;
                locked = new FileStream(Assert.Single(Directory.GetFiles(files.DirectoryPath, "*.staging.xlsx")), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                cancellation.Cancel();
            }), cancellation.Token));
            Assert.Equal(locked!.Name, exception.Data[nameof(OperationErrorCode.IncompleteOutputCleanupFailed)]);
            Assert.False(File.Exists(files.OutputPath));
            files.AssertInputsUnchanged();
        }
        finally { locked?.Dispose(); }
    }

    [Fact]
    public async Task CompletedObserverFailureDoesNotInvalidateCommittedOutput()
    {
        using var files = new Files();
        AssertSuccess(await _service.ExecuteAsync(files.Request, new ProgressSink(p =>
        {
            if (p.Stage == OperationStage.Completed) throw new InvalidOperationException("observer failed");
        })), 1, 1, 0, 0, 0);
        files.AssertInputsUnchanged();
    }

    private static void AssertSuccess(DataMatchingResult result, int total, int matched, int unmatched, int duplicate, int empty)
    {
        Assert.True(result.Success, result.Error?.ToString());
        Assert.NotNull(result.OutputFilePath); Assert.True(File.Exists(result.OutputFilePath));
        Assert.Null(result.Error); Assert.NotEmpty(result.ReturnedFields);
        var summary = Assert.IsType<DataMatchingSummary>(result.Summary);
        Assert.Equal(total, summary.TotalMasterDataRowCount); Assert.Equal(matched, summary.MatchedCount);
        Assert.Equal(unmatched, summary.UnmatchedCount); Assert.Equal(duplicate, summary.DuplicateCount); Assert.Equal(empty, summary.EmptyKeyCount);
        Assert.Equal(total, summary.MatchedCount + summary.UnmatchedCount + summary.DuplicateCount + summary.EmptyKeyCount);
        Assert.True(summary.Elapsed >= TimeSpan.Zero);
    }

    private static void AssertFailure(DataMatchingResult result, OperationErrorCode code)
    {
        Assert.False(result.Success); Assert.Null(result.OutputFilePath); Assert.Null(result.Summary);
        Assert.Empty(result.ReturnedFields); Assert.Null(result.ActualStatusColumnName);
        Assert.Equal(code, Assert.IsType<OperationError>(result.Error).Code);
    }

    private static XLCellValue Value(object? value) => value switch
    {
        null => Blank.Value, string text => text, double number => number,
        bool flag => flag, DateTime date => date, XLError error => error,
        _ => throw new ArgumentException("Unsupported synthetic value.")
    };

    private static void Rows(IXLWorksheet sheet, params object?[][] rows)
    {
        sheet.RangeUsed()?.Clear(XLClearOptions.Contents);
        var count = rows.Max(r => r.Length);
        for (var c = 0; c < count; c++) sheet.Cell(1, c + 1).Value = "field" + c;
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = Value(rows[r][c]);
    }

    private sealed class ProgressSink(Action<OperationProgress> action) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => action(value);
    }

    private sealed class Files : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "TabularStudio.MatchingTests", Guid.NewGuid().ToString("N"));
        public string MasterPath => Path.Combine(DirectoryPath, "master.xlsx");
        public string ReferencePath { get; }
        public string OutputPath => Path.Combine(DirectoryPath, "output.xlsx");
        private string _masterHash = "";
        private string _referenceHash = "";
        public DataMatchingRequest Request => new(new(MasterPath, "Master", 1), new(ReferencePath, "Reference", 1),
            [new(new(1, "stale key"), new(1, "stale key"))], [new(2, "stale return")], true, new(), OutputPath, false);

        public Files(Action<IXLWorksheet>? master = null, Action<IXLWorksheet>? reference = null, bool sameFile = false)
        {
            Directory.CreateDirectory(DirectoryPath);
            ReferencePath = sameFile ? MasterPath : Path.Combine(DirectoryPath, "reference.xlsx");
            using var masterBook = new XLWorkbook();
            var masterSheet = masterBook.AddWorksheet("Master");
            Rows(masterSheet, ["k", "original"]);
            master?.Invoke(masterSheet);
            masterBook.AddWorksheet("Other").Cell(1, 1).Value = "unchanged";
            using var referenceBook = new XLWorkbook();
            var referenceSheet = (sameFile ? masterBook : referenceBook).AddWorksheet("Reference");
            Rows(referenceSheet, ["k", "answer"]);
            reference?.Invoke(referenceSheet);
            masterBook.SaveAs(MasterPath);
            if (!sameFile) referenceBook.SaveAs(ReferencePath);
            RefreshHashes();
        }
        public void RefreshHashes() { _masterHash = Hash(MasterPath); _referenceHash = Hash(ReferencePath); }
        public void AssertInputsUnchanged() { Assert.Equal(_masterHash, Hash(MasterPath)); Assert.Equal(_referenceHash, Hash(ReferencePath)); }
        private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
