using System.Security.Cryptography;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class WorkbookInspectionServiceTests
{
    private readonly WorkbookInspectionService _service = new();

    [Fact]
    public async Task InspectAsync_ReturnsAllWorksheetsInWorkbookOrder()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            workbook.AddWorksheet("第一表");
            workbook.AddWorksheet("第二表");
            workbook.AddWorksheet("第三表");
        });

        var result = await _service.InspectAsync(new WorkbookInspectionRequest(file.Path));

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(["第一表", "第二表", "第三表"], result.Worksheets.Select(sheet => sheet.Name));
    }

    [Fact]
    public async Task InspectAsync_ReturnsFileNotFoundForMissingFile()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "missing.xlsx");

        var result = await _service.InspectAsync(new WorkbookInspectionRequest(path));

        AssertFailure(result, OperationErrorCode.FileNotFound);
    }

    [Fact]
    public async Task InspectAsync_ReturnsUnsupportedFileTypeForExistingNonXlsxFile()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "input.ods");
        await File.WriteAllTextAsync(path, "编号,姓名");

        var result = await _service.InspectAsync(new WorkbookInspectionRequest(path));

        AssertFailure(result, OperationErrorCode.UnsupportedFileType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InspectAsync_ReturnsInvalidConfigurationForBlankPath(string? filePath)
    {
        var result = await _service.InspectAsync(new WorkbookInspectionRequest(filePath!));

        AssertFailure(result, OperationErrorCode.InvalidConfiguration);
    }

    [Fact]
    public async Task InspectAsync_ReturnsWorkbookUnreadableForCorruptXlsx()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "corrupt.xlsx");
        await File.WriteAllTextAsync(path, "not an Excel workbook");

        var result = await _service.InspectAsync(new WorkbookInspectionRequest(path));

        AssertFailure(result, OperationErrorCode.WorkbookUnreadable);
    }

    [Fact]
    public async Task InspectAsync_ReturnsFileLockedForExclusiveFileLockOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var file = SyntheticWorkbook.Create(workbook => workbook.AddWorksheet("Sheet1"));
        using var exclusiveLock = new FileStream(
            file.Path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = await _service.InspectAsync(new WorkbookInspectionRequest(file.Path));

        AssertFailure(result, OperationErrorCode.FileLocked);
    }

    [Fact]
    public async Task GetPreviewAsync_ReturnsWorksheetNotFoundWithoutFuzzyMatching()
    {
        using var file = CreateBasicWorkbook("Data");

        var result = await GetPreviewAsync(file.Path, "data", 1);

        AssertFailure(result, OperationErrorCode.WorksheetNotFound);
    }

    [Fact]
    public async Task GetPreviewAsync_UsesTheSameFileValidationAsInspection()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = System.IO.Path.Combine(directory.Path, "missing.xlsx");
        var unsupportedPath = System.IO.Path.Combine(directory.Path, "input.ods");
        await File.WriteAllTextAsync(unsupportedPath, "编号,姓名");

        var missingResult = await GetPreviewAsync(missingPath, "Sheet1", 1);
        var unsupportedResult = await GetPreviewAsync(unsupportedPath, "Sheet1", 1);

        AssertFailure(missingResult, OperationErrorCode.FileNotFound);
        AssertFailure(unsupportedResult, OperationErrorCode.UnsupportedFileType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetPreviewAsync_ReturnsInvalidHeaderRowWhenLessThanOne(int headerRowNumber)
    {
        using var file = CreateBasicWorkbook();

        var result = await GetPreviewAsync(file.Path, "Sheet1", headerRowNumber);

        AssertFailure(result, OperationErrorCode.InvalidHeaderRow);
    }

    [Fact]
    public async Task GetPreviewAsync_ReturnsInvalidHeaderRowWhenBeyondContent()
    {
        using var file = CreateBasicWorkbook();

        var result = await GetPreviewAsync(file.Path, "Sheet1", 10);

        AssertFailure(result, OperationErrorCode.InvalidHeaderRow);
    }

    [Fact]
    public async Task GetPreviewAsync_ReturnsInvalidHeaderRowForEmptyWorksheet()
    {
        using var file = SyntheticWorkbook.Create(workbook => workbook.AddWorksheet("Sheet1"));

        var result = await GetPreviewAsync(file.Path, "Sheet1", 1);

        AssertFailure(result, OperationErrorCode.InvalidHeaderRow);
    }

    [Fact]
    public async Task GetPreviewAsync_PreservesPhysicalColumnsDuplicateAndEmptyHeaders()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(3, 2).Value = "科室";
            sheet.Cell(3, 4).Value = "科室";
            sheet.Cell(4, 2).Value = "科室A";
            sheet.Cell(4, 4).Value = "科室B";
        });

        var result = await GetPreviewAsync(file.Path, "Sheet1", 3);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Preview);
        Assert.Equal(3, result.Preview.HeaderRowNumber);
        Assert.Equal(
            [new ColumnReference(2, "科室"), new ColumnReference(3, null), new ColumnReference(4, "科室")],
            result.Preview.Columns);
        Assert.Equal([2, 3, 4], result.Preview.Rows.Single().Cells.Select(cell => cell.ColumnNumber));
        Assert.Null(result.Preview.Rows.Single().Cells[1].DisplayValue);
    }

    [Fact]
    public async Task GetPreviewAsync_IgnoresEarlierTitlesAndStyleOnlyCellsWhenFindingColumnRange()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(1, 20).Value = "工作簿标题";
            sheet.Cell(3, 2).Value = "编号";
            sheet.Cell(3, 4).Value = "姓名";
            sheet.Cell(4, 2).Value = "A001";
            sheet.Cell(4, 4).Value = "测试甲";
            sheet.Cell(10, 30).Style.Fill.BackgroundColor = XLColor.Yellow;
        });

        var result = await GetPreviewAsync(file.Path, "Sheet1", 3);

        Assert.True(result.Success);
        Assert.NotNull(result.Preview);
        Assert.Equal([2, 3, 4], result.Preview.Columns.Select(column => column.ColumnNumber));
        Assert.Single(result.Preview.Rows);
        Assert.Equal(4, result.Preview.Rows.Single().WorksheetRowNumber);
    }

    [Fact]
    public async Task GetPreviewAsync_ReturnsEmptyRowsWhenOnlyHeaderHasContent()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(1, 2).Value = "编号";
            sheet.Cell(1, 3).Value = "姓名";
        });

        var result = await GetPreviewAsync(file.Path, "Sheet1", 1);

        Assert.True(result.Success);
        Assert.NotNull(result.Preview);
        Assert.Equal([2, 3], result.Preview.Columns.Select(column => column.ColumnNumber));
        Assert.Empty(result.Preview.Rows);
    }

    [Fact]
    public async Task GetPreviewAsync_UsesDataColumnsWhenHeaderRowIsEmptyButDataExistsBelow()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(5, 3).Value = "A001";
            sheet.Cell(5, 5).Value = "测试甲";
        });

        var result = await GetPreviewAsync(file.Path, "Sheet1", 4);

        Assert.True(result.Success);
        Assert.NotNull(result.Preview);
        Assert.Equal([3, 4, 5], result.Preview.Columns.Select(column => column.ColumnNumber));
        Assert.All(result.Preview.Columns, column => Assert.Null(column.HeaderText));
        Assert.Equal(5, result.Preview.Rows.Single().WorksheetRowNumber);
    }

    [Fact]
    public async Task GetPreviewAsync_ReturnsAtMostTwentyConsecutivePhysicalRows()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(2, 2).Value = "编号";
            for (var rowNumber = 3; rowNumber <= 27; rowNumber++)
            {
                sheet.Cell(rowNumber, 2).Value = $"A{rowNumber:000}";
            }

            sheet.Row(8).Clear(XLClearOptions.Contents);
        });

        var result = await GetPreviewAsync(file.Path, "Sheet1", 2);

        Assert.True(result.Success);
        Assert.NotNull(result.Preview);
        Assert.Equal(20, result.Preview.Rows.Count);
        Assert.Equal(Enumerable.Range(3, 20), result.Preview.Rows.Select(row => row.WorksheetRowNumber));
        Assert.Null(result.Preview.Rows.Single(row => row.WorksheetRowNumber == 8).Cells.Single().DisplayValue);
        Assert.DoesNotContain(result.Preview.Rows, row => row.WorksheetRowNumber == 23);
    }

    [Fact]
    public async Task GetPreviewAsync_StopsAtLastContentRowWhenThereAreFewerThanTwentyRows()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(4, 2).Value = "编号";
            sheet.Cell(5, 2).Value = "A001";
            sheet.Cell(7, 2).Value = "A003";
        });

        var result = await GetPreviewAsync(file.Path, "Sheet1", 4);

        Assert.True(result.Success);
        Assert.NotNull(result.Preview);
        Assert.Equal([5, 6, 7], result.Preview.Rows.Select(row => row.WorksheetRowNumber));
        Assert.Null(result.Preview.Rows[1].Cells.Single().DisplayValue);
    }

    [Fact]
    public async Task GetPreviewAsync_AlignsEveryCellWithColumnsAndUsesFormattedDisplayValues()
    {
        using var file = SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("数据");
            sheet.Cell(2, 2).Value = "编号";
            sheet.Cell(2, 3).Value = "姓名";
            sheet.Cell(2, 4).Value = "金额";
            sheet.Cell(3, 2).Value = "A001";
            sheet.Cell(3, 4).Value = 100;
            sheet.Cell(3, 4).Style.NumberFormat.Format = "0.00";
            sheet.Cell(4, 2).Value = "A002";
            sheet.Cell(4, 3).Value = "测试乙";
            sheet.Cell(4, 4).Value = 25.5;
            sheet.Cell(4, 4).Style.NumberFormat.Format = "0.00";
        });

        var result = await GetPreviewAsync(file.Path, "数据", 2);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Preview);
        Assert.Equal("数据", result.Preview.WorksheetName);
        Assert.Equal([2, 3, 4], result.Preview.Columns.Select(column => column.ColumnNumber));

        foreach (var row in result.Preview.Rows)
        {
            Assert.Equal(result.Preview.Columns.Count, row.Cells.Count);
            Assert.Equal(
                result.Preview.Columns.Select(column => column.ColumnNumber),
                row.Cells.Select(cell => cell.ColumnNumber));
        }

        Assert.Equal(3, result.Preview.Rows[0].WorksheetRowNumber);
        Assert.Null(result.Preview.Rows[0].Cells[1].DisplayValue);
        Assert.Equal("100.00", result.Preview.Rows[0].Cells[2].DisplayValue);
        Assert.Equal(4, result.Preview.Rows[1].WorksheetRowNumber);
    }

    [Fact]
    public async Task InspectAndPreview_DoNotModifyInputFile()
    {
        using var file = CreateBasicWorkbook();
        var originalHash = ComputeSha256(file.Path);

        var inspection = await _service.InspectAsync(new WorkbookInspectionRequest(file.Path));
        var hashAfterInspection = ComputeSha256(file.Path);
        var preview = await GetPreviewAsync(file.Path, "Sheet1", 1);
        var hashAfterPreview = ComputeSha256(file.Path);

        Assert.True(inspection.Success);
        Assert.True(preview.Success);
        Assert.Equal(originalHash, hashAfterInspection);
        Assert.Equal(originalHash, hashAfterPreview);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedInsteadOfConvertedToFailure()
    {
        using var file = CreateBasicWorkbook();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.InspectAsync(new WorkbookInspectionRequest(file.Path), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GetPreviewAsync(file.Path, "Sheet1", 1, cancellation.Token));
    }

    private Task<WorksheetPreviewResult> GetPreviewAsync(
        string filePath,
        string worksheetName,
        int headerRowNumber,
        CancellationToken cancellationToken = default) =>
        _service.GetPreviewAsync(
            new WorksheetPreviewRequest(new WorksheetSource(filePath, worksheetName, headerRowNumber)),
            cancellationToken);

    private static SyntheticWorkbook CreateBasicWorkbook(string worksheetName = "Sheet1") =>
        SyntheticWorkbook.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet(worksheetName);
            sheet.Cell(1, 1).Value = "编号";
            sheet.Cell(1, 2).Value = "姓名";
            sheet.Cell(2, 1).Value = "A001";
            sheet.Cell(2, 2).Value = "测试甲";
        });

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void AssertFailure(
        WorkbookInspectionResult result,
        OperationErrorCode expectedCode)
    {
        Assert.False(result.Success);
        Assert.Empty(result.Worksheets);
        Assert.NotNull(result.Error);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    private static void AssertFailure(
        WorksheetPreviewResult result,
        OperationErrorCode expectedCode)
    {
        Assert.False(result.Success);
        Assert.Null(result.Preview);
        Assert.NotNull(result.Error);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    private sealed class SyntheticWorkbook : IDisposable
    {
        private readonly TemporaryDirectory _directory;

        private SyntheticWorkbook(TemporaryDirectory directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        public string Path { get; }

        public static SyntheticWorkbook Create(Action<XLWorkbook> configure)
        {
            var directory = new TemporaryDirectory();
            var path = System.IO.Path.Combine(directory.Path, "synthetic.xlsx");

            try
            {
                using var workbook = new XLWorkbook();
                configure(workbook);
                workbook.SaveAs(path);
                return new SyntheticWorkbook(directory, path);
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        public void Dispose() => _directory.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TabularStudio.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
