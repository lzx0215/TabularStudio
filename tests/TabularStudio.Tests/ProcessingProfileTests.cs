using System.Text.Json;
using System.Text.Json.Nodes;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class ProcessingProfileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularStudio-profiles-" + Guid.NewGuid());
    private static readonly ColumnReference[] MasterColumns = [new(1, "Key"), new(2, "Department"), new(3, "Other")];
    private static readonly ColumnReference[] ReferenceColumns = [new(1, "Key"), new(2, "Department"), new(3, "Result"), new(4, "Other")];
    private static readonly WorksheetSource NewMaster = new("synthetic-new-master.xlsx", "Current sheet", 3);

    public ProcessingProfileTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);
    private JsonProcessingProfileStore Store() => new(directory);
    private static ProcessingProfile Format(string name = "Daily") => new(1, name, ProcessingProfileKind.FormatStandardization,
        new(true, false, true, false, false, true), null);
    private static ProcessingProfile Matching(string name = "Daily", ProfileFilterSettings? filter = null) => new(1, name,
        ProcessingProfileKind.DataMatching, null,
        new([new(new(2, "Department"), new(2, "Department")), new(new(1, "Key"), new(1, "Key"))],
            [new(3, "Result"), new(2, "Department")], false, new(true, "核对结果"), filter));
    private static ProfileFilterSettings Filter(ColumnValueKind kind = ColumnValueKind.Text, string raw = "A", bool hasTime = false) =>
        new(new(2, "Department"), kind, raw, hasTime);
    private string OnlyProfileFile() => Assert.Single(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
    private static void AssertWrite(ProfileWriteResult result) => Assert.True(result.Success, result.Error);
    private static JsonObject Object(JsonNode node, string property) => Assert.IsType<JsonObject>(node[Property(node, property)]);
    private static string Property(JsonNode node, string property) => Assert.Single(node.AsObject().Select(p => p.Key),
        key => string.Equals(key, property, StringComparison.OrdinalIgnoreCase));
    private static void AssertRejected(ProfileApplicationResult result)
    {
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.FilterValues);
        Assert.Null(result.SelectedFilterValue);
    }

    [Fact]
    public void FormatRoundTripPreservesEveryExplicitSwitchAcrossStoreInstances()
    {
        var profile = Format("  每日报表  ");
        AssertWrite(Store().Save(profile));
        var loaded = Store().Load(profile.Kind, "每日报表");
        Assert.True(loaded.Success, loaded.Error);
        Assert.Equal("每日报表", loaded.Profile!.Name);
        Assert.Equal(profile.Format, loaded.Profile.Format);
        Assert.Null(loaded.Profile.Matching);
        Assert.Empty(Store().List(profile.Kind).Errors);
        Assert.Single(Store().List(profile.Kind).Profiles);
    }

    [Fact]
    public void MatchingRoundTripPreservesOrderedConditionsFieldsAndTypedFilter()
    {
        var profile = Matching(filter: Filter(ColumnValueKind.DateTime, "2026-09-09T12:30:00.0000000", true));
        AssertWrite(Store().Save(profile));
        var loaded = Store().Load(profile.Kind, profile.Name);
        Assert.True(loaded.Success, loaded.Error);
        var actual = loaded.Profile!.Matching!;
        Assert.Equal(profile.Matching!.Conditions, actual.Conditions);
        Assert.Equal(profile.Matching.ReturnFields, actual.ReturnFields);
        Assert.False(actual.NormalizeComparisonKeys);
        Assert.Equal(profile.Matching.StatusColumn, actual.StatusColumn);
        Assert.Equal(profile.Matching.MasterFilter, actual.MasterFilter);
        Assert.Null(loaded.Profile.Format);
    }

    [Fact]
    public void NamesAreTrimmedAndCaseInsensitiveWithinPageButPagesRemainIsolated()
    {
        var original = Format();
        AssertWrite(Store().Save(original));
        var originalPath = OnlyProfileFile();
        var bytes = File.ReadAllBytes(originalPath);
        var changed = original with { Name = "  dAiLy  ", Format = new(false, true, false, false, false, false) };
        var conflict = Store().Save(changed);
        Assert.False(conflict.Success);
        Assert.True(conflict.NameConflict);
        Assert.Equal(bytes, File.ReadAllBytes(originalPath));
        Assert.Equal(original.Format, Store().Load(original.Kind, " DAILY ").Profile!.Format);

        AssertWrite(Store().Save(Matching("DAILY")));
        Assert.Single(Store().List(ProcessingProfileKind.DataMatching).Profiles);
        Assert.Single(Store().List(ProcessingProfileKind.FormatStandardization).Profiles);
        AssertWrite(Store().Save(changed, overwrite: true));
        Assert.Equal(changed.Format, Store().Load(original.Kind, "daily").Profile!.Format);
        AssertWrite(Store().Save(original with { Name = "Another day" }));
        Assert.Equal(2, Store().List(original.Kind).Profiles.Count);
        AssertWrite(Store().Delete(original.Kind, " DAILY "));
        Assert.Single(Store().List(original.Kind).Profiles);
        Assert.True(Store().Load(ProcessingProfileKind.DataMatching, "daily").Success);
    }

    [Fact]
    public void MissingProfileIsExplicitFailureAndListDoesNotCreateBuiltInProfiles()
    {
        Assert.Empty(Store().List(ProcessingProfileKind.FormatStandardization).Profiles);
        Assert.Empty(Store().List(ProcessingProfileKind.DataMatching).Profiles);
        var missing = Store().Load(ProcessingProfileKind.FormatStandardization, "missing");
        Assert.False(missing.Success);
        Assert.Null(missing.Profile);
        Assert.False(string.IsNullOrWhiteSpace(missing.Error));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstUseMissingStoreOrPageDirectoryIsEmptyWithoutErrorOrCreation(bool storeDirectoryExists)
    {
        var storeDirectory = Path.Combine(directory, "first-use");
        if (storeDirectoryExists) Directory.CreateDirectory(storeDirectory);
        var store = new JsonProcessingProfileStore(storeDirectory);
        foreach (var kind in Enum.GetValues<ProcessingProfileKind>())
        {
            var result = store.List(kind);
            Assert.Empty(result.Profiles);
            Assert.Empty(result.Errors);
            Assert.False(Directory.Exists(Path.Combine(storeDirectory, kind.ToString())));
        }
        Assert.Equal(storeDirectoryExists, Directory.Exists(storeDirectory));
    }

    [Theory]
    [InlineData("root", ProcessingProfileKind.FormatStandardization)]
    [InlineData("page", ProcessingProfileKind.FormatStandardization)]
    [InlineData("ancestor", ProcessingProfileKind.FormatStandardization)]
    [InlineData("root", ProcessingProfileKind.DataMatching)]
    [InlineData("page", ProcessingProfileKind.DataMatching)]
    [InlineData("ancestor", ProcessingProfileKind.DataMatching)]
    public void FileBlockingStorageDirectoryIsReportedInsteadOfPretendingListIsEmpty(string position, ProcessingProfileKind kind)
    {
        var storeDirectory = Path.Combine(directory, "store");
        var blocker = storeDirectory;
        if (position == "page")
        {
            Directory.CreateDirectory(storeDirectory);
            blocker = Path.Combine(storeDirectory, kind.ToString());
        }
        else if (position == "ancestor")
        {
            blocker = Path.Combine(directory, "blocked-ancestor");
            storeDirectory = Path.Combine(blocker, "nested", "store");
        }
        File.WriteAllText(blocker, "preserve the blocking file");
        var original = File.ReadAllBytes(blocker);
        var result = new JsonProcessingProfileStore(storeDirectory).List(kind);
        Assert.Empty(result.Profiles);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error => Assert.False(string.IsNullOrWhiteSpace(error)));
        Assert.Equal(original, File.ReadAllBytes(blocker));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void BlankNameIsRejectedWithoutCreatingProfile(string name)
    {
        var result = Store().Save(Format(name));
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("TrimOuterWhitespace")]
    [InlineData("RemoveTabsNewLinesAndHiddenCharacters")]
    [InlineData("NormalizeFullWidthHalfWidth")]
    [InlineData("NormalizeUnicode")]
    [InlineData("NormalizeSafeNumbers")]
    [InlineData("NormalizeUnambiguousDates")]
    public void MissingExplicitFormatSwitchIsRejectedInsteadOfEnablingDefault(string property)
    {
        AssertWrite(Store().Save(Format()));
        var path = OnlyProfileFile();
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var format = Object(json, "Format");
        format.Remove(Property(format, property));
        File.WriteAllText(path, json.ToJsonString());
        var result = Store().Load(ProcessingProfileKind.FormatStandardization, "Daily");
        Assert.False(result.Success);
        Assert.Null(result.Profile);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("version")]
    [InlineData("column")]
    [InlineData("missing-status")]
    [InlineData("missing-normalize")]
    [InlineData("unknown-field")]
    public void DamagedProfileIsRejectedButOtherProfileRemainsUsable(string damage)
    {
        var profile = Matching("Broken");
        AssertWrite(Store().Save(profile));
        var path = OnlyProfileFile();
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        if (damage == "version") json[Property(json, "Version")] = 999;
        if (damage == "column")
        {
            var matching = Object(json, "Matching");
            var field = matching[Property(matching, "ReturnFields")]![0]!;
            field[Property(field, "ColumnNumber")] = 0;
        }
        if (damage is "missing-status" or "missing-normalize")
        {
            var matching = Object(json, "Matching");
            matching.Remove(Property(matching, damage == "missing-status" ? "StatusColumn" : "NormalizeComparisonKeys"));
        }
        if (damage == "unknown-field") json["OutputFilePath"] = "must-not-be-accepted.xlsx";
        File.WriteAllText(path, damage == "malformed" ? "{ corrupt JSON" : json.ToJsonString());
        AssertWrite(Store().Save(Matching("Good")));
        var loaded = Store().Load(profile.Kind, profile.Name);
        Assert.False(loaded.Success);
        Assert.Null(loaded.Profile);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Error));
        var list = Store().List(profile.Kind);
        Assert.Equal("Good", Assert.Single(list.Profiles).Name);
        Assert.NotEmpty(list.Errors);
        Assert.True(Store().Load(profile.Kind, "Good").Success);
    }

    [Fact]
    public void SelectedProfileIsRereadAfterListAndExternalCorruption()
    {
        AssertWrite(Store().Save(Format()));
        Assert.Single(Store().List(ProcessingProfileKind.FormatStandardization).Profiles);
        File.WriteAllText(OnlyProfileFile(), "broken after listing");
        Assert.False(Store().Load(ProcessingProfileKind.FormatStandardization, "Daily").Success);
    }

    [Fact]
    public void LockedOverwriteAndDeleteFailWithoutChangingOldBytesThenRecover()
    {
        var original = Format();
        AssertWrite(Store().Save(original));
        var path = OnlyProfileFile();
        var bytes = File.ReadAllBytes(path);
        var changed = original with { Format = new(false, false, false, true, false, false) };
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var overwrite = Store().Save(changed, overwrite: true);
            Assert.False(overwrite.Success);
            Assert.False(string.IsNullOrWhiteSpace(overwrite.Error));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            var delete = Store().Delete(original.Kind, original.Name);
            Assert.False(delete.Success);
            Assert.False(string.IsNullOrWhiteSpace(delete.Error));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        Assert.Equal(original.Format, Store().Load(original.Kind, original.Name).Profile!.Format);
        AssertWrite(Store().Save(changed, overwrite: true));
        Assert.Equal(changed.Format, Store().Load(original.Kind, original.Name).Profile!.Format);
        AssertWrite(Store().Delete(original.Kind, original.Name));
        Assert.Empty(Store().List(original.Kind).Profiles);
    }

    [Fact]
    public void FirstSaveToBlockedDirectoryReportsFailureAndPreservesBlocker()
    {
        var blockedDirectory = Path.Combine(directory, "file-instead-of-directory");
        File.WriteAllText(blockedDirectory, "keep this file");
        var result = new JsonProcessingProfileStore(blockedDirectory).Save(Format());
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal("keep this file", File.ReadAllText(blockedDirectory));
    }

    [Fact]
    public void OversizedReplacementIsRejectedWithoutDamagingSavedProfile()
    {
        var original = Matching(filter: Filter());
        AssertWrite(Store().Save(original));
        var path = OnlyProfileFile();
        var bytes = File.ReadAllBytes(path);
        var oversized = original with { Matching = original.Matching! with { MasterFilter = Filter(raw: new string('x', 1024 * 1024)) } };
        var result = Store().Save(oversized, overwrite: true);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(original.Matching.MasterFilter, Store().Load(original.Kind, original.Name).Profile!.Matching!.MasterFilter);
    }

    [Fact]
    public void StoredJsonContainsOnlyConfigurationAndTypedSelectedValue()
    {
        AssertWrite(Store().Save(Matching(filter: Filter(ColumnValueKind.Text, "selected literal"))));
        using var json = JsonDocument.Parse(File.ReadAllText(OnlyProfileFile()));
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FilePath", "FileName", "OutputFilePath", "OutputDirectory", "OverwriteExistingOutput", "WorksheetName",
            "HeaderRowNumber", "Preview", "Rows", "FilterValues", "Candidates", "Results", "BatchFiles"
        };
        Inspect(json.RootElement);
        void Inspect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    Assert.DoesNotContain(property.Name, forbidden);
                    Inspect(property.Value);
                }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Inspect(item);
        }
        Assert.Contains("selected literal", File.ReadAllText(OnlyProfileFile()));
    }

    [Fact]
    public void InvalidOptionsAndMixedKindsAreRejectedBeforeSaving()
    {
        var valid = Matching();
        ProcessingProfile[] invalid =
        [
            Format() with { Format = new(false, false, false, false, false, false) },
            Format() with { Version = 2 },
            Format() with { Kind = (ProcessingProfileKind)99 },
            Format() with { Matching = valid.Matching },
            valid with { Matching = valid.Matching! with { Conditions = [] } },
            valid with { Matching = valid.Matching! with { ReturnFields = [] } },
            valid with { Matching = valid.Matching! with { StatusColumn = new(true, " ") } },
            valid with { Matching = valid.Matching! with { ReturnFields = [new(0, "Result")] } },
            valid with { Matching = valid.Matching! with { ReturnFields = [new(3, null)] } },
            valid with { Matching = valid.Matching! with { ReturnFields = [new(3, " ")] } },
            valid with { Matching = valid.Matching! with { MasterFilter = Filter((ColumnValueKind)99) } },
            valid with { Matching = valid.Matching! with { MasterFilter = Filter(ColumnValueKind.Number, "NaN") } }
        ];
        var validator = new ProcessingProfileValidator(new RecordingInspection());
        foreach (var profile in invalid)
        {
            Assert.NotEmpty(validator.Validate(profile));
            Assert.False(Store().Save(profile).Success);
        }
        Assert.Empty(Store().List(valid.Kind).Profiles);
        Assert.Empty(Store().List(ProcessingProfileKind.FormatStandardization).Profiles);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("moved")]
    [InlineData("renamed")]
    [InlineData("blank")]
    [InlineData("case")]
    public async Task ChangedReferencedColumnRejectsWholePreparationBeforeReadingCandidates(string change)
    {
        ColumnReference[] current = change switch
        {
            "missing" => [new(1, "Key")],
            "moved" => [new(1, "Key"), new(2, "Inserted"), new(3, "Department")],
            "renamed" => [new(1, "Key"), new(2, "New department")],
            "blank" => [new(1, "Key"), new(2, "")],
            _ => [new(1, "Key"), new(2, "department")]
        };
        var inspection = new RecordingInspection();
        var profile = Matching(filter: Filter());
        var before = JsonSerializer.Serialize(profile);
        var result = await new ProcessingProfileValidator(inspection).PrepareMatchingAsync(profile, NewMaster, current, ReferenceColumns);
        AssertRejected(result);
        Assert.Contains("Department", string.Join(" ", result.Errors));
        Assert.Empty(inspection.Requests);
        Assert.Equal(before, JsonSerializer.Serialize(profile));
    }

    [Fact]
    public async Task ChangedReturnFieldAndFilterOnlyColumnAreAlsoChecked()
    {
        var inspection = new RecordingInspection();
        var validator = new ProcessingProfileValidator(inspection);
        var changedReference = ReferenceColumns.Select(c => c.ColumnNumber == 3 ? c with { HeaderText = "Renamed" } : c).ToArray();
        AssertRejected(await validator.PrepareMatchingAsync(Matching(), NewMaster, MasterColumns, changedReference));
        var profile = Matching(filter: new(new(3, "Other"), ColumnValueKind.Text, "A", false));
        var changedMaster = MasterColumns.Select(c => c.ColumnNumber == 3 ? c with { HeaderText = "Changed" } : c).ToArray();
        AssertRejected(await validator.PrepareMatchingAsync(profile, NewMaster, changedMaster, ReferenceColumns));
        Assert.Empty(inspection.Requests);
    }

    [Fact]
    public async Task DuplicateHeadersUsePhysicalPositionsAndUnreferencedChangesAreAllowed()
    {
        var profile = Matching() with
        {
            Matching = new([new(new(2, "Same"), new(1, "Same")), new(new(1, "Same"), new(2, "Same"))],
                [new(3, "Result")], true, new(false, ""), null)
        };
        var inspection = new RecordingInspection();
        var result = await new ProcessingProfileValidator(inspection).PrepareMatchingAsync(profile, NewMaster,
            [new(1, "Same"), new(2, "Same"), new(3, "Unused changed")],
            [new(1, "Same"), new(2, "Same"), new(3, "Result"), new(4, null)]);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(result.FilterValues);
        Assert.Null(result.SelectedFilterValue);
        Assert.Empty(inspection.Requests);
        Assert.Equal(2, profile.Matching.Conditions[0].MasterColumn.ColumnNumber);
        Assert.Equal(1, profile.Matching.Conditions[0].ReferenceColumn.ColumnNumber);
    }

    [Fact]
    public async Task FilterUsesFreshCandidatesAndExactTypedIdentityRatherThanDisplayText()
    {
        var text = new ColumnValueOption("123", new(ColumnValueKind.Text, "123"));
        var number = new ColumnValueOption("123", new(ColumnValueKind.Number, "123"));
        var inspection = new RecordingInspection { Result = new(true, [text, number], null) };
        var validator = new ProcessingProfileValidator(inspection);
        var profile = Matching(filter: Filter(ColumnValueKind.Number, "123"));
        var result = await validator.PrepareMatchingAsync(profile, NewMaster, MasterColumns, ReferenceColumns);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Same(number, result.SelectedFilterValue);
        Assert.Equal(new[] { text, number }, result.FilterValues);
        Assert.Equal(new ColumnValuesRequest(NewMaster, new(2, "Department")), Assert.Single(inspection.Requests));
        inspection.Result = new(true, [text], null);
        AssertRejected(await validator.PrepareMatchingAsync(profile, NewMaster, MasterColumns, ReferenceColumns));
        Assert.Equal(2, inspection.Requests.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("type")]
    [InlineData("granularity")]
    [InlineData("failure")]
    public async Task CandidateMismatchOrReadFailureReturnsNoPartialApplication(string failure)
    {
        const string date = "2026-09-09T12:30:00.0000000";
        var profile = Matching(filter: Filter(ColumnValueKind.DateTime, date, true));
        var values = failure switch
        {
            "missing" => new ColumnFilterValue(ColumnValueKind.DateTime, "2026-09-10T12:30:00.0000000", true),
            "type" => new ColumnFilterValue(ColumnValueKind.Text, date, true),
            _ => new ColumnFilterValue(ColumnValueKind.DateTime, date, false)
        };
        var inspection = new RecordingInspection
        {
            Result = failure == "failure"
                ? new(false, [], new(OperationErrorCode.FileLocked, "synthetic candidate file is locked"))
                : new(true, [new("same displayed date", values)], null)
        };
        var before = JsonSerializer.Serialize(profile);
        var result = await new ProcessingProfileValidator(inspection).PrepareMatchingAsync(profile, NewMaster, MasterColumns, ReferenceColumns);
        AssertRejected(result);
        Assert.Single(inspection.Requests);
        Assert.Equal(before, JsonSerializer.Serialize(profile));
    }

    [Fact]
    public async Task CancellationDuringCandidateReadPropagatesWithoutResult()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspection = new RecordingInspection
        {
            Read = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return new(true, [], null);
            }
        };
        var pending = new ProcessingProfileValidator(inspection).PrepareMatchingAsync(Matching(filter: Filter()), NewMaster,
            MasterColumns, ReferenceColumns, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(inspection.Requests);
    }

    [Fact]
    public async Task AlreadyCancelledPreparationDoesNotReadCandidatesEvenWithoutFilter()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var inspection = new RecordingInspection();
        var validator = new ProcessingProfileValidator(inspection);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.PrepareMatchingAsync(Matching(), NewMaster,
            MasterColumns, ReferenceColumns, cancellation.Token));
        Assert.Empty(inspection.Requests);
    }

    [Fact]
    public async Task LateCandidateResponseCannotTurnCancelledPreparationIntoSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ColumnValuesResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspection = new RecordingInspection { Read = _ => { started.SetResult(); return response.Task; } };
        var pending = new ProcessingProfileValidator(inspection).PrepareMatchingAsync(Matching(filter: Filter()), NewMaster,
            MasterColumns, ReferenceColumns, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        response.SetResult(new(true, [new("A", new(ColumnValueKind.Text, "A"))], null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task CandidateIOExceptionBecomesActionableFailure()
    {
        var inspection = new RecordingInspection { Read = _ => throw new IOException("synthetic read failure") };
        var result = await new ProcessingProfileValidator(inspection).PrepareMatchingAsync(Matching(filter: Filter()), NewMaster,
            MasterColumns, ReferenceColumns);
        AssertRejected(result);
        Assert.Contains("synthetic read failure", string.Join(" ", result.Errors));
    }

    [Theory]
    [InlineData(".xlsx", false)]
    [InlineData(".xls", false)]
    [InlineData(".csv", false)]
    [InlineData(".xlsx", true)]
    [InlineData(".xls", true)]
    [InlineData(".csv", true)]
    public async Task ReloadedMatchingProfileMatchesManualOutputAndFiveStatistics(string extension, bool filtered)
    {
        var masterPath = Path.Combine(directory, "new-master" + extension);
        var referencePath = Path.Combine(directory, "new-reference" + extension);
        List<string[]> masterRows = [["Key", "Department", "Other"]];
        masterRows.AddRange(Enumerable.Range(1, 20).Select(i => new[] { "skip" + i, "B", "kept" }));
        masterRows.AddRange([["m", "A", "matched"], ["u", "A", "unmatched"], ["d", "A", "duplicate"],
            ["", "A", "empty"], ["skip", "B", "skipped"]]);
        MultiFormatTests.Write(masterPath, masterRows.ToArray());
        MultiFormatTests.Write(referencePath, [["Key", "Department", "Result", "Other"],
            ["m", "A", "returned", ""], ["d", "A", "first duplicate", ""], ["d", "A", "second duplicate", ""]]);
        var masterBytes = File.ReadAllBytes(masterPath);
        var referenceBytes = File.ReadAllBytes(referencePath);
        WorksheetSource Source(string path) => new(path, extension == ".csv" ? null : "Data", 1);
        var inspection = new WorkbookInspectionService();
        var masterPreview = await inspection.GetPreviewAsync(new(Source(masterPath)));
        var referencePreview = await inspection.GetPreviewAsync(new(Source(referencePath)));
        Assert.True(masterPreview.Success, masterPreview.Error?.ToString());
        Assert.True(referencePreview.Success, referencePreview.Error?.ToString());
        Assert.Equal(20, masterPreview.Preview!.Rows.Count);
        var profile = Matching(filter: filtered ? Filter() : null);
        AssertWrite(Store().Save(profile));
        var loaded = Store().Load(profile.Kind, profile.Name);
        Assert.True(loaded.Success, loaded.Error);
        var prepared = await new ProcessingProfileValidator(inspection).PrepareMatchingAsync(loaded.Profile!, Source(masterPath),
            masterPreview.Preview.Columns, referencePreview.Preview!.Columns);
        Assert.True(prepared.Success, string.Join("; ", prepared.Errors));
        if (filtered)
        {
            Assert.Equal("A", prepared.SelectedFilterValue!.Value.RawValue);
            Assert.Equal(2, prepared.FilterValues.Count);
        }
        var settings = loaded.Profile!.Matching!;
        var restoredRequest = new DataMatchingRequest(Source(masterPath), Source(referencePath), settings.Conditions,
            settings.ReturnFields, settings.NormalizeComparisonKeys,
            new() { Enabled = settings.StatusColumn.Enabled, ColumnName = settings.StatusColumn.ColumnName },
            Path.Combine(directory, "restored" + extension), false)
        {
            MasterFilter = filtered ? new(new(2, "Department"), "A") { SelectedValue = prepared.SelectedFilterValue!.Value } : null
        };
        var manualRequest = new DataMatchingRequest(Source(masterPath), Source(referencePath),
            [new(new(2, "Department"), new(2, "Department")), new(new(1, "Key"), new(1, "Key"))],
            [new(3, "Result"), new(2, "Department")], false, new() { Enabled = true, ColumnName = "核对结果" },
            Path.Combine(directory, "manual" + extension), false)
        {
            MasterFilter = filtered ? new(new(2, "Department"), "A") { SelectedValue = new(ColumnValueKind.Text, "A") } : null
        };
        var service = new DataMatchingService();
        var manual = await service.ExecuteAsync(manualRequest);
        var restored = await service.ExecuteAsync(restoredRequest);
        Assert.True(manual.Success, manual.Error?.ToString());
        Assert.True(restored.Success, restored.Error?.ToString());
        Assert.Equal(manual.Summary! with { Elapsed = TimeSpan.Zero }, restored.Summary! with { Elapsed = TimeSpan.Zero });
        Assert.Equal(25, restored.Summary.TotalMasterDataRowCount);
        Assert.Equal(1, restored.Summary.MatchedCount);
        Assert.Equal(filtered ? 1 : 22, restored.Summary.UnmatchedCount);
        Assert.Equal(1, restored.Summary.DuplicateCount);
        Assert.Equal(1, restored.Summary.EmptyKeyCount);
        Assert.Equal(filtered ? 21 : 0, restored.Summary.SkippedCount);
        Assert.Equal(JsonSerializer.Serialize(MultiFormatTests.Read(manualRequest.OutputFilePath)),
            JsonSerializer.Serialize(MultiFormatTests.Read(restoredRequest.OutputFilePath)));
        Assert.Equal(masterBytes, File.ReadAllBytes(masterPath));
        Assert.Equal(referenceBytes, File.ReadAllBytes(referencePath));
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xls")]
    [InlineData(".csv")]
    public async Task ReloadedFormatProfileMatchesManualRulesAndRetainsInputBytes(string extension)
    {
        var input = Path.Combine(directory, "format-input" + extension);
        MultiFormatTests.Write(input, [["Text", "Identifier", "Long number"],
            ["  ＡＢＣ  ", "00123", "12345678901234567890"], ["  unchanged\tline  ", "0007", "09876543210987654321"]]);
        var original = File.ReadAllBytes(input);
        AssertWrite(Store().Save(Format()));
        var loaded = Store().Load(ProcessingProfileKind.FormatStandardization, "Daily");
        Assert.True(loaded.Success, loaded.Error);
        var settings = loaded.Profile!.Format!;
        var restoredOptions = new FormatStandardizationOptions
        {
            TrimOuterWhitespace = settings.TrimOuterWhitespace,
            RemoveTabsNewLinesAndHiddenCharacters = settings.RemoveTabsNewLinesAndHiddenCharacters,
            NormalizeFullWidthHalfWidth = settings.NormalizeFullWidthHalfWidth,
            NormalizeUnicode = settings.NormalizeUnicode,
            NormalizeSafeNumbers = settings.NormalizeSafeNumbers,
            NormalizeUnambiguousDates = settings.NormalizeUnambiguousDates
        };
        var manualOptions = new FormatStandardizationOptions
        {
            TrimOuterWhitespace = true, RemoveTabsNewLinesAndHiddenCharacters = false,
            NormalizeFullWidthHalfWidth = true, NormalizeUnicode = false,
            NormalizeSafeNumbers = false, NormalizeUnambiguousDates = true
        };
        var source = new WorksheetSource(input, extension == ".csv" ? null : "Data", 1);
        var manualPath = Path.Combine(directory, "manual-format" + extension);
        var restoredPath = Path.Combine(directory, "restored-format" + extension);
        var service = new FormatStandardizationService();
        var manual = await service.ExecuteAsync(new(source, manualPath, manualOptions, false));
        var restored = await service.ExecuteAsync(new(source, restoredPath, restoredOptions, false));
        Assert.True(manual.Success, manual.Error?.ToString());
        Assert.True(restored.Success, restored.Error?.ToString());
        var rows = MultiFormatTests.Read(restoredPath);
        Assert.Equal(JsonSerializer.Serialize(MultiFormatTests.Read(manualPath)), JsonSerializer.Serialize(rows));
        Assert.Equal("00123", rows[1][1]);
        Assert.Equal("12345678901234567890", rows[1][2]);
        Assert.Equal(original, File.ReadAllBytes(input));
    }

    private sealed class RecordingInspection : IWorkbookInspectionService
    {
        public List<ColumnValuesRequest> Requests { get; } = [];
        public ColumnValuesResult Result { get; set; } = new(true, [], null);
        public Func<CancellationToken, Task<ColumnValuesResult>>? Read { get; init; }
        public Task<WorkbookInspectionResult> InspectAsync(WorkbookInspectionRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Profile preparation must use the supplied current preview.");
        public Task<WorksheetPreviewResult> GetPreviewAsync(WorksheetPreviewRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Profile preparation must not replace the current preview.");
        public Task<ColumnValuesResult> GetColumnValuesAsync(ColumnValuesRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Read?.Invoke(cancellationToken) ?? Task.FromResult(Result);
        }
    }
}
