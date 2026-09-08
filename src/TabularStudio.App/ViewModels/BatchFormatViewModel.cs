using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Services;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.ViewModels;

public sealed partial class FormatBatchFileViewModel : ObservableObject
{
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public FormatStandardizationViewModel Editor { get; }
    [ObservableProperty] private string _resultText = "待处理";
    [ObservableProperty] private string? _resultPath;
    public FormatBatchFileViewModel(string path, FormatStandardizationViewModel editor) { FilePath = path; Editor = editor; }
}

public sealed partial class BatchFormatViewModel : ObservableObject
{
    private readonly IWorkbookInspectionService inspection;
    private readonly IFormatStandardizationService single;
    private readonly IBatchFormatStandardizationService batch;
    private readonly IOutputDirectoryPreferenceService preferences;
    private readonly Func<string, ExistingOutputChoice>? confirm;
    private readonly Func<string, string?>? saveAs;
    private readonly Action<string>? sendToMatching;
    public ObservableCollection<FormatBatchFileViewModel> Files { get; } = [];
    public FormatStandardizationViewModel Rules { get; }
    [ObservableProperty] private FormatBatchFileViewModel? _selectedFile;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanUseSelectedResult))]
    private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _summary = "请选择一个或多个表格文件。";
    public bool CanConfigure => !IsBusy;
    public bool CanStart => !IsBusy && Files.Count > 0 && Files.All(f => !f.Editor.IsPreviewLoading);
    public bool CanUseSelectedResult => !IsBusy && SelectedFile?.ResultPath is not null;
    partial void OnSelectedFileChanged(FormatBatchFileViewModel? value) => OnPropertyChanged(nameof(CanUseSelectedResult));

    public BatchFormatViewModel(IWorkbookInspectionService inspection, IFormatStandardizationService single,
        IOutputDirectoryPreferenceService? preferences = null, IBatchFormatStandardizationService? batch = null,
        Func<string, ExistingOutputChoice>? confirm = null, Func<string, string?>? saveAs = null,
        Action<string>? sendToMatching = null)
    {
        this.inspection = inspection; this.single = single; this.preferences = preferences ?? new OutputDirectoryPreferenceService();
        this.batch = batch ?? new BatchFormatStandardizationService(single); this.confirm = confirm; this.saveAs = saveAs; this.sendToMatching = sendToMatching;
        Rules = new(inspection, single, outputDirectoryPreferenceService: this.preferences);
    }

    [RelayCommand]
    public async Task BrowseFilesAsync()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog { Filter = TabularFileTypes.OpenFilter, Multiselect = true, CheckFileExists = true, Title = "选择要格式统一的文件" };
        if (dialog.ShowDialog() == true) await LoadFilesAsync(dialog.FileNames);
    }

    public async Task LoadFilesAsync(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Files.Clear(); SelectedFile = null; ProgressPercent = 0;
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var editor = new FormatStandardizationViewModel(inspection, single, outputDirectoryPreferenceService: preferences,
                    showSaveFileDialog: saveAs, confirmExistingOutput: confirm);
                var item = new FormatBatchFileViewModel(path, editor);
                Files.Add(item);
                editor.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(editor.IsPreviewLoading)) OnPropertyChanged(nameof(CanStart));
                };
                await editor.LoadFileAsync(path);
                if (editor.HasError) item.ResultText = editor.ErrorMessage ?? "文件加载失败";
            }
            SelectedFile = Files.FirstOrDefault(); Summary = $"已选择 {Files.Count} 个文件；规则对整批共用。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void RemoveSelected()
    {
        if (IsBusy || SelectedFile is null) return;
        Files.Remove(SelectedFile); SelectedFile = Files.FirstOrDefault(); OnPropertyChanged(nameof(CanStart));
    }

    [RelayCommand]
    public void ChooseOutputDirectory()
    {
        if (IsBusy) return;
        var dialog = new OpenFolderDialog { Title = "选择整批输出目录" };
        if (dialog.ShowDialog() == true) SetOutputDirectory(dialog.FolderName);
    }

    public void SetOutputDirectory(string directory)
    {
        if (IsBusy || !Directory.Exists(directory)) return;
        preferences.SetRememberedDirectory(directory);
        foreach (var item in Files)
            item.Editor.OutputFilePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(item.FilePath) + "_格式统一" + Path.GetExtension(item.FilePath));
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (!CanStart) return;
        IsBusy = true; ProgressPercent = 0; Summary = "正在处理...";
        var snapshot = Files.ToArray();
        var requests = new List<BatchFormatItem>();
        try
        {
            foreach (var item in snapshot)
            {
                item.ResultText = "等待处理"; item.ResultPath = null;
                var editor = item.Editor;
                var output = editor.OutputFilePath ?? "";
                var overwrite = false;
                while (File.Exists(output))
                {
                    // Never ask for permission to overwrite any source in the batch.
                    if (snapshot.Any(source => PathsEqual(source.FilePath, output))) break;
                    var choice = Confirm(output);
                    if (choice == ExistingOutputChoice.Overwrite) { overwrite = true; break; }
                    if (choice == ExistingOutputChoice.Cancel) break;
                    var selected = SaveAs(output);
                    if (string.IsNullOrWhiteSpace(selected)) break;
                    output = selected; editor.OutputFilePath = selected;
                    preferences.SetRememberedDirectory(Path.GetDirectoryName(selected));
                }
                requests.Add(new(new(item.FilePath, editor.SelectedWorksheet?.Name, editor.HeaderRowNumber), output, overwrite));
            }
            var options = new FormatStandardizationOptions
            {
                TrimOuterWhitespace = Rules.TrimOuterWhitespace,
                RemoveTabsNewLinesAndHiddenCharacters = Rules.RemoveTabsNewLinesAndHiddenCharacters,
                NormalizeFullWidthHalfWidth = Rules.NormalizeFullWidthHalfWidth,
                NormalizeUnicode = Rules.NormalizeUnicode,
                NormalizeSafeNumbers = Rules.NormalizeSafeNumbers,
                NormalizeUnambiguousDates = Rules.NormalizeUnambiguousDates
            };
            var progress = new Progress<BatchFormatProgress>(p =>
            {
                if (IsBusy) ProgressPercent = p.TotalCount == 0 ? 0 : p.CompletedCount * 100 / p.TotalCount;
            });
            var result = await batch.ExecuteAsync(new(requests, options), progress);
            foreach (var entry in result.Items)
            {
                var item = snapshot[entry.Index]; item.ResultPath = entry.Result.OutputFilePath;
                item.ResultText = entry.Result.Success ? $"成功 · {entry.Result.Summary?.ProcessedDataRowCount} 行" : $"失败 · {entry.Result.Error?.Message}";
            }
            ProgressPercent = 100; Summary = $"处理完成：成功 {result.SucceededCount}，失败 {result.FailedCount}，总计 {snapshot.Length}。";
        }
        catch (Exception ex) { Summary = $"批量处理异常：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void SendSelectedToMatching()
    {
        if (!IsBusy && SelectedFile?.ResultPath is { } path && File.Exists(path)) sendToMatching?.Invoke(path);
    }

    [RelayCommand]
    public void OpenSelectedResult() => OpenPath(SelectedFile?.ResultPath);
    [RelayCommand]
    public void OpenSelectedDirectory() => OpenPath(Path.GetDirectoryName(SelectedFile?.ResultPath));
    private void OpenPath(string? path)
    {
        if (!CanUseSelectedResult || string.IsNullOrWhiteSpace(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Summary = $"无法打开结果：{ex.Message}"; }
    }

    private ExistingOutputChoice Confirm(string path)
    {
        if (confirm is not null) return confirm(path);
        var dialog = new ExistingOutputDialog(path);
        if (Application.Current?.MainWindow is { } owner) dialog.Owner = owner;
        dialog.ShowDialog(); return dialog.Choice;
    }
    private string? SaveAs(string path)
    {
        if (saveAs is not null) return saveAs(path);
        var dialog = new SaveFileDialog { Filter = TabularFileTypes.SaveFilter(path), FileName = Path.GetFileName(path), DefaultExt = Path.GetExtension(path), InitialDirectory = Path.GetDirectoryName(path), AddExtension = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
