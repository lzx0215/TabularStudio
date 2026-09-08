using System.IO;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class MultiFormatViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularStudio-UI31-" + Guid.NewGuid());
    public MultiFormatViewModelTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private OutputDirectoryPreferenceService Preferences() => new(Path.Combine(directory, "preferences.json"));
    internal static void Write(string path)
    {
        if (TabularFileTypes.IsCsv(path)) { File.WriteAllText(path, "Key,Value\r\n001,  yes  \r\n"); return; }
        if (Path.GetExtension(path) == ".xls")
        {
            using var book = new HSSFWorkbook(); var sheet = book.CreateSheet("Data");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("Key"); sheet.GetRow(0).CreateCell(1).SetCellValue("Value");
            sheet.CreateRow(1).CreateCell(0).SetCellValue("001"); sheet.GetRow(1).CreateCell(1).SetCellValue("  yes  ");
            using var stream = File.Create(path); book.Write(stream, true); return;
        }
        using var xlsx = new XLWorkbook(); var target = xlsx.AddWorksheet("Data");
        target.Cell(1, 1).Value = "Key"; target.Cell(1, 2).Value = "Value";
        target.Cell(2, 1).Value = "001"; target.Cell(2, 2).Value = "  yes  "; xlsx.SaveAs(path);
    }

    [Theory]
    [InlineData(".xlsx")][InlineData(".xls")][InlineData(".csv")]
    public async Task StandardizationLoadsPreviewsAndSavesOriginalFormat(string extension)
    {
        var path = Path.Combine(directory, "source" + extension); Write(path); var original = File.ReadAllBytes(path);
        var vm = new FormatStandardizationViewModel(new WorkbookInspectionService(), new FormatStandardizationService(), outputDirectoryPreferenceService: Preferences());
        await vm.LoadFileAsync(path);
        Assert.Equal(extension != ".csv", vm.HasWorksheetSelector);
        Assert.Equal("001", vm.PreviewDataTable!.Rows[0][0]);
        Assert.True(vm.CanStart);
        Assert.Equal(extension, Path.GetExtension(vm.OutputFilePath));
        vm.TrimOuterWhitespace = true;
        await vm.StartAsync();
        Assert.True(vm.HasSuccess, vm.ErrorMessage);
        Assert.Equal(original, File.ReadAllBytes(path));
        var preview = await new WorkbookInspectionService().GetPreviewAsync(new(new(vm.ResultOutputFilePath!, vm.SelectedWorksheet?.Name, 1)));
        Assert.Equal("yes", preview.Preview!.Rows[0].Cells[1].DisplayValue);
        vm.HeaderRowNumber = 2;
        await vm.RefreshPreviewAsync();
        Assert.Equal(0, vm.PreviewRowCount);
    }

    public static IEnumerable<object[]> Pairs() => from a in new[] { ".xlsx", ".xls", ".csv" } from b in new[] { ".xlsx", ".xls", ".csv" } select new object[] { a, b };

    [Theory, MemberData(nameof(Pairs))]
    public async Task MatchingUsesActualCoreWithNoCsvWorksheet(string a, string b)
    {
        var master = Path.Combine(directory, "master" + a); var reference = Path.Combine(directory, "reference" + b);
        Write(master); Write(reference);
        var vm = new DataMatchingViewModel(new WorkbookInspectionService(), new DataMatchingService(), outputDirectoryPreferenceService: Preferences());
        await vm.LoadMasterFileAsync(master); await vm.LoadReferenceFileAsync(reference);
        Assert.Equal(a != ".csv", vm.HasMasterWorksheetSelector);
        Assert.Equal(b != ".csv", vm.HasReferenceWorksheetSelector);
        vm.Conditions[0].SelectedMasterColumn = vm.MasterAvailableColumns[0];
        vm.Conditions[0].SelectedReferenceColumn = vm.ReferenceAvailableColumns[0];
        vm.ReturnFields[1].IsSelected = true;
        vm.IsMasterFilterEnabled = true; vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0]; vm.MasterFilterValue = "001";
        Assert.True(vm.CanStart);
        await vm.StartAsync(); Assert.True(vm.HasSuccess, vm.ErrorMessage); Assert.Equal(1, vm.ResultMatchedCount);
        Assert.Equal(a, Path.GetExtension(vm.ResultOutputFilePath));
        if (a == ".csv") { vm.UseSameFileAsMaster = true; Assert.False(vm.UseSameFileAsMaster); }
        await vm.LoadMasterFileAsync(master); Assert.Null(vm.SelectedMasterFilterColumn); Assert.False(vm.CanStart);
        await vm.LoadMasterFileAsync(Path.Combine(directory, "unsupported.ods")); Assert.True(vm.HasError); Assert.False(vm.CanStart);
    }
}
