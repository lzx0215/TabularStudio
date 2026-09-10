using System.Text.Json.Serialization;

namespace TabularStudio.Core.Contracts;

public enum ProcessingProfileKind { FormatStandardization, DataMatching }

public sealed record FormatProfileSettings(
    bool TrimOuterWhitespace,
    bool RemoveTabsNewLinesAndHiddenCharacters,
    bool NormalizeFullWidthHalfWidth,
    bool NormalizeUnicode,
    bool NormalizeSafeNumbers,
    bool NormalizeUnambiguousDates)
{
    [JsonIgnore]
    public bool HasAnyRule => TrimOuterWhitespace || RemoveTabsNewLinesAndHiddenCharacters ||
        NormalizeFullWidthHalfWidth || NormalizeUnicode || NormalizeSafeNumbers || NormalizeUnambiguousDates;
}

public sealed record ProfileStatusSettings(bool Enabled, string ColumnName);

public sealed record ProfileFilterSettings(ColumnReference Column, ColumnValueKind Kind, string RawValue, bool HasTime)
{
    public ColumnFilterValue ToValue() => new(Kind, RawValue, HasTime);
}

public sealed record MatchingProfileSettings(
    IReadOnlyList<MatchingCondition> Conditions,
    IReadOnlyList<ColumnReference> ReturnFields,
    bool NormalizeComparisonKeys,
    ProfileStatusSettings StatusColumn,
    ProfileFilterSettings? MasterFilter);

// Version 1 intentionally contains no file paths, worksheets, header rows or output permissions.
public sealed record ProcessingProfile(
    int Version,
    string Name,
    ProcessingProfileKind Kind,
    FormatProfileSettings? Format,
    MatchingProfileSettings? Matching);

public sealed record ProfileListResult(IReadOnlyList<ProcessingProfile> Profiles, IReadOnlyList<string> Errors);
public sealed record ProfileLoadResult(bool Success, ProcessingProfile? Profile, string? Error);
public sealed record ProfileWriteResult(bool Success, bool NameConflict, string? Error);
public sealed record ProfileApplicationResult(bool Success, IReadOnlyList<string> Errors,
    IReadOnlyList<ColumnValueOption> FilterValues, ColumnValueOption? SelectedFilterValue);

public interface IProcessingProfileStore
{
    ProfileListResult List(ProcessingProfileKind kind);
    ProfileLoadResult Load(ProcessingProfileKind kind, string name);
    ProfileWriteResult Save(ProcessingProfile profile, bool overwrite = false);
    ProfileWriteResult Delete(ProcessingProfileKind kind, string name);
}

public interface IProcessingProfileValidator
{
    IReadOnlyList<string> Validate(ProcessingProfile profile);
    Task<ProfileApplicationResult> PrepareMatchingAsync(ProcessingProfile profile, WorksheetSource master,
        IReadOnlyList<ColumnReference> masterColumns, IReadOnlyList<ColumnReference> referenceColumns,
        CancellationToken cancellationToken = default);
}
