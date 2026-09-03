using System.Reflection;
using TabularStudio.Core.Contracts;

namespace TabularStudio.Tests;

public sealed class ContractBaselineTests
{
    private static readonly string[] ExpectedFormatOptionNames =
    [
        nameof(FormatStandardizationOptions.TrimOuterWhitespace),
        nameof(FormatStandardizationOptions.RemoveTabsNewLinesAndHiddenCharacters),
        nameof(FormatStandardizationOptions.NormalizeFullWidthHalfWidth),
        nameof(FormatStandardizationOptions.NormalizeUnicode),
        nameof(FormatStandardizationOptions.NormalizeSafeNumbers),
        nameof(FormatStandardizationOptions.NormalizeUnambiguousDates)
    ];

    private static readonly string[] ExpectedErrorCodes =
    [
        nameof(OperationErrorCode.FileNotFound),
        nameof(OperationErrorCode.UnsupportedFileType),
        nameof(OperationErrorCode.FileLocked),
        nameof(OperationErrorCode.WorkbookUnreadable),
        nameof(OperationErrorCode.WorksheetNotFound),
        nameof(OperationErrorCode.InvalidHeaderRow),
        nameof(OperationErrorCode.ColumnNotFound),
        nameof(OperationErrorCode.InvalidConfiguration),
        nameof(OperationErrorCode.OutputConflictsWithInput),
        nameof(OperationErrorCode.OutputAlreadyExists),
        nameof(OperationErrorCode.OutputDirectoryNotWritable),
        nameof(OperationErrorCode.FormulaCellNotAllowedForMatching),
        nameof(OperationErrorCode.IncompleteOutputCleanupFailed),
        nameof(OperationErrorCode.ProcessingFailed)
    ];

    [Fact]
    public void MatchingStatusColumnOptions_UsesApprovedDefaults()
    {
        var options = new MatchingStatusColumnOptions();

        Assert.True(options.Enabled);
        Assert.Equal("匹配状态", options.ColumnName);
    }

    [Fact]
    public void FormatStandardizationOptions_ExposesOnlySixApprovedBooleanOptions()
    {
        var properties = typeof(FormatStandardizationOptions).GetProperties();

        Assert.Equal(ExpectedFormatOptionNames, properties.Select(property => property.Name));
        Assert.All(properties, property => Assert.Equal(typeof(bool), property.PropertyType));

        var defaults = new FormatStandardizationOptions();
        Assert.All(properties, property => Assert.True((bool)property.GetValue(defaults)!));
        Assert.Null(typeof(FormatStandardizationOptions).GetProperty("PreserveLeadingZeroIdentifiers"));
        Assert.Null(typeof(FormatStandardizationOptions).GetProperty("PreserveLongNumericText"));
    }

    [Fact]
    public void ColumnReference_RepresentsEmptyAndDuplicateHeadersByPhysicalColumnNumber()
    {
        var emptyHeader = new ColumnReference(1, null);
        var firstDuplicate = new ColumnReference(2, "科室");
        var secondDuplicate = new ColumnReference(3, "科室");

        Assert.Equal(1, emptyHeader.ColumnNumber);
        Assert.Null(emptyHeader.HeaderText);
        Assert.Equal(firstDuplicate.HeaderText, secondDuplicate.HeaderText);
        Assert.NotEqual(firstDuplicate.ColumnNumber, secondDuplicate.ColumnNumber);
    }

    [Fact]
    public void MatchingAndPreviewContracts_UseColumnReference()
    {
        Assert.Equal(
            typeof(ColumnReference),
            typeof(MatchingCondition).GetProperty(nameof(MatchingCondition.MasterColumn))!.PropertyType);
        Assert.Equal(
            typeof(ColumnReference),
            typeof(MatchingCondition).GetProperty(nameof(MatchingCondition.ReferenceColumn))!.PropertyType);
        Assert.Equal(
            typeof(IReadOnlyList<ColumnReference>),
            typeof(DataMatchingRequest).GetProperty(nameof(DataMatchingRequest.ReturnFields))!.PropertyType);
        Assert.Equal(
            typeof(IReadOnlyList<ColumnReference>),
            typeof(PreviewTable).GetProperty(nameof(PreviewTable.Columns))!.PropertyType);
        Assert.Null(typeof(PreviewTable).Assembly.GetType("TabularStudio.Core.Contracts.PreviewColumn"));
    }

