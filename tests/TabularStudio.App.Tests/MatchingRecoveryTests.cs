using System.IO;
using ClosedXML.Excel;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class MatchingRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TabularStudioTests", Guid.NewGuid().ToString("N"));
    private ExistingOutputChoice _choice = ExistingOutputChoice.Overwrite;
    private int _confirmations;

    private async Task<DataMatchingViewModel> CreateAsync()
    {
        Directory.CreateDirectory(_directory);
        foreach (string name in new[] { "master", "reference" })
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "Key";
            sheet.Cell(1, 2).Value = "Value";
            sheet.Cell(2, 1).Value = "001";
            sheet.Cell(2, 2).Value = "returned";
            workbook.SaveAs(Path.Combine(_directory, name + ".xlsx"));
        }

        var vm = new DataMatchingViewModel(new WorkbookInspectionService(), new DataMatchingService(),
            confirmExistingOutput: _ => { _confirmations++; return _choice; },
            showSaveFileDialog: _ => Path.Combine(_directory, "save-as.xlsx"),
            outputDirectoryPreferenceService: new OutputDirectoryPreferenceService(Path.Combine(_directory, "preferences.json")));
        await vm.LoadMasterFileAsync(Path.Combine(_directory, "master.xlsx"));
        await vm.LoadReferenceFileAsync(Path.Combine(_directory, "reference.xlsx"));
        vm.Conditions[0].SelectedMasterColumn = vm.MasterAvailableColumns[0];
        vm.Conditions[0].SelectedReferenceColumn = vm.ReferenceAvailableColumns[0];
        vm.ReturnFields[1].IsSelected = true;
        vm.OutputFilePath = Path.Combine(_directory, "result.xlsx");
        Assert.True(vm.CanStart);
        return vm;
    }

    [Fact]
    public async Task LockedOutputCanBeRetriedWithoutChangingConfiguration()
    {
        var vm = await CreateAsync();
        await vm.StartAsync();
        Assert.True(vm.HasSuccess);
        Assert.True(vm.CanStart);
        var original = File.ReadAllBytes(vm.OutputFilePath!);
        using (var locked = new FileStream(vm.OutputFilePath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await vm.StartAsync();
                Assert.Equal("文件被占用", vm.ErrorTitle);
                Assert.False(vm.IsProcessing);
                Assert.True(vm.CanStart);
                Assert.True(vm.CanConfigure);
                Assert.False(vm.CanExecuteSuccessActions);
            }
        }
        Assert.Equal(original, File.ReadAllBytes(vm.OutputFilePath!));
        await vm.StartAsync();
        Assert.True(vm.HasSuccess);
        Assert.False(vm.HasError);
        Assert.True(vm.CanStart);
        Assert.True(_confirmations > 0);
        using var result = new XLWorkbook(vm.OutputFilePath!);
        Assert.Equal("returned", result.Worksheet(1).Cell(2, 3).GetString());
    }

    [Fact]
    public async Task SuccessStillRequiresOverwriteConfirmationAndCancelAllowsRetry()
    {
        var vm = await CreateAsync();
        await vm.StartAsync();
        var original = File.ReadAllBytes(vm.OutputFilePath!);
        _choice = ExistingOutputChoice.Cancel;
        await vm.StartAsync();
        Assert.Equal(1, _confirmations);
        Assert.Equal(original, File.ReadAllBytes(vm.OutputFilePath!));
        Assert.True(vm.CanStart);
        Assert.False(vm.IsProcessing);
        _choice = ExistingOutputChoice.SaveAs;
        await vm.StartAsync();
        Assert.True(vm.HasSuccess);
        Assert.True(File.Exists(Path.Combine(_directory, "save-as.xlsx")));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_directory, "result.xlsx")));
    }

    [Fact]
    public async Task InvalidConfigurationAndLoadingStillBlockStart()
    {
        var vm = await CreateAsync();
        await vm.StartAsync();
        vm.IsProcessing = true;
        Assert.False(vm.CanStart);
        Assert.False(vm.CanConfigure);
        vm.IsProcessing = false;
        vm.IsMasterPreviewLoading = true;
        Assert.False(vm.CanStart);
        vm.IsMasterPreviewLoading = false;
        vm.IsReferencePreviewLoading = true;
        Assert.False(vm.CanStart);
        vm.IsReferencePreviewLoading = false;
        vm.OutputFilePath = vm.MasterFilePath;
        Assert.False(vm.CanStart);
        vm.OutputFilePath = Path.Combine(_directory, "other.xlsx");
        vm.ReturnFields[1].IsSelected = false;
        Assert.False(vm.CanStart);
        vm.ReturnFields[1].IsSelected = true;
        vm.Conditions[0].SelectedMasterColumn = null;
        Assert.False(vm.CanStart);
        await vm.LoadMasterFileAsync(Path.Combine(_directory, "missing.xlsx"));
        Assert.False(vm.CanStart);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task FilterConfigurationFlowsToCoreAndResetOnMasterChange()
    {
        var vm = await CreateAsync();
        Assert.False(vm.IsMasterFilterEnabled);
        vm.IsMasterFilterEnabled = true;
        Assert.False(vm.CanStart);
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0];
        vm.MasterFilterValue = "001";
        Assert.True(vm.CanStart);
        await vm.StartAsync();
        Assert.True(vm.HasSuccess);
        Assert.Equal(1, vm.ResultMatchedCount);
        Assert.Equal(0, vm.ResultSkippedCount);
        vm.MasterFilterValue = "no";
        await vm.StartAsync();
        Assert.True(vm.HasSuccess);
        Assert.Equal(0, vm.ResultMatchedCount);
        Assert.Equal(1, vm.ResultSkippedCount);
        vm.MasterFilterValue = "";
        Assert.False(vm.CanStart);
        vm.IsMasterFilterEnabled = false;
        Assert.True(vm.CanStart);
        await vm.StartAsync();
        Assert.Equal(1, vm.ResultMatchedCount);
        vm.IsMasterFilterEnabled = true;
        await vm.LoadMasterFileAsync(vm.MasterFilePath!);
        Assert.Null(vm.SelectedMasterFilterColumn);
        Assert.False(vm.CanStart);
    }
}
