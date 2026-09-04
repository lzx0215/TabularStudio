using System.Security.Cryptography;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class FormatStandardizationServiceTests
{
    private static readonly FormatStandardizationOptions AllOptionsDisabled = new()
    {
        TrimOuterWhitespace = false,
        RemoveTabsNewLinesAndHiddenCharacters = false,
        NormalizeFullWidthHalfWidth = false,
        NormalizeUnicode = false,
        NormalizeSafeNumbers = false,
        NormalizeUnambiguousDates = false
    };

    private readonly FormatStandardizationService _service = new();

    [Fact]
    public async Task ExecuteAsync_ProcessesOnlyActualDataRowsInSelectedWorksheet()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var selected = workbook.AddWorksheet("Data");
            selected.Cell(1, 1).Value = "  标题  ";
            selected.Cell(2, 1).Value = "  表头  ";
            selected.Cell(3, 1).Value = "  第一行  ";
            selected.Cell(4, 1).Style.Fill.BackgroundColor = XLColor.Yellow;
            selected.Cell(5, 1).Value = "  第二行  ";

            var other = workbook.AddWorksheet("Other");
            other.Cell(1, 1).Value = "表头";
            other.Cell(2, 1).Value = "  不应处理  ";
        });

        var result = await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { TrimOuterWhitespace = true },
            headerRowNumber: 2);

        AssertSuccess(result);
        Assert.Equal(2, result.Summary!.ProcessedDataRowCount);

        using var output = new XLWorkbook(workspace.OutputPath);
        var selected = output.Worksheet("Data");
        Assert.Equal("  标题  ", selected.Cell(1, 1).GetString());
        Assert.Equal("  表头  ", selected.Cell(2, 1).GetString());
        Assert.Equal("第一行", selected.Cell(3, 1).GetString());
        Assert.True(selected.Cell(4, 1).IsEmpty(XLCellsUsedOptions.Contents));
        Assert.Equal("第二行", selected.Cell(5, 1).GetString());
        Assert.Equal("  不应处理  ", output.Worksheet("Other").Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ExecuteAsync_PreservesInputFileBytes()
    {
        using var workspace = CreateSingleColumnWorkbook("  测试甲  ");
        var beforeHash = ComputeSha256(workspace.InputPath);

        var result = await ExecuteAsync(workspace);

        AssertSuccess(result);
        Assert.Equal(beforeHash, ComputeSha256(workspace.InputPath));
    }

    [Fact]
    public async Task TrimOption_RemovesOnlyApprovedOuterWhitespaceAndPreservesInternalSpace()
    {
        using var enabled = CreateSingleColumnWorkbook(" \u00A0\u3000A B\u3000\u00A0 ");
        using var disabled = CreateSingleColumnWorkbook(" \u00A0\u3000A B\u3000\u00A0 ");

        AssertSuccess(await ExecuteAsync(
            enabled,
            AllOptionsDisabled with { TrimOuterWhitespace = true }));
        AssertSuccess(await ExecuteAsync(disabled, AllOptionsDisabled));

        Assert.Equal("A B", ReadOutputText(enabled));
        Assert.Equal(" \u00A0\u3000A B\u3000\u00A0 ", ReadOutputText(disabled));
    }

    [Fact]
    public async Task HiddenCharacterOption_RemovesOnlyApprovedCharactersWithoutAddingSpaces()
    {
        const string source = "A\t\r\n\u0001\u001F\u007F\u200B\uFEFFB\u200C\u200D";
        using var enabled = CreateSingleColumnWorkbook(source);
        using var disabled = CreateSingleColumnWorkbook(source);
        string persistedSource;

        using (var workbook = new XLWorkbook(disabled.InputPath))
        {
            persistedSource = workbook.Worksheet("Data").Cell(2, 1).GetString();
        }

        AssertSuccess(await ExecuteAsync(
            enabled,
            AllOptionsDisabled with { RemoveTabsNewLinesAndHiddenCharacters = true }));
        AssertSuccess(await ExecuteAsync(disabled, AllOptionsDisabled));

        Assert.Equal("AB\u200C\u200D", ReadOutputText(enabled));
        Assert.Equal(persistedSource, ReadOutputText(disabled));
    }

    [Fact]
    public async Task UnicodeOption_AppliesNfkcOnlyWhenEnabled()
    {
        using var enabled = CreateSingleColumnWorkbook("①ﬀ");
        using var disabled = CreateSingleColumnWorkbook("①ﬀ");

        AssertSuccess(await ExecuteAsync(
            enabled,
            AllOptionsDisabled with { NormalizeUnicode = true }));
        AssertSuccess(await ExecuteAsync(disabled, AllOptionsDisabled));

        Assert.Equal("1ff", ReadOutputText(enabled));
        Assert.Equal("①ﬀ", ReadOutputText(disabled));
    }

    [Fact]
    public async Task WidthOption_ConvertsOnlyApprovedFullWidthRangeWhenEnabled()
    {
        const string source = "ＡＢＣ　１２３、￥ｶ";
        using var enabled = CreateSingleColumnWorkbook(source);
        using var disabled = CreateSingleColumnWorkbook(source);

        AssertSuccess(await ExecuteAsync(
            enabled,
            AllOptionsDisabled with { NormalizeFullWidthHalfWidth = true }));
        AssertSuccess(await ExecuteAsync(disabled, AllOptionsDisabled));

        Assert.Equal("ABC 123、￥ｶ", ReadOutputText(enabled));
        Assert.Equal(source, ReadOutputText(disabled));
    }

    [Fact]
    public async Task TextRules_RunInApprovedOrder()
    {
        using var workspace = CreateSingleColumnWorkbook("　Ａ\t B　");

        AssertSuccess(await ExecuteAsync(workspace, new FormatStandardizationOptions
        {
            NormalizeSafeNumbers = false,
            NormalizeUnambiguousDates = false
        }));

        Assert.Equal("A B", ReadOutputText(workspace));
    }

    [Fact]
    public async Task SafeNumberOption_ConvertsPureSafeNumericTextColumnToNumbers()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "值";
            sheet.Cell(2, 1).Value = "123";
            sheet.Cell(3, 1).Value = "456";
        });

        AssertSuccess(await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { NormalizeSafeNumbers = true }));

        using var output = new XLWorkbook(workspace.OutputPath);
        var sheet = output.Worksheet("Data");
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 1).DataType);
        Assert.Equal(123d, sheet.Cell(2, 1).GetDouble());
        Assert.Equal(XLDataType.Number, sheet.Cell(3, 1).DataType);
        Assert.Equal(456d, sheet.Cell(3, 1).GetDouble());
    }

    [Fact]
    public async Task SafeNumberOption_DoesNotConvertWhenDisabled()
    {
        using var workspace = CreateSingleColumnWorkbook("123");

        AssertSuccess(await ExecuteAsync(workspace, AllOptionsDisabled));

        using var output = new XLWorkbook(workspace.OutputPath);
        Assert.Equal(XLDataType.Text, output.Worksheet("Data").Cell(2, 1).DataType);
        Assert.Equal("123", output.Worksheet("Data").Cell(2, 1).GetString());
    }

    [Fact]
    public async Task NumericColumnAnalysis_PreservesWholeColumnWhenLeadingZeroIsPresent()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "编号";
            sheet.Cell(2, 1).Value = "123";
            sheet.Cell(3, 1).Value = "00123";
        });

        AssertSuccess(await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { NormalizeSafeNumbers = true }));

        using var output = new XLWorkbook(workspace.OutputPath);
        var sheet = output.Worksheet("Data");
        Assert.Equal(XLDataType.Text, sheet.Cell(2, 1).DataType);
        Assert.Equal("123", sheet.Cell(2, 1).GetString());
        Assert.Equal(XLDataType.Text, sheet.Cell(3, 1).DataType);
        Assert.Equal("00123", sheet.Cell(3, 1).GetString());
    }

    [Fact]
    public async Task SafeNumbers_ConvertFifteenDigitsAndPreserveSixteenDigits()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "安全";
            sheet.Cell(1, 2).Value = "过长";
            sheet.Cell(2, 1).Value = "123456789012345";
            sheet.Cell(2, 2).Value = "1234567890123456";
        });

        AssertSuccess(await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { NormalizeSafeNumbers = true }));

        using var output = new XLWorkbook(workspace.OutputPath);
        var sheet = output.Worksheet("Data");
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 1).DataType);
        Assert.Equal(123456789012345d, sheet.Cell(2, 1).GetDouble());
        Assert.Equal(XLDataType.Text, sheet.Cell(2, 2).DataType);
        Assert.Equal("1234567890123456", sheet.Cell(2, 2).GetString());
    }

    [Theory]
    [InlineData("1,234")]
    [InlineData("12%")]
    [InlineData("￥100")]
    [InlineData("1E3")]
    [InlineData("123元")]
    public async Task UnsafeNumericAppearances_RemainText(string source)
    {
        using var workspace = CreateSingleColumnWorkbook(source);

        AssertSuccess(await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { NormalizeSafeNumbers = true }));

        using var output = new XLWorkbook(workspace.OutputPath);
        var cell = output.Worksheet("Data").Cell(2, 1);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(source, cell.GetString());
    }

    [Fact]
    public async Task ApprovedDateFormats_ConvertToDateValuesAndExactNumberFormats()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "日期";
            sheet.Cell(2, 1).Value = "2026-09-04";
            sheet.Cell(3, 1).Value = "2026/09/05";
            sheet.Cell(4, 1).Value = "2026-09-06 12:13:14";
            sheet.Cell(5, 1).Value = "2026/09/07 01:02:03";
        });

        AssertSuccess(await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { NormalizeUnambiguousDates = true }));

        using var output = new XLWorkbook(workspace.OutputPath);
        var sheet = output.Worksheet("Data");
        AssertDateCell(sheet.Cell(2, 1), new DateTime(2026, 9, 4), "yyyy-MM-dd");
        AssertDateCell(sheet.Cell(3, 1), new DateTime(2026, 9, 5), "yyyy-MM-dd");
        AssertDateCell(sheet.Cell(4, 1), new DateTime(2026, 9, 6, 12, 13, 14), "yyyy-MM-dd HH:mm:ss");
        AssertDateCell(sheet.Cell(5, 1), new DateTime(2026, 9, 7, 1, 2, 3), "yyyy-MM-dd HH:mm:ss");
    }

    [Theory]
    [InlineData("01/02/2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-1-2")]
    [InlineData("2026.09.04")]
    public async Task UnapprovedOrInvalidDates_RemainText(string source)
    {
        using var workspace = CreateSingleColumnWorkbook(source);

        AssertSuccess(await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { NormalizeUnambiguousDates = true }));

        using var output = new XLWorkbook(workspace.OutputPath);
        var cell = output.Worksheet("Data").Cell(2, 1);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(source, cell.GetString());
    }

    [Fact]
    public async Task CompactDateAppearance_IsNotParsedAsDateAndMayFollowSafeNumberRule()
    {
        using var workspace = CreateSingleColumnWorkbook("20260902");

        AssertSuccess(await ExecuteAsync(workspace));

        using var output = new XLWorkbook(workspace.OutputPath);
        var cell = output.Worksheet("Data").Cell(2, 1);
        Assert.Equal(XLDataType.Number, cell.DataType);
        Assert.Equal(20260902d, cell.GetDouble());
    }

    [Fact]
    public async Task DateOption_DoesNotConvertWhenDisabled()
    {
        using var workspace = CreateSingleColumnWorkbook("2026-09-04");

        AssertSuccess(await ExecuteAsync(workspace, AllOptionsDisabled));

        using var output = new XLWorkbook(workspace.OutputPath);
        var cell = output.Worksheet("Data").Cell(2, 1);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal("2026-09-04", cell.GetString());
    }

    [Fact]
    public async Task FormulaExpressions_ArePreservedAndSkippedBeforeAllStandardization()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "值";
            sheet.Cell(2, 1).FormulaA1 = "CONCAT(\"  \",\"Ａ\")";
            sheet.Cell(3, 1).Value = "  普通文本  ";
            sheet.Cell(3, 2).FormulaR1C1 = "RC[-1]";
        });

        string expectedA1;
        string expectedR1C1;
        using (var input = new XLWorkbook(workspace.InputPath))
        {
            expectedA1 = input.Worksheet("Data").Cell(2, 1).FormulaA1;
            expectedR1C1 = input.Worksheet("Data").Cell(3, 2).FormulaR1C1;
        }

        AssertSuccess(await ExecuteAsync(workspace));

        using var output = new XLWorkbook(workspace.OutputPath);
        var sheet = output.Worksheet("Data");
        Assert.True(sheet.Cell(2, 1).HasFormula);
        Assert.Equal(expectedA1, sheet.Cell(2, 1).FormulaA1);
        Assert.True(sheet.Cell(3, 2).HasFormula);
        Assert.Equal(expectedR1C1, sheet.Cell(3, 2).FormulaR1C1);
        Assert.Equal("普通文本", sheet.Cell(3, 1).GetString());
    }

    [Fact]
    public async Task ExistingStylesLayoutMergedRangesAndWorksheetOrder_ArePreserved()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var selected = workbook.AddWorksheet("Data");
            selected.Cell(1, 1).Value = "表头";
            var styledCell = selected.Cell(2, 1);
            styledCell.Value = "  A  ";
            styledCell.Style.Font.Bold = true;
            styledCell.Style.Font.FontName = "Consolas";
            styledCell.Style.Font.FontSize = 14;
            styledCell.Style.Font.FontColor = XLColor.Red;
            styledCell.Style.Fill.BackgroundColor = XLColor.Yellow;
            styledCell.Style.Border.BottomBorder = XLBorderStyleValues.Thick;
            styledCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            styledCell.Style.NumberFormat.Format = "@";
            selected.Row(2).Height = 27;
            selected.Column(1).Width = 33;
            selected.Range("A3:B3").Merge();
            selected.Cell(3, 1).Value = "  合并  ";

            var other = workbook.AddWorksheet("Other");
            other.Cell(1, 1).Value = "  原值  ";
        });

        AssertSuccess(await ExecuteAsync(
            workspace,
            AllOptionsDisabled with { TrimOuterWhitespace = true }));

        using var output = new XLWorkbook(workspace.OutputPath);
        Assert.Equal(["Data", "Other"], output.Worksheets.Select(sheet => sheet.Name));
        var selected = output.Worksheet("Data");
        var styledCell = selected.Cell(2, 1);
        Assert.Equal("A", styledCell.GetString());
        Assert.True(styledCell.Style.Font.Bold);
        Assert.Equal("Consolas", styledCell.Style.Font.FontName);
        Assert.Equal(14d, styledCell.Style.Font.FontSize);
        Assert.Equal(XLColor.Red, styledCell.Style.Font.FontColor);
        Assert.Equal(XLColor.Yellow, styledCell.Style.Fill.BackgroundColor);
        Assert.Equal(XLBorderStyleValues.Thick, styledCell.Style.Border.BottomBorder);
        Assert.Equal(XLAlignmentHorizontalValues.Center, styledCell.Style.Alignment.Horizontal);
        Assert.Equal("@", styledCell.Style.NumberFormat.Format);
        Assert.Equal(27d, selected.Row(2).Height);
        Assert.Equal(33d, selected.Column(1).Width);
        Assert.True(selected.Range("A3:B3").IsMerged());
        Assert.Equal("合并", selected.Cell(3, 1).GetString());
        Assert.Equal("  原值  ", output.Worksheet("Other").Cell(1, 1).GetString());
    }

    [Fact]
    public async Task ExistingNonTextCellTypesRemainUnchanged()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "值";
            sheet.Cell(2, 1).Value = 12.5;
            sheet.Cell(3, 1).Value = new DateTime(2026, 9, 4);
            sheet.Cell(4, 1).Value = true;
        });

        AssertSuccess(await ExecuteAsync(workspace));

        using var output = new XLWorkbook(workspace.OutputPath);
        var sheet = output.Worksheet("Data");
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 1).DataType);
        Assert.Equal(12.5d, sheet.Cell(2, 1).GetDouble());
        Assert.Equal(XLDataType.DateTime, sheet.Cell(3, 1).DataType);
        Assert.Equal(new DateTime(2026, 9, 4), sheet.Cell(3, 1).GetDateTime());
        Assert.Equal(XLDataType.Boolean, sheet.Cell(4, 1).DataType);
        Assert.True(sheet.Cell(4, 1).GetBoolean());
    }

    [Fact]
    public async Task SuccessfulOutputUsesFinalPathAndLeavesNoStagingFile()
    {
        using var workspace = CreateSingleColumnWorkbook("  A  ");

        var result = await ExecuteAsync(workspace);

        AssertSuccess(result);
        Assert.Equal(Path.GetFullPath(workspace.OutputPath), result.OutputFilePath);
        Assert.True(File.Exists(workspace.OutputPath));
        workspace.AssertNoStagingFiles();
    }

    [Fact]
    public async Task OutputPathEqualToInput_IsAlwaysRejected()
    {
        using var workspace = CreateSingleColumnWorkbook("A");
        var request = CreateRequest(workspace, outputPath: workspace.InputPath.ToUpperInvariant(), overwrite: true);

        var result = await _service.ExecuteAsync(request);

        AssertFailure(result, OperationErrorCode.OutputConflictsWithInput);
        Assert.False(File.Exists(workspace.OutputPath));
    }

    [Fact]
    public async Task ExistingOutputWithoutOverwrite_IsRejectedAndPreserved()
    {
        using var workspace = CreateSingleColumnWorkbook("A");
        await File.WriteAllTextAsync(workspace.OutputPath, "existing output");
        var beforeHash = ComputeSha256(workspace.OutputPath);

        var result = await ExecuteAsync(workspace, overwrite: false);

        AssertFailure(result, OperationErrorCode.OutputAlreadyExists);
        Assert.Equal(beforeHash, ComputeSha256(workspace.OutputPath));
        workspace.AssertNoStagingFiles();
    }

    [Fact]
    public async Task ExistingOutputWithOverwrite_IsReplacedAfterSuccessfulStaging()
    {
        using var workspace = CreateSingleColumnWorkbook("  A  ");
        await File.WriteAllTextAsync(workspace.OutputPath, "existing output");
        var beforeHash = ComputeSha256(workspace.OutputPath);

        var result = await ExecuteAsync(workspace, overwrite: true);

        AssertSuccess(result);
        Assert.NotEqual(beforeHash, ComputeSha256(workspace.OutputPath));
        Assert.Equal("A", ReadOutputText(workspace));
        workspace.AssertNoStagingFiles();
    }

    [Fact]
    public async Task MissingOutputDirectory_ReturnsOutputDirectoryNotWritable()
    {
        using var workspace = CreateSingleColumnWorkbook("A");
        var outputPath = Path.Combine(workspace.DirectoryPath, "missing", "output.xlsx");

        var result = await _service.ExecuteAsync(CreateRequest(workspace, outputPath: outputPath));

        AssertFailure(result, OperationErrorCode.OutputDirectoryNotWritable);
    }

    [Fact]
    public async Task InputExclusiveLock_ReturnsFileLockedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = CreateSingleColumnWorkbook("A");
        using var inputLock = new FileStream(
            workspace.InputPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = await ExecuteAsync(workspace);

        AssertFailure(result, OperationErrorCode.FileLocked);
        workspace.AssertNoStagingFiles();
    }

    [Fact]
    public async Task OutputExclusiveLock_ReturnsFileLockedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = CreateSingleColumnWorkbook("A");
        await File.WriteAllTextAsync(workspace.OutputPath, "existing output");
        using var outputLock = new FileStream(
            workspace.OutputPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = await ExecuteAsync(workspace, overwrite: true);

        AssertFailure(result, OperationErrorCode.FileLocked);
        workspace.AssertNoStagingFiles();
    }

    [Fact]
    public async Task MissingInput_ReturnsFileNotFound()
    {
        using var workspace = TestWorkspace.Empty();

        var result = await ExecuteAsync(workspace);

        AssertFailure(result, OperationErrorCode.FileNotFound);
    }

    [Fact]
    public async Task NonXlsxInputOrOutput_ReturnsUnsupportedFileType()
    {
        using var inputWorkspace = TestWorkspace.Empty(inputFileName: "input.csv");
        await File.WriteAllTextAsync(inputWorkspace.InputPath, "value");
        using var outputWorkspace = CreateSingleColumnWorkbook("A");

        var inputResult = await ExecuteAsync(inputWorkspace);
        var outputResult = await _service.ExecuteAsync(CreateRequest(
            outputWorkspace,
            outputPath: Path.Combine(outputWorkspace.DirectoryPath, "output.csv")));

        AssertFailure(inputResult, OperationErrorCode.UnsupportedFileType);
        AssertFailure(outputResult, OperationErrorCode.UnsupportedFileType);
    }

    [Fact]
    public async Task CorruptXlsx_ReturnsWorkbookUnreadableAndCleansStaging()
    {
        using var workspace = TestWorkspace.Empty();
        await File.WriteAllTextAsync(workspace.InputPath, "not a workbook");

        var result = await ExecuteAsync(workspace);

        AssertFailure(result, OperationErrorCode.WorkbookUnreadable);
        workspace.AssertNoStagingFiles();
    }

    [Fact]
    public async Task MissingWorksheet_ReturnsWorksheetNotFoundAndCleansStaging()
    {
        using var workspace = CreateSingleColumnWorkbook("A");

        var result = await _service.ExecuteAsync(CreateRequest(workspace, worksheetName: "data"));

        AssertFailure(result, OperationErrorCode.WorksheetNotFound);
        workspace.AssertNoStagingFiles();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10)]
    public async Task InvalidHeaderRow_ReturnsInvalidHeaderRow(int headerRowNumber)
    {
        using var workspace = CreateSingleColumnWorkbook("A");

        var result = await ExecuteAsync(workspace, headerRowNumber: headerRowNumber);

        AssertFailure(result, OperationErrorCode.InvalidHeaderRow);
        workspace.AssertNoStagingFiles();
    }

    [Fact]
    public async Task HeaderWithoutData_IsValidAndReportsZeroProcessedRows()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "表头";
        });

        var result = await ExecuteAsync(workspace);

        AssertSuccess(result);
        Assert.Equal(0, result.Summary!.ProcessedDataRowCount);
    }

    [Fact]
    public async Task SuccessProgress_UsesApprovedStagesInOrderAndCompletesAtOneHundredPercent()
    {
        using var workspace = TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "值";
            sheet.Cell(2, 1).Value = " A ";
            sheet.Cell(4, 1).Value = " B ";
        });
        var progress = new RecordingProgress();

        var result = await _service.ExecuteAsync(CreateRequest(workspace), progress);

        AssertSuccess(result);
        var reports = progress.Snapshot();
        Assert.Equal(
            [OperationStage.Reading, OperationStage.Preparing, OperationStage.Processing, OperationStage.Writing, OperationStage.Completed],
            reports.Select(report => report.Stage).Distinct());
        var completed = Assert.Single(reports, report => report.Stage == OperationStage.Completed);
        Assert.Equal(100, completed.Percent);
        Assert.Equal(2, completed.ProcessedRows);
        Assert.Equal(2, completed.TotalRows);
    }

    [Fact]
    public async Task PreCancelledToken_PropagatesOperationCanceledException()
    {
        using var workspace = CreateSingleColumnWorkbook("A");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ExecuteAsync(CreateRequest(workspace), cancellationToken: cancellation.Token));
        Assert.False(File.Exists(workspace.OutputPath));
    }

    [Fact]
    public async Task NullOrBlankRequiredConfiguration_ReturnsInvalidConfiguration()
    {
        using var workspace = CreateSingleColumnWorkbook("A");

        var nullRequest = await _service.ExecuteAsync(null!);
        var nullSource = await _service.ExecuteAsync(new FormatStandardizationRequest(
            null!, workspace.OutputPath, new FormatStandardizationOptions(), false));
        var nullOptions = await _service.ExecuteAsync(new FormatStandardizationRequest(
            new WorksheetSource(workspace.InputPath, "Data", 1), workspace.OutputPath, null!, false));
        var blankOutput = await _service.ExecuteAsync(new FormatStandardizationRequest(
            new WorksheetSource(workspace.InputPath, "Data", 1), " ", new FormatStandardizationOptions(), false));

        AssertFailure(nullRequest, OperationErrorCode.InvalidConfiguration);
        AssertFailure(nullSource, OperationErrorCode.InvalidConfiguration);
        AssertFailure(nullOptions, OperationErrorCode.InvalidConfiguration);
        AssertFailure(blankOutput, OperationErrorCode.InvalidConfiguration);
    }

    private Task<FormatStandardizationResult> ExecuteAsync(
        TestWorkspace workspace,
        FormatStandardizationOptions? options = null,
        int headerRowNumber = 1,
        bool overwrite = false) =>
        _service.ExecuteAsync(CreateRequest(
            workspace,
            options,
            headerRowNumber: headerRowNumber,
            overwrite: overwrite));

    private static FormatStandardizationRequest CreateRequest(
        TestWorkspace workspace,
        FormatStandardizationOptions? options = null,
        string worksheetName = "Data",
        int headerRowNumber = 1,
        string? outputPath = null,
        bool overwrite = false) =>
        new(
            new WorksheetSource(workspace.InputPath, worksheetName, headerRowNumber),
            outputPath ?? workspace.OutputPath,
            options ?? new FormatStandardizationOptions(),
            overwrite);

    private static TestWorkspace CreateSingleColumnWorkbook(string value) =>
        TestWorkspace.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "值";
            sheet.Cell(2, 1).Value = value;
        });

    private static string ReadOutputText(TestWorkspace workspace)
    {
        using var output = new XLWorkbook(workspace.OutputPath);
        return output.Worksheet("Data").Cell(2, 1).GetString();
    }

    private static void AssertDateCell(IXLCell cell, DateTime expected, string expectedFormat)
    {
        Assert.Equal(XLDataType.DateTime, cell.DataType);
        Assert.Equal(expected, cell.GetDateTime());
        Assert.Equal(expectedFormat, cell.Style.NumberFormat.Format);
    }

    private static void AssertSuccess(FormatStandardizationResult result)
    {
        Assert.True(
            result.Success,
            $"{result.Error?.Code}: {result.Error?.Message} ({result.Error?.Detail})");
        Assert.NotNull(result.OutputFilePath);
        Assert.NotNull(result.Summary);
        Assert.Null(result.Error);
    }

    private static void AssertFailure(
        FormatStandardizationResult result,
        OperationErrorCode expectedCode)
    {
        Assert.False(result.Success);
        Assert.Null(result.OutputFilePath);
        Assert.Null(result.Summary);
        Assert.NotNull(result.Error);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class RecordingProgress : IProgress<OperationProgress>
    {
        private readonly List<OperationProgress> _reports = [];

        public void Report(OperationProgress value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
        }

        public IReadOnlyList<OperationProgress> Snapshot()
        {
            lock (_reports)
            {
                return [.. _reports];
            }
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string directoryPath, string inputFileName)
        {
            DirectoryPath = directoryPath;
            InputPath = Path.Combine(directoryPath, inputFileName);
            OutputPath = Path.Combine(directoryPath, "output.xlsx");
        }

        public string DirectoryPath { get; }

        public string InputPath { get; }

        public string OutputPath { get; }

        public static TestWorkspace Empty(string inputFileName = "input.xlsx")
        {
            var directoryPath = Path.Combine(
                Path.GetTempPath(),
                "TabularStudio.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);
            return new TestWorkspace(directoryPath, inputFileName);
        }

        public static TestWorkspace Create(Action<XLWorkbook> configure)
        {
            var workspace = Empty();

            try
            {
                using var workbook = new XLWorkbook();
                configure(workbook);
                workbook.SaveAs(workspace.InputPath);
                return workspace;
            }
            catch
            {
                workspace.Dispose();
                throw;
            }
        }

        public void AssertNoStagingFiles()
        {
            Assert.Empty(Directory.EnumerateFiles(DirectoryPath, "*.staging.xlsx"));
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