    [Fact]
    public void OperationEnums_ContainExactlyApprovedMembers()
    {
        Assert.Equal(
            ["Reading", "Preparing", "Processing", "Writing", "Completed"],
            Enum.GetNames<OperationStage>());
        Assert.Equal(ExpectedErrorCodes, Enum.GetNames<OperationErrorCode>());
    }

    [Fact]
    public void ApprovedRequestsResultsAndSummaries_CanBeConstructed()
    {
        var source = new WorksheetSource("input.xlsx", "Sheet1", 1);
        var column = new ColumnReference(1, "编号");
        var condition = new MatchingCondition(column, column);
        var error = new OperationError(OperationErrorCode.ProcessingFailed, "处理失败");
        var preview = new PreviewTable(
            "Sheet1",
            1,
            [column],
            [new PreviewRow(2, [new PreviewCell(1, "001")])]);
        var formatRequest = new FormatStandardizationRequest(
            source,
            "format.xlsx",
            new FormatStandardizationOptions(),
            false);
        var formatSummary = new FormatStandardizationSummary("Sheet1", 1, TimeSpan.Zero);
        var matchRequest = new DataMatchingRequest(
            source,
            source,
            [condition],
            [column],
            true,
            new MatchingStatusColumnOptions(),
            "match.xlsx",
            false);
        var matchSummary = new DataMatchingSummary(1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.NotNull(new WorkbookInspectionRequest("input.xlsx"));
        Assert.NotNull(new WorksheetInfo("Sheet1"));
        Assert.NotNull(new WorkbookInspectionResult(true, [new WorksheetInfo("Sheet1")], null));
        Assert.NotNull(new WorksheetPreviewRequest(source));
        Assert.NotNull(new WorksheetPreviewResult(true, preview, null));
        Assert.NotNull(formatRequest);
        Assert.NotNull(new FormatStandardizationResult(true, "format.xlsx", formatSummary, null));
        Assert.NotNull(matchRequest);
        Assert.NotNull(new ReturnedFieldMapping(column, "编号_匹配"));
        Assert.NotNull(new DataMatchingResult(
            true,
            "match.xlsx",
            matchSummary,
            [new ReturnedFieldMapping(column, "编号_匹配")],
            "匹配状态",
            null));
        Assert.NotNull(new OperationProgress(OperationStage.Completed, 100, 1, 1));
        Assert.Equal(OperationErrorCode.ProcessingFailed, error.Code);
    }

    [Fact]
    public void ServiceInterfaces_UseApprovedAsyncSignatures()
    {
        AssertMethod(
            typeof(IWorkbookInspectionService),
            nameof(IWorkbookInspectionService.InspectAsync),
            typeof(Task<WorkbookInspectionResult>),
            typeof(WorkbookInspectionRequest),
            typeof(CancellationToken));
        AssertMethod(
            typeof(IWorkbookInspectionService),
            nameof(IWorkbookInspectionService.GetPreviewAsync),
            typeof(Task<WorksheetPreviewResult>),
            typeof(WorksheetPreviewRequest),
            typeof(CancellationToken));
        AssertMethod(
            typeof(IFormatStandardizationService),
            nameof(IFormatStandardizationService.ExecuteAsync),
            typeof(Task<FormatStandardizationResult>),
            typeof(FormatStandardizationRequest),
            typeof(IProgress<OperationProgress>),
            typeof(CancellationToken));
        AssertMethod(
            typeof(IDataMatchingService),
            nameof(IDataMatchingService.ExecuteAsync),
            typeof(Task<DataMatchingResult>),
            typeof(DataMatchingRequest),
            typeof(IProgress<OperationProgress>),
            typeof(CancellationToken));
    }

    private static void AssertMethod(
        Type serviceType,
        string methodName,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = serviceType.GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
        Assert.Equal(parameterTypes, method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(method.GetParameters().Skip(1), parameter => Assert.True(parameter.HasDefaultValue));
    }
}
