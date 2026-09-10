using System.IO;
using System.Security.Cryptography;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class ProfileComparisonIntegrationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ProfileComparison-" + Guid.NewGuid().ToString("N"));

    public ProfileComparisonIntegrationTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xls")]
    [InlineData(".csv")]
    public async Task SavedProfilesAndStrictComparisonCoexistThroughTheMainWindow(string extension)
    {
        var left = WriteTable("left" + extension, " A ");
        var right = WriteTable("right" + extension, "A");
        var inputs = new[] { left, right }.ToDictionary(p => p, Hash);
        var store = new JsonProcessingProfileStore(Path.Combine(directory, "profiles"));
        Assert.True(store.Save(new(1, "日常配置", ProcessingProfileKind.FormatStandardization,
            new(true, false, false, false, false, false), null)).Success);
        Assert.True(store.Save(new(1, "日常配置", ProcessingProfileKind.DataMatching, null,
            new([new(new(1, "Code"), new(1, "Code"))], [new(2, "Name")], true,
                new(false, "匹配状态"), null))).Success);
        var profileFiles = Directory.GetFiles(Path.Combine(directory, "profiles"), "*.json", SearchOption.AllDirectories)
            .ToDictionary(p => p, Hash);
        var shell = CreateShell(store);
        var format = shell.FormatStandardizationVm;
        var matching = shell.DataMatchingVm;
        var comparison = shell.TableComparisonVm;
        Assert.False(format.Rules.TrimOuterWhitespace);
        Assert.Single(format.Profiles);
        Assert.Single(matching.Profiles);

        format.SelectedProfile = format.Profiles[0];
        format.ApplyProfile();
        Assert.True(format.Rules.TrimOuterWhitespace);
        await format.LoadFilesAsync([left]);
        await format.StartAsync();
        var formatted = Assert.Single(format.Files).ResultPath;
        Assert.NotNull(formatted);
        Assert.Equal(extension, Path.GetExtension(formatted));

        shell.SelectDataMatching();
        Assert.Same(matching, shell.CurrentViewViewModel);
        await matching.LoadMasterFileAsync(left);
        await matching.LoadReferenceFileAsync(right);
        matching.SelectedProfile = matching.Profiles[0];
        await matching.ApplyProfileAsync();
        Assert.Equal("已应用：日常配置", matching.ProfileStatusMessage);
        Assert.True(matching.NormalizeComparisonKeys);
        Assert.False(matching.HasSuccess);
        matching.OutputFilePath = Path.Combine(directory, "matched" + extension);
        await matching.StartAsync();
        Assert.True(matching.HasSuccess, matching.ErrorMessage);
        Assert.Equal(1, matching.ResultMatchedCount);

        shell.SelectTableComparison();
        Assert.Same(comparison, shell.CurrentViewViewModel);
        Assert.True(shell.IsTableComparisonSelected);
        Assert.False(shell.IsDataMatchingSelected);
        Assert.False(shell.IsFormatStandardizationSelected);
        await comparison.Left.LoadAsync(left);
        await comparison.Right.LoadAsync(right);
        await comparison.StartAsync();
        Assert.True(comparison.Result!.Success, comparison.ErrorMessage);
        Assert.False(comparison.Result.AreEqual);
        Assert.Equal("A2", Assert.Single(comparison.Differences).Address);
        var report = Path.Combine(directory, "differences.xlsx");
        await comparison.ExportToAsync(report);
        Assert.Equal(report, comparison.ReportPath);
        using (var book = new XLWorkbook(report))
            Assert.Equal("A2", book.Worksheet("差异明细1").Cell(2, 1).GetString());

        await comparison.Left.LoadAsync(formatted!);
        await comparison.StartAsync();
        Assert.True(comparison.Result!.AreEqual);
        Assert.Empty(comparison.Differences);
        shell.SelectFormatStandardization();
        Assert.Same(format, shell.CurrentViewViewModel);
        Assert.True(format.Rules.TrimOuterWhitespace);
        shell.SelectDataMatching();
        Assert.Equal("已应用：日常配置", matching.ProfileStatusMessage);
        Assert.Equal(1, matching.ResultMatchedCount);
        shell.SelectTableComparison();
        Assert.True(comparison.Result.AreEqual);

        var reopened = CreateShell(new JsonProcessingProfileStore(Path.Combine(directory, "profiles")));
        Assert.Single(reopened.FormatStandardizationVm.Profiles);
        Assert.Single(reopened.DataMatchingVm.Profiles);
        Assert.False(reopened.FormatStandardizationVm.Rules.TrimOuterWhitespace);
        Assert.False(reopened.DataMatchingVm.HasSuccess);
        Assert.Null(reopened.TableComparisonVm.Result);
        foreach (var input in inputs) Assert.Equal(input.Value, Hash(input.Key));
        foreach (var profile in profileFiles) Assert.Equal(profile.Value, Hash(profile.Key));
    }

    private MainWindowViewModel CreateShell(IProcessingProfileStore store)
    {
        var inspection = new WorkbookInspectionService();
        return new(inspection, new FormatStandardizationService(), new DataMatchingService(),
            new OutputDirectoryPreferenceService(Path.Combine(directory, "preferences.json")),
            comparisonService: new TableComparisonService(),
            profileStore: store, profileValidator: new ProcessingProfileValidator(inspection));
    }

    private string WriteTable(string name, string code)
    {
        var path = Path.Combine(directory, name);
        if (Path.GetExtension(path) == ".csv")
            File.WriteAllText(path, $"Code,Name\r\n{code},Alice\r\n");
        else if (Path.GetExtension(path) == ".xlsx")
        {
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = "Code";
            sheet.Cell(1, 2).Value = "Name";
            sheet.Cell(2, 1).Value = code;
            sheet.Cell(2, 2).Value = "Alice";
            book.SaveAs(path);
        }
        else
        {
            using var book = new HSSFWorkbook();
            var sheet = book.CreateSheet("Data");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("Code");
            header.CreateCell(1).SetCellValue("Name");
            var row = sheet.CreateRow(1);
            row.CreateCell(0).SetCellValue(code);
            row.CreateCell(1).SetCellValue("Alice");
            using var file = File.Create(path);
            book.Write(file, true);
        }
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
