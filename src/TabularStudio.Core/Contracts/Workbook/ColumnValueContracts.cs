namespace TabularStudio.Core.Contracts;

public enum ColumnValueKind { Text, Number, Boolean, DateTime, TimeSpan, Error }

// Culture-independent raw value; date granularity follows the source cell format.
public sealed record ColumnFilterValue(ColumnValueKind Kind, string RawValue, bool HasTime = false);
public sealed record ColumnValueOption(string DisplayText, ColumnFilterValue Value);
public sealed record ColumnValuesRequest(WorksheetSource Source, ColumnReference Column);
public sealed record ColumnValuesResult(bool Success, IReadOnlyList<ColumnValueOption> Values, OperationError? Error);
