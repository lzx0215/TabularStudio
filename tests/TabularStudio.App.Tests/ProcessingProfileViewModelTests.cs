using System.IO;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Models;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class ProcessingProfileViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TabularStudio-profile-vm-" + Guid.NewGuid());

    public ProcessingProfileViewModelTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private OutputDirectoryPreferenceService Preferences() =>
        new(Path.Combine(_directory, "preferences.json"));

    #region Format Profile Tests

    [Fact]
    public void FormatProfile_InitialState_WithNoStore_PreservesCompatibility()
    {
        // Calling constructor without profile store works cleanly
        var vm = new BatchFormatViewModel(
            new WorkbookInspectionService(),
            new FormatStandardizationService(),
            Preferences());

        Assert.False(vm.HasProfiles);
        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.False(vm.CanApplyProfile);
        Assert.False(vm.CanDeleteProfile);
        Assert.False(vm.CanSaveProfile); // No rules selected
    }

    [Fact]
    public void FormatProfile_Save_BlocksWhenZeroRulesSelected()
    {
        var store = new InMemoryProfileStore();
        var vm = new BatchFormatViewModel(
            new WorkbookInspectionService(),
            new FormatStandardizationService(),
            Preferences(),
            profileStore: store,
            profileValidator: new FakeProfileValidator(),
            promptProfileName: _ => "有效配置");

        Assert.False(vm.Rules.TrimOuterWhitespace);
        Assert.False(vm.CanSaveProfile);

        vm.SaveProfileCommand.Execute(null);
        Assert.Contains("至少选择一项", vm.ProfileStatusMessage);
        Assert.Empty(store.FormatProfiles);
    }

    [Fact]
    public void FormatProfile_Save_SavesCleanly_AndDetectsManualEdits()
    {
        var store = new InMemoryProfileStore();
        string? promptedName = "月度清理配置";
        var vm = new BatchFormatViewModel(
            new WorkbookInspectionService(),
            new FormatStandardizationService(),
            Preferences(),
            profileStore: store,
            profileValidator: new FakeProfileValidator(),
            promptProfileName: _ => promptedName);

        vm.Rules.TrimOuterWhitespace = true;
        vm.Rules.NormalizeFullWidthHalfWidth = true;
        vm.Rules.NormalizeSafeNumbers = true;
        Assert.True(vm.CanSaveProfile);

        vm.SaveProfileCommand.Execute(null);

        Assert.Equal("已保存：月度清理配置", vm.ProfileStatusMessage);
        Assert.Single(vm.Profiles);
        Assert.Equal("月度清理配置", vm.SelectedProfile?.Name);
        Assert.True(store.FormatProfiles.ContainsKey("月度清理配置"));

        var saved = store.FormatProfiles["月度清理配置"];
        Assert.True(saved.Format?.TrimOuterWhitespace);
        Assert.True(saved.Format?.NormalizeFullWidthHalfWidth);
        Assert.True(saved.Format?.NormalizeSafeNumbers);
        Assert.False(saved.Format?.NormalizeUnambiguousDates);

        // Modify a rule manually
        vm.Rules.NormalizeUnicode = true;
        Assert.Equal("当前设置已修改", vm.ProfileStatusMessage);

        // Profile on store is NOT modified
        Assert.False(store.FormatProfiles["月度清理配置"].Format?.NormalizeUnicode);

        // Re-apply restores saved rules
        vm.ApplyProfileCommand.Execute(null);
        Assert.Equal("已应用：月度清理配置", vm.ProfileStatusMessage);
        Assert.False(vm.Rules.NormalizeUnicode);
        Assert.True(vm.Rules.TrimOuterWhitespace);
    }

    [Fact]
    public void FormatProfile_Refresh_DisplaysErrorsWhenCorruptedFilesExist_WhileRetainingValidProfiles()
    {
        var store = new InMemoryProfileStore();
        store.FormatProfiles["有效配置"] = new ProcessingProfile(
            1, "有效配置", ProcessingProfileKind.FormatStandardization,
            new FormatProfileSettings(true, false, false, false, false, false), null);
        store.ErrorsToReturn = ["bad.json: 文件损坏，解析失败。"];

        var vm = new BatchFormatViewModel(
            new WorkbookInspectionService(),
            new FormatStandardizationService(),
            Preferences(),
            profileStore: store,
            profileValidator: new FakeProfileValidator());

        Assert.Contains("发现 1 个损坏配置", vm.ProfileStatusMessage);
        Assert.Contains("bad.json: 文件损坏", vm.ProfileStatusMessage);
        Assert.Single(vm.Profiles);
        Assert.Equal("有效配置", vm.Profiles[0].Name);
    }

    [Fact]
    public void FormatProfile_NullValidator_RejectsSaveAndApply()
    {
        var store = new InMemoryProfileStore();
        store.FormatProfiles["既有配置"] = new ProcessingProfile(
            1, "既有配置", ProcessingProfileKind.FormatStandardization,
            new FormatProfileSettings(true, false, false, false, false, false), null);

        var vm = new BatchFormatViewModel(
            new WorkbookInspectionService(),
            new FormatStandardizationService(),
            Preferences(),
            profileStore: store,
            profileValidator: null, // Null validator
            promptProfileName: _ => "新配置");

        vm.Rules.TrimOuterWhitespace = true;
        vm.SaveProfileCommand.Execute(null);
        Assert.Equal("未保存：配置校验服务未初始化。", vm.ProfileStatusMessage);

        vm.SelectedProfile = vm.Profiles[0];
        vm.ApplyProfileCommand.Execute(null);
        Assert.Equal("未应用：配置校验服务未初始化。", vm.ProfileStatusMessage);
    }

    [Fact]
    public void FormatProfile_OverwriteConflict_CancelRetainsOld_ConfirmOverwrites()
    {
        var store = new InMemoryProfileStore();
        bool allowOverwrite = false;
        var vm = new BatchFormatViewModel(
            new WorkbookInspectionService(),
            new FormatStandardizationService(),
            Preferences(),
            profileStore: store,
            profileValidator: new FakeProfileValidator(),
            promptProfileName: _ => "常用规则",
            confirmOverwrite: _ => allowOverwrite);

        vm.Rules.TrimOuterWhitespace = true;
        vm.SaveProfileCommand.Execute(null);
        Assert.True(store.FormatProfiles.ContainsKey("常用规则"));

        // User changes rule and tries to save with same name but cancels overwrite
        vm.Rules.NormalizeSafeNumbers = true;
        allowOverwrite = false;
        vm.SaveProfileCommand.Execute(null);
        Assert.False(store.FormatProfiles["常用规则"].Format?.NormalizeSafeNumbers);

        // User confirms overwrite
        allowOverwrite = true;
        vm.SaveProfileCommand.Execute(null);
        Assert.True(store.FormatProfiles["常用规则"].Format?.NormalizeSafeNumbers);
    }

    [Fact]
    public void FormatProfile_Delete_RetainsCurrentPageSettings()
    {
        var store = new InMemoryProfileStore();
        bool allowDelete = true;
        var vm = new BatchFormatViewModel(
            new WorkbookInspectionService(),
            new FormatStandardizationService(),
            Preferences(),
            profileStore: store,
            profileValidator: new FakeProfileValidator(),
            promptProfileName: _ => "临时配置",
            confirmDelete: _ => allowDelete);

        vm.Rules.TrimOuterWhitespace = true;
        vm.SaveProfileCommand.Execute(null);
        Assert.Single(vm.Profiles);

        vm.DeleteProfileCommand.Execute(null);
        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.False(store.FormatProfiles.ContainsKey("临时配置"));

        // Page settings must remain intact
        Assert.True(vm.Rules.TrimOuterWhitespace);
    }

    #endregion

    #region Data Matching Profile Tests

    private async Task<DataMatchingViewModel> CreateMatchingVm(
        InMemoryProfileStore store,
        IProcessingProfileValidator? validator = null,
        Func<string, string?>? prompt = null,
        Func<string, bool>? overwrite = null,
        Func<string, bool>? delete = null,
        IWorkbookInspectionService? inspectionService = null,
        string? masterContents = null,
        string? referenceContents = null)
    {
        var master = Path.Combine(_directory, "master.csv");
        var reference = Path.Combine(_directory, "ref.csv");
        File.WriteAllText(master, masterContents ?? "Dept,Code,Name\r\nCardiology,001,John\r\nNeurology,002,Jane\r\n");
        File.WriteAllText(reference, referenceContents ?? "Code,Desc\r\n001,Heart\r\n002,Brain\r\n");

        var inspection = inspectionService ?? new WorkbookInspectionService();
        var vm = new DataMatchingViewModel(
            inspection,
            new DataMatchingService(),
            outputDirectoryPreferenceService: Preferences(),
            profileStore: store,
            profileValidator: validator ?? new FakeProfileValidator(),
            promptProfileName: prompt,
            confirmOverwrite: overwrite,
            confirmDelete: delete);

        await vm.LoadMasterFileAsync(master);
        await vm.LoadReferenceFileAsync(reference);

        // Configure condition: Master Col 2 (Code) == Ref Col 1 (Code)
        vm.Conditions[0].SelectedMasterColumn = vm.MasterAvailableColumns[1];
        vm.Conditions[0].SelectedReferenceColumn = vm.ReferenceAvailableColumns[0];

        // Return field: Desc (Ref Col 2)
        vm.ReturnFields[1].IsSelected = true;

        vm.OutputFilePath = Path.Combine(_directory, "out.csv");
        return vm;
    }

    [Fact]
    public async Task MatchingProfile_ApplyAfterSuccess_ClearsOldResultAndUsesNewSettingsOnlyOnStart()
    {
        var store = new InMemoryProfileStore();
        var vm = await CreateMatchingVm(store,
            validator: new ProcessingProfileValidator(new WorkbookInspectionService()),
            prompt: _ => "标准化全部匹配",
            masterContents: "Dept,Code,Name\r\nCardiology, A ,John\r\nNeurology, B ,Jane\r\n",
            referenceContents: "Code,Desc\r\nA,Heart\r\nB,Brain\r\n");
        vm.NormalizeComparisonKeys = true;
        vm.SaveProfile();
        Assert.Equal("已保存：标准化全部匹配", vm.ProfileStatusMessage);

        vm.NormalizeComparisonKeys = false;
        vm.IsMasterFilterEnabled = true;
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0];
        await vm.MasterFilterValuesLoadTask;
        vm.SelectedMasterFilterValue = vm.MasterFilterValues.Single(v => v.Value.RawValue == "Cardiology");
        await vm.StartAsync();
        Assert.True(vm.HasSuccess, vm.ErrorMessage);
        Assert.Equal(0, vm.ResultMatchedCount);
        Assert.Equal(1, vm.ResultUnmatchedCount);
        Assert.Equal(1, vm.ResultSkippedCount);
        var oldOutput = vm.ResultOutputFilePath!;
        var oldBytes = File.ReadAllBytes(oldOutput);
        var filesBefore = Directory.GetFiles(_directory).Order().ToArray();

        await vm.ApplyProfileAsync();

        Assert.Equal("已应用：标准化全部匹配", vm.ProfileStatusMessage);
        Assert.True(vm.NormalizeComparisonKeys);
        Assert.False(vm.IsMasterFilterEnabled);
        Assert.False(vm.HasSuccess);
        Assert.False(vm.CanExecuteSuccessActions);
        Assert.Null(vm.ResultOutputFilePath);
        Assert.Null(vm.SuccessMessage);
        Assert.Equal(0, vm.ResultTotalMasterDataRowCount);
        Assert.Equal(0, vm.ResultMatchedCount);
        Assert.Equal(0, vm.ResultUnmatchedCount);
        Assert.Equal(0, vm.ResultDuplicateCount);
        Assert.Equal(0, vm.ResultEmptyKeyCount);
        Assert.Equal(0, vm.ResultSkippedCount);
        Assert.Equal(TimeSpan.Zero, vm.ResultElapsed);
        Assert.Equal(0, vm.ProgressPercent);
        Assert.Null(vm.ProgressStageText);
        Assert.True(vm.CanStart);
        Assert.Equal(oldOutput, vm.OutputFilePath);
        Assert.Equal(oldBytes, File.ReadAllBytes(oldOutput));
        Assert.Equal(filesBefore, Directory.GetFiles(_directory).Order().ToArray());

        vm.OutputFilePath = Path.Combine(_directory, "new-settings.csv");
        await vm.StartAsync();
        Assert.True(vm.HasSuccess, vm.ErrorMessage);
        Assert.Equal(2, vm.ResultMatchedCount);
        Assert.Equal(0, vm.ResultSkippedCount);
        Assert.Equal(oldBytes, File.ReadAllBytes(oldOutput));
    }

    [Fact]
    public async Task MatchingProfile_RejectedAfterSuccess_PreservesCurrentSettingsAndResult()
    {
        var store = new InMemoryProfileStore();
        var vm = await CreateMatchingVm(store,
            validator: new ProcessingProfileValidator(new WorkbookInspectionService()),
            prompt: _ => "匹配配置");
        vm.SaveProfile();
        await vm.StartAsync();
        Assert.True(vm.HasSuccess, vm.ErrorMessage);
        var oldOutput = vm.ResultOutputFilePath!;
        var oldBytes = File.ReadAllBytes(oldOutput);
        var oldCondition = vm.Conditions[0];
        var oldMessage = vm.SuccessMessage;
        var profile = store.MatchingProfiles["匹配配置"];
        store.MatchingProfiles[profile.Name] = profile with
        {
            Matching = profile.Matching! with
            {
                Conditions = [new MatchingCondition(new ColumnReference(99, "Missing"), new ColumnReference(1, "Code"))]
            }
        };

        await vm.ApplyProfileAsync();

        Assert.StartsWith("未应用：", vm.ProfileStatusMessage);
        Assert.Same(oldCondition, vm.Conditions[0]);
        Assert.True(vm.HasSuccess);
        Assert.True(vm.CanExecuteSuccessActions);
        Assert.Equal(2, vm.ResultMatchedCount);
        Assert.Equal(oldMessage, vm.SuccessMessage);
        Assert.Equal(oldOutput, vm.ResultOutputFilePath);
        Assert.Equal(oldBytes, File.ReadAllBytes(oldOutput));
        Assert.False(vm.IsProfileValidating);
    }

    [Fact]
    public async Task MatchingProfile_Save_ValidatesScopeAndStoresNoPaths()
    {
        var store = new InMemoryProfileStore();
        var vm = await CreateMatchingVm(store, prompt: _ => "科室代码匹配");

        Assert.True(vm.CanSaveProfile);
        vm.SaveProfileCommand.Execute(null);

        Assert.Equal("已保存：科室代码匹配", vm.ProfileStatusMessage);
        Assert.Single(vm.Profiles);
        Assert.True(store.MatchingProfiles.ContainsKey("科室代码匹配"));

        var profile = store.MatchingProfiles["科室代码匹配"];
        Assert.Equal(ProcessingProfileKind.DataMatching, profile.Kind);
        Assert.NotNull(profile.Matching);

        // Verify out-of-scope items: MUST NOT contain file paths, sheet names, output paths
        Assert.Single(profile.Matching!.Conditions);
        Assert.Equal(2, profile.Matching.Conditions[0].MasterColumn.ColumnNumber);
        Assert.Equal("Code", profile.Matching.Conditions[0].MasterColumn.HeaderText);
        Assert.Equal(1, profile.Matching.Conditions[0].ReferenceColumn.ColumnNumber);
        Assert.Equal("Code", profile.Matching.Conditions[0].ReferenceColumn.HeaderText);

        Assert.Single(profile.Matching.ReturnFields);
        Assert.Equal(2, profile.Matching.ReturnFields[0].ColumnNumber);
        Assert.Equal("Desc", profile.Matching.ReturnFields[0].HeaderText);
    }

    [Fact]
    public async Task MatchingProfile_Refresh_DisplaysErrorsWhenCorruptedFilesExist_WhileRetainingValidProfiles()
    {
        var store = new InMemoryProfileStore();
        store.MatchingProfiles["有效匹配配置"] = new ProcessingProfile(
            1, "有效匹配配置", ProcessingProfileKind.DataMatching,
            null, new MatchingProfileSettings([], [], true, new ProfileStatusSettings(true, "匹配状态"), null));
        store.ErrorsToReturn = ["bad_matching.json: JSON 格式非法。"];

        var vm = await CreateMatchingVm(store);
        vm.RefreshProfiles();

        Assert.Contains("发现 1 个损坏配置", vm.ProfileStatusMessage);
        Assert.Contains("bad_matching.json: JSON 格式非法", vm.ProfileStatusMessage);
        Assert.Contains(vm.Profiles, p => p.Name == "有效匹配配置");
    }

    [Fact]
    public async Task MatchingProfile_NullValidator_RejectsSaveAndApply()
    {
        var store = new InMemoryProfileStore();
        store.MatchingProfiles["已有配置"] = new ProcessingProfile(
            1, "已有配置", ProcessingProfileKind.DataMatching,
            null, new MatchingProfileSettings(
                [new MatchingCondition(new ColumnReference(2, "Code"), new ColumnReference(1, "Code"))],
                [new ColumnReference(2, "Desc")],
                true,
                new ProfileStatusSettings(true, "匹配状态"),
                null));

        var master = Path.Combine(_directory, "master_null.csv");
        var reference = Path.Combine(_directory, "ref_null.csv");
        File.WriteAllText(master, "Dept,Code,Name\r\nCardiology,001,John\r\n");
        File.WriteAllText(reference, "Code,Desc\r\n001,Heart\r\n");

        var inspection = new WorkbookInspectionService();
        var vm = new DataMatchingViewModel(
            inspection,
            new DataMatchingService(),
            outputDirectoryPreferenceService: Preferences(),
            profileStore: store,
            profileValidator: null, // Null validator
            promptProfileName: _ => "新配置");

        await vm.LoadMasterFileAsync(master);
        await vm.LoadReferenceFileAsync(reference);
        vm.Conditions[0].SelectedMasterColumn = vm.MasterAvailableColumns[1];
        vm.Conditions[0].SelectedReferenceColumn = vm.ReferenceAvailableColumns[0];
        vm.ReturnFields[1].IsSelected = true;
        vm.OutputFilePath = Path.Combine(_directory, "out_null.csv");

        vm.SaveProfileCommand.Execute(null);
        Assert.Equal("未保存：配置校验服务未初始化。", vm.ProfileStatusMessage);

        vm.SelectedProfile = vm.Profiles[0];
        await vm.ApplyProfileCommand.ExecuteAsync(null);
        Assert.Equal("未应用：配置校验服务未初始化。", vm.ProfileStatusMessage);
    }

    [Fact]
    public async Task MatchingProfile_CanSaveAndApply_BlockedWhilePreviewIsLoading()
    {
        var store = new InMemoryProfileStore();
        var vm = await CreateMatchingVm(store, prompt: _ => "加载校验");

        store.MatchingProfiles["加载校验"] = new ProcessingProfile(
            1, "加载校验", ProcessingProfileKind.DataMatching,
            null, new MatchingProfileSettings([], [], true, new ProfileStatusSettings(true, "匹配状态"), null));
        vm.RefreshProfiles();
        vm.SelectedProfile = vm.Profiles[0];

        Assert.True(vm.CanSaveProfile);
        Assert.True(vm.CanApplyProfile);

        // While master preview is loading, save & apply must be blocked
        vm.IsMasterPreviewLoading = true;
        Assert.False(vm.CanSaveProfile);
        Assert.False(vm.CanApplyProfile);
        vm.IsMasterPreviewLoading = false;

        // While reference preview is loading, save & apply must be blocked
        vm.IsReferencePreviewLoading = true;
        Assert.False(vm.CanSaveProfile);
        Assert.False(vm.CanApplyProfile);
        vm.IsReferencePreviewLoading = false;

        Assert.True(vm.CanSaveProfile);
        Assert.True(vm.CanApplyProfile);
    }

    [Fact]
    public async Task MatchingProfile_CanSave_RequiresValidColumnSelectionAndFilterCandidate()
    {
        var store = new InMemoryProfileStore();
        var vm = await CreateMatchingVm(store);

        Assert.True(vm.CanSaveProfile);

        // Stale master column not in MasterAvailableColumns
        var staleMasterCol = new AvailableColumnItem(new ColumnReference(99, "NonExistent"));
        vm.Conditions[0].SelectedMasterColumn = staleMasterCol;
        Assert.False(vm.CanSaveProfile);
        vm.Conditions[0].SelectedMasterColumn = vm.MasterAvailableColumns[1];
        Assert.True(vm.CanSaveProfile);

        // Deselect all return fields -> CanSaveProfile false
        vm.DeselectAllReturnFields();
        Assert.False(vm.CanSaveProfile);
        vm.SelectAllReturnFields();
        Assert.True(vm.CanSaveProfile);

        // Enable master filter without selecting valid candidate
        vm.IsMasterFilterEnabled = true;
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0];
        vm.SelectedMasterFilterValue = null;
        Assert.False(vm.CanSaveProfile);

        // With valid candidate value selected
        await vm.MasterFilterValuesLoadTask;
        vm.SelectedMasterFilterValue = vm.MasterFilterValues.First();
        Assert.True(vm.CanSaveProfile);
    }

    [Fact]
    public async Task MatchingProfile_ValidationBusy_DisablesNavigationAndConfiguration()
    {
        var store = new InMemoryProfileStore();
        var tcs = new TaskCompletionSource<ProfileApplicationResult>();
        var validator = new FakeProfileValidator
        {
            CustomPrepare = (_, _, _, _, _) => tcs.Task
        };

        var vm = await CreateMatchingVm(store, validator: validator, prompt: _ => "忙碌校验");
        vm.SaveProfileCommand.Execute(null);
        vm.SelectedProfile = vm.Profiles[0];

        // Start applying asynchronously
        var applyTask = vm.ApplyProfileCommand.ExecuteAsync(null);

        // While validating in background
        Assert.True(vm.IsProfileValidating);
        Assert.False(vm.CanBrowseMaster);
        Assert.False(vm.CanBrowseReference);
        Assert.False(vm.CanConfigure);
        Assert.False(vm.CanStart);
        Assert.False(vm.CanSelectProfile);
        Assert.False(vm.CanApplyProfile);
        Assert.False(vm.CanSaveProfile);
        Assert.False(vm.CanDeleteProfile);

        // Complete the async validation
        tcs.SetResult(new ProfileApplicationResult(true, [], [], null));
        await applyTask;

        // Validation finished
        Assert.False(vm.IsProfileValidating);
        Assert.True(vm.CanBrowseMaster);
        Assert.True(vm.CanConfigure);
    }

    [Fact]
    public async Task MatchingProfile_Apply_StaleSourceChangeDiscardsResultWithoutHanging()
    {
        var store = new InMemoryProfileStore();
        var tcs = new TaskCompletionSource<ProfileApplicationResult>();
        var validator = new FakeProfileValidator
        {
            CustomPrepare = (_, _, _, _, _) => tcs.Task
        };

        var vm = await CreateMatchingVm(store, validator: validator, prompt: _ => "数据源变更测试");
        vm.SaveProfileCommand.Execute(null);
        vm.SelectedProfile = vm.Profiles[0];

        // Start applying profile
        var applyTask = vm.ApplyProfileCommand.ExecuteAsync(null);
        Assert.True(vm.IsProfileValidating);

        // While in-flight, change the master file path
        vm.MasterFilePath = Path.Combine(_directory, "different_master.csv");

        // Complete async preparation
        tcs.SetResult(new ProfileApplicationResult(true, [], [], null));
        await applyTask;

        // Result must be discarded without leaving the page in "正在校验"
        Assert.False(vm.IsProfileValidating);
        Assert.Equal("未应用：数据源已发生变更。", vm.ProfileStatusMessage);
    }

    [Fact]
    public async Task MatchingProfile_Apply_WithValidationAndCandidateSuppression()
    {
        var store = new InMemoryProfileStore();
        var validator = new FakeProfileValidator();
        var vm = await CreateMatchingVm(store, validator: validator, prompt: _ => "测试匹配");

        // Enable master filter with Dept == Cardiology
        vm.IsMasterFilterEnabled = true;
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0];
        await vm.MasterFilterValuesLoadTask;
        vm.SelectedMasterFilterValue = vm.MasterFilterValues.FirstOrDefault(v => v.DisplayText == "Cardiology");

        // Save profile
        vm.SaveProfileCommand.Execute(null);
        Assert.Equal("已保存：测试匹配", vm.ProfileStatusMessage);

        // Change conditions and clear filter manually
        vm.IsMasterFilterEnabled = false;
        vm.Conditions[0].SelectedMasterColumn = vm.MasterAvailableColumns[0];
        Assert.Equal("当前设置已修改", vm.ProfileStatusMessage);

        // Setup validator to return success for PrepareMatchingAsync
        var filterColRef = new ColumnReference(1, "Dept");
        var preparedFilterValue = new ColumnValueOption("Cardiology", new ColumnFilterValue(ColumnValueKind.Text, "Cardiology"));
        validator.PrepareResult = new ProfileApplicationResult(
            Success: true,
            Errors: [],
            FilterValues: [preparedFilterValue],
            SelectedFilterValue: preparedFilterValue);

        // Apply profile
        await vm.ApplyProfileCommand.ExecuteAsync(null);

        Assert.Equal("已应用：测试匹配", vm.ProfileStatusMessage);
        Assert.True(vm.IsMasterFilterEnabled);
        Assert.Equal("Dept", vm.SelectedMasterFilterColumn?.HeaderText);
        Assert.Single(vm.MasterFilterValues);
        Assert.Equal("Cardiology", vm.SelectedMasterFilterValue?.DisplayText);

        // Verify condition restored
        Assert.Equal("Code", vm.Conditions[0].SelectedMasterColumn?.HeaderText);
        Assert.Equal("Code", vm.Conditions[0].SelectedReferenceColumn?.HeaderText);
    }

    [Fact]
    public async Task MatchingProfile_Apply_ValidationFailureRejectsAllOrNothing()
    {
        var store = new InMemoryProfileStore();
        var validator = new FakeProfileValidator();
        var vm = await CreateMatchingVm(store, validator: validator, prompt: _ => "待失效配置");

        vm.SaveProfileCommand.Execute(null);

        // Configure validator to fail PrepareMatchingAsync (e.g. column missing or renamed)
        validator.PrepareResult = new ProfileApplicationResult(
            Success: false,
            Errors: ["对照表 C 列原为'Desc'，当前为'Amount'"],
            FilterValues: [],
            SelectedFilterValue: null);

        // Current state before apply
        var condMasterCol = vm.Conditions[0].SelectedMasterColumn;
        var condRefCol = vm.Conditions[0].SelectedReferenceColumn;
        var returnFieldSelected = vm.ReturnFields[1].IsSelected;

        await vm.ApplyProfileCommand.ExecuteAsync(null);

        Assert.Contains("未应用", vm.ProfileStatusMessage);
        Assert.Contains("对照表 C 列原为'Desc'", vm.ProfileStatusMessage);

        // Must retain previous state completely (All-or-Nothing)
        Assert.Same(condMasterCol, vm.Conditions[0].SelectedMasterColumn);
        Assert.Same(condRefCol, vm.Conditions[0].SelectedReferenceColumn);
        Assert.Equal(returnFieldSelected, vm.ReturnFields[1].IsSelected);
    }

    [Fact]
    public async Task MatchingProfile_Apply_CancelsInFlightFilterLoading()
    {
        var store = new InMemoryProfileStore();
        var validator = new FakeProfileValidator();
        var realInspection = new WorkbookInspectionService();
        var deferredInspection = new DeferredInspectionService(realInspection);
        var vm = await CreateMatchingVm(store, validator: validator, prompt: _ => "取消在途筛选加载测试", inspectionService: deferredInspection);

        // Save a profile with filter enabled (Dept == Cardiology)
        vm.IsMasterFilterEnabled = true;
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0];
        await vm.MasterFilterValuesLoadTask;
        vm.SelectedMasterFilterValue = vm.MasterFilterValues.First(v => v.DisplayText == "Cardiology");
        vm.SaveProfileCommand.Execute(null);

        // Set up deferred pending task that deliberately ignores cancellation to simulate in-flight/late response
        var oldPendingTcs = new TaskCompletionSource<ColumnValuesResult>();
        deferredInspection.PendingGetColumnValuesTcs = oldPendingTcs;

        // Change selected filter column to Code (column 1) to trigger candidate loading
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[1];
        var oldLoadTask = vm.MasterFilterValuesLoadTask;
        Assert.True(vm.IsMasterFilterValuesLoading);

        // Configure profile validator with target profile values
        var preparedFilterValue = new ColumnValueOption("Cardiology", new ColumnFilterValue(ColumnValueKind.Text, "Cardiology"));
        validator.PrepareResult = new ProfileApplicationResult(
            Success: true,
            Errors: [],
            FilterValues: [preparedFilterValue],
            SelectedFilterValue: preparedFilterValue);

        // Apply profile - must cancel old loading and set new candidates atomically
        await vm.ApplyProfileCommand.ExecuteAsync(null);

        Assert.Equal("已应用：取消在途筛选加载测试", vm.ProfileStatusMessage);
        Assert.Equal("Cardiology", vm.SelectedMasterFilterValue?.DisplayText);

        // Complete the OLD pending candidate request with stale data and await it
        var staleOption = new ColumnValueOption("001", new ColumnFilterValue(ColumnValueKind.Text, "001"));
        oldPendingTcs.SetResult(new ColumnValuesResult(true, [staleOption], null));
        await oldLoadTask;

        // Assert that the late response was safely ignored and current candidates/selection remain intact
        Assert.Equal("Cardiology", vm.SelectedMasterFilterValue?.DisplayText);
        Assert.Single(vm.MasterFilterValues);
        Assert.Equal("Cardiology", vm.MasterFilterValues[0].DisplayText);
    }

    [Fact]
    public async Task MatchingProfile_Delete_PreservesPageSettings()
    {
        var store = new InMemoryProfileStore();
        var vm = await CreateMatchingVm(store, prompt: _ => "欲删配置", delete: _ => true);

        vm.SaveProfileCommand.Execute(null);
        Assert.Single(vm.Profiles);

        vm.DeleteProfileCommand.Execute(null);
        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.False(store.MatchingProfiles.ContainsKey("欲删配置"));

        // Current conditions and return fields remain intact
        Assert.Single(vm.Conditions);
        Assert.NotNull(vm.Conditions[0].SelectedMasterColumn);
        Assert.True(vm.ReturnFields[1].IsSelected);
    }

    [Fact]
    public void ProfileStore_KindIsolation_EnsuresFormatAndMatchingDoNotPollute()
    {
        var store = new InMemoryProfileStore();
        store.Save(new ProcessingProfile(1, "通用格式", ProcessingProfileKind.FormatStandardization,
            new FormatProfileSettings(true, false, false, false, false, false), null));

        store.Save(new ProcessingProfile(1, "通用匹配", ProcessingProfileKind.DataMatching,
            null, new MatchingProfileSettings([], [], true, new ProfileStatusSettings(true, "匹配状态"), null)));

        var formatList = store.List(ProcessingProfileKind.FormatStandardization);
        var matchingList = store.List(ProcessingProfileKind.DataMatching);

        Assert.Single(formatList.Profiles);
        Assert.Equal("通用格式", formatList.Profiles[0].Name);

        Assert.Single(matchingList.Profiles);
        Assert.Equal("通用匹配", matchingList.Profiles[0].Name);
    }

    #endregion

    #region In-Memory Test Fakes

    private sealed class InMemoryProfileStore : IProcessingProfileStore
    {
        public Dictionary<string, ProcessingProfile> FormatProfiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ProcessingProfile> MatchingProfiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ErrorsToReturn { get; set; } = [];

        private Dictionary<string, ProcessingProfile> GetDict(ProcessingProfileKind kind) =>
            kind == ProcessingProfileKind.FormatStandardization ? FormatProfiles : MatchingProfiles;

        public ProfileListResult List(ProcessingProfileKind kind) =>
            new(GetDict(kind).Values.ToList(), ErrorsToReturn);

        public ProfileLoadResult Load(ProcessingProfileKind kind, string name)
        {
            var dict = GetDict(kind);
            return dict.TryGetValue(name, out var profile)
                ? new(true, profile, null)
                : new(false, null, "配置不存在");
        }

        public ProfileWriteResult Save(ProcessingProfile profile, bool overwrite = false)
        {
            var dict = GetDict(profile.Kind);
            if (dict.ContainsKey(profile.Name) && !overwrite)
            {
                return new(false, true, "配置已存在");
            }

            dict[profile.Name] = profile;
            return new(true, false, null);
        }

        public ProfileWriteResult Delete(ProcessingProfileKind kind, string name)
        {
            var dict = GetDict(kind);
            dict.Remove(name);
            return new(true, false, null);
        }
    }

    private sealed class FakeProfileValidator : IProcessingProfileValidator
    {
        public ProfileApplicationResult PrepareResult { get; set; } =
            new(true, [], [], null);

        public Func<ProcessingProfile, IReadOnlyList<string>>? CustomValidate { get; set; }

        public Func<ProcessingProfile, WorksheetSource, IReadOnlyList<ColumnReference>, IReadOnlyList<ColumnReference>, CancellationToken, Task<ProfileApplicationResult>>? CustomPrepare { get; set; }

        public IReadOnlyList<string> Validate(ProcessingProfile profile) =>
            CustomValidate != null ? CustomValidate(profile) : [];

        public Task<ProfileApplicationResult> PrepareMatchingAsync(
            ProcessingProfile profile,
            WorksheetSource master,
            IReadOnlyList<ColumnReference> masterColumns,
            IReadOnlyList<ColumnReference> referenceColumns,
            CancellationToken cancellationToken = default)
        {
            if (CustomPrepare != null)
            {
                return CustomPrepare(profile, master, masterColumns, referenceColumns, cancellationToken);
            }
            return Task.FromResult(PrepareResult);
        }
    }

    private sealed class DeferredInspectionService(IWorkbookInspectionService inner) : IWorkbookInspectionService
    {
        public TaskCompletionSource<ColumnValuesResult>? PendingGetColumnValuesTcs { get; set; }

        public Task<WorkbookInspectionResult> InspectAsync(
            WorkbookInspectionRequest request,
            CancellationToken cancellationToken = default) =>
            inner.InspectAsync(request, cancellationToken);

        public Task<WorksheetPreviewResult> GetPreviewAsync(
            WorksheetPreviewRequest request,
            CancellationToken cancellationToken = default) =>
            inner.GetPreviewAsync(request, cancellationToken);

        public Task<ColumnValuesResult> GetColumnValuesAsync(
            ColumnValuesRequest request,
            CancellationToken cancellationToken = default)
        {
            if (PendingGetColumnValuesTcs != null)
            {
                // Deliberately ignore cancellation to simulate late response
                return PendingGetColumnValuesTcs.Task;
            }
            return inner.GetColumnValuesAsync(request, cancellationToken);
        }
    }

    #endregion
}
