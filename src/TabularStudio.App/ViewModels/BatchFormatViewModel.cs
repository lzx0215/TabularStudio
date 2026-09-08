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
    [ObservableProperty] private bool _isChecked;
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
    private readonly Func<bool, string, string[]?>? openFiles;
    public ObservableCollection<FormatBatchFileViewModel> Files { get; } = [];
    public ObservableCollection<BatchFailureDetail> Failures { get; } = [];
    public FormatStandardizationViewModel Rules { get; }
    [ObservableProperty] private FormatBatchFileViewModel? _selectedFile;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanUseSelectedResult))]
    [NotifyPropertyChangedFor(nameof(HasCheckedFiles))]
    private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _summary = "请选择一个或多个表格文件。";
    public bool CanConfigure => !IsBusy;
    public bool HasSelectedRule => Rules.TrimOuterWhitespace || Rules.RemoveTabsNewLinesAndHiddenCharacters ||
        Rules.NormalizeFullWidthHalfWidth || Rules.NormalizeUnicode || Rules.NormalizeSafeNumbers || Rules.NormalizeUnambiguousDates;
    public string RuleHint => HasSelectedRule ? "所选规则将应用到全部文件。" : "请至少选择一项处理规则后再开始。";
    public bool CanStart => !IsBusy && HasSelectedRule && Files.Count > 0 && Files.All(f => !f.Editor.IsPreviewLoading);
    public bool HasCheckedFiles => !IsBusy && Files.Any(f => f.IsChecked);
    public bool CanUseSelectedResult => !IsBusy && SelectedFile?.ResultPath is not null;
    partial void OnSelectedFileChanged(FormatBatchFileViewModel? value) => OnPropertyChanged(nameof(CanUseSelectedResult));

    public BatchFormatViewModel(IWorkbookInspectionService inspection, IFormatStandardizationService single,
        IOutputDirectoryPreferenceService? preferences = null, IBatchFormatStandardizationService? batch = null,
        Func<string, ExistingOutputChoice>? confirm = null, Func<string, string?>? saveAs = null,
        Action<string>? sendToMatching = null, Func<bool, string, string[]?>? openFiles = null)
    {
        this.inspection = inspection; this.single = single; this.preferences = preferences ?? new OutputDirectoryPreferenceService();
        this.batch = batch ?? new BatchFormatStandardizationService(single); this.confirm = confirm; this.saveAs = saveAs; this.sendToMatching = sendToMatching;
        this.openFiles = openFiles;
        Rules = new(inspection, single, outputDirectoryPreferenceService: this.preferences);
        Rules.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelectedRule)); OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(RuleHint));
        };
    }

    [RelayCommand]
    public async Task BrowseFilesAsync()
    {
        if (IsBusy) return;
        var paths = ChooseFiles(true, "添加文件（可按 Ctrl / Shift 多选）");
        if (paths is not null) await LoadFilesAsync(paths);
    }

    public async Task LoadFilesAsync(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            ProgressPercent = 0; Failures.Clear();
            FormatBatchFileViewModel? firstAdded = null;
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Files.Any(f => PathsEqual(f.FilePath, path))) continue;
                var item = await CreateFileAsync(path);
                Files.Add(item);
                firstAdded ??= item;
            }
            SelectedFile = firstAdded ?? SelectedFile ?? Files.FirstOrDefault(); Summary = $"已添加 {Files.Count} 个文件；点击文件或使用预览选择框切换。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void RemoveSelected()
    {
        if (IsBusy) return;
        foreach (var file in Files.Where(f => f.IsChecked).ToArray()) Files.Remove(file);
        if (SelectedFile is null || !Files.Contains(SelectedFile)) SelectedFile = Files.FirstOrDefault();
        OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(HasCheckedFiles));
        Summary = $"列表中剩余 {Files.Count} 个文件。"; Failures.Clear();
    }

    [RelayCommand]
    public async Task ReplaceCheckedFilesAsync()
    {
        if (!HasCheckedFiles) return;
        IsBusy = true;
        var changed = 0; var duplicate = 0;
        try
        {
            foreach (var old in Files.Where(f => f.IsChecked).ToArray())
            {
                var paths = ChooseFiles(false, $"替换：{old.FileName}");
                if (paths is not { Length: > 0 }) continue;
                var path = paths[0];
                if (Files.Any(f => f != old && PathsEqual(f.FilePath, path))) { duplicate++; continue; }
                var replacement = await CreateFileAsync(path);
                Files[Files.IndexOf(old)] = replacement;
                if (SelectedFile == old) SelectedFile = replacement;
                changed++;
            }
            Failures.Clear();
            Summary = $"已修改 {changed} 个文件。" + (duplicate > 0 ? $"{duplicate} 项与列表已有文件重复，保留原项。" : "取消的项目保持不变。");
        }
        finally { IsBusy = false; OnPropertyChanged(nameof(HasCheckedFiles)); }
    }

    private async Task<FormatBatchFileViewModel> CreateFileAsync(string path)
    {
        var editor = new FormatStandardizationViewModel(inspection, single, outputDirectoryPreferenceService: preferences,
            showSaveFileDialog: saveAs, confirmExistingOutput: confirm);
        var item = new FormatBatchFileViewModel(path, editor);
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.IsChecked)) OnPropertyChanged(nameof(HasCheckedFiles)); };
        editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(editor.IsPreviewLoading)) OnPropertyChanged(nameof(CanStart)); };
        await editor.LoadFileAsync(path);
        if (editor.HasError) item.ResultText = editor.ErrorMessage ?? "文件加载失败";
        return item;
    }

    private string[]? ChooseFiles(bool multiple, string title)
    {
        if (openFiles is not null) return openFiles(multiple, title);
        var dialog = new OpenFileDialog { Filter = TabularFileTypes.OpenFilter, Multiselect = multiple, CheckFileExists = true, Title = title };
        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    [RelayCommand]
    public void PreviewFile(FormatBatchFileViewModel? file)
    {
        if (!IsBusy && file is not null && Files.Contains(file)) SelectedFile = file;
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
        IsBusy = true; ProgressPercent = 0; Summary = "正在处理..."; Failures.Clear();
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
                if (!entry.Result.Success)
                {
                    var error = entry.Result.Error;
                    var context = $"工作表：{entry.Item.Source.WorksheetName ?? "CSV（无工作表）"}；表头行：{entry.Item.Source.HeaderRowNumber}；输出：{entry.Item.OutputFilePath}";
                    Failures.Add(new(item.FilePath, error?.Message ?? "处理失败", context,
                        string.IsNullOrWhiteSpace(error?.Detail) ? "未提供具体行/列位置；请根据失败原因检查文件或配置。" : error.Detail));
                }
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

public sealed record BatchFailureDetail(string FilePath, string Reason, string Context, string Detail);
