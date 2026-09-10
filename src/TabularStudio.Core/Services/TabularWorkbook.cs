using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace TabularStudio.Core.Services;

// ClosedXML supplies the existing value model, not an intermediate XLSX file.
// XLS saves apply only changed values to the original HSSF workbook.
internal sealed class TabularWorkbook : IDisposable
{
    private readonly XLWorkbook model;
    private readonly HSSFWorkbook? binary;
    private readonly Dictionary<(string, int, int), (XLCellValue Value, string Format)> original = new();
    private readonly List<int> csvWidths = [];
    private readonly bool csv;
    private readonly bool bom;
    public IXLWorksheets Worksheets => model.Worksheets;
    public bool IsCsv => csv;
    public int MaxColumns => binary is null ? XLHelper.MaxColumnNumber : 256;

    public TabularWorkbook(Stream stream, string path, CancellationToken token = default)
    {
        csv = WorkbookFileOperations.IsCsvPath(path);
        if (WorkbookFileOperations.IsXlsxPath(path)) { model = new XLWorkbook(stream); return; }
        model = new XLWorkbook();
        try
        {
            if (csv)
            {
                Span<byte> prefix = stackalloc byte[3];
                var count = stream.Read(prefix);
                bom = count == 3 && prefix.SequenceEqual(new byte[] { 239, 187, 191 });
                stream.Position = bom ? 3 : 0;
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, leaveOpen: true);
                using var parser = new CsvParser(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false, Delimiter = ",", IgnoreBlankLines = false,
                    TrimOptions = TrimOptions.None, Mode = CsvMode.RFC4180,
                    ExceptionMessagesContainRawData = false
                });
                var sheet = model.AddWorksheet("Data");
                while (parser.Read())
                {
                    token.ThrowIfCancellationRequested();
                    var fields = parser.Record!;
                    csvWidths.Add(fields.Length);
                    if (csvWidths.Count > XLHelper.MaxRowNumber || fields.Length > MaxColumns)
                        throw new InvalidDataException("CSV exceeds supported table dimensions.");
                    for (var i = 0; i < fields.Length; i++) sheet.Cell(csvWidths.Count, i + 1).Value = fields[i];
                }
                return;
            }
            binary = new HSSFWorkbook(stream);
            for (var s = 0; s < binary.NumberOfSheets; s++)
            {
                token.ThrowIfCancellationRequested();
                var source = binary.GetSheetAt(s);
                var target = model.AddWorksheet(source.SheetName);
                foreach (IRow row in source)
                foreach (var cell in row.Cells)
                {
                    token.ThrowIfCancellationRequested();
                    var dest = target.Cell(row.RowNum + 1, cell.ColumnIndex + 1);
                    var format = cell.CellStyle.GetDataFormatString() ?? "General";
                    dest.Style.NumberFormat.Format = format;
                    switch (cell.CellType)
                    {
                        case CellType.String: dest.Value = cell.StringCellValue; break;
                        case CellType.Numeric:
                            if (DateUtil.IsCellDateFormatted(cell)) dest.Value = cell.DateCellValue!.Value;
                            else dest.Value = cell.NumericCellValue;
                            break;
                        case CellType.Boolean: dest.Value = cell.BooleanCellValue; break;
                        case CellType.Error: dest.Value = FromExcelError(cell.ErrorCellValue); break;
                        // Retain expression; never evaluate. Original HSSF formula is never rewritten.
                        case CellType.Formula: dest.FormulaA1 = cell.CellFormula; break;
                    }
                    if (!dest.HasFormula) original[(source.SheetName, row.RowNum + 1, cell.ColumnIndex + 1)] = (dest.Value, format);
                }
            }
        }
        catch { binary?.Dispose(); model.Dispose(); throw; }
    }

    public IXLWorksheet? FindSheet(string? name) => csv ? model.Worksheets.First() :
        model.Worksheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    public IEnumerable<IXLCell> ContentCells(IXLWorksheet sheet)
    {
        if (!csv) return sheet.CellsUsed(XLCellsUsedOptions.Contents);
        return csvWidths.SelectMany((width, row) => Enumerable.Range(1, width).Select(col => sheet.Cell(row + 1, col)));
    }

    public string? Display(IXLCell cell)
    {
        if (binary is not null && cell.HasFormula)
        {
            var source = binary.GetSheet(cell.Worksheet.Name).GetRow(cell.Address.RowNumber - 1).GetCell(cell.Address.ColumnNumber - 1);
            return source.CachedFormulaResultType switch
            {
                CellType.String => source.StringCellValue,
                CellType.Numeric => source.NumericCellValue.ToString(CultureInfo.CurrentCulture),
                CellType.Boolean => source.BooleanCellValue.ToString(),
                _ => null
            };
        }
        return cell.IsEmpty(XLCellsUsedOptions.Contents) ? null : cell.HasFormula
            ? cell.CachedValue.ToString(CultureInfo.CurrentCulture) : cell.GetFormattedString(CultureInfo.CurrentCulture);
    }

    // Comparison reads persisted formula results only; it must never trigger evaluation.
    public XLCellValue ComparisonValue(IXLCell cell)
    {
        if (!cell.HasFormula) return cell.Value;
        if (binary is not null)
        {
            var source = binary.GetSheet(cell.Worksheet.Name).GetRow(cell.Address.RowNumber - 1)
                .GetCell(cell.Address.ColumnNumber - 1);
            return source.CachedFormulaResultType switch
            {
                CellType.String => source.StringCellValue,
                CellType.Numeric => source.NumericCellValue,
                CellType.Boolean => source.BooleanCellValue,
                CellType.Error => FromExcelError(source.ErrorCellValue),
                _ => throw MissingFormulaResult(cell)
            };
        }
        var cached = cell.CachedValue;
        if (cached.Type == XLDataType.Blank) throw MissingFormulaResult(cell);
        return cached;
    }

    private static InvalidDataException MissingFormulaResult(IXLCell cell) => new(
        $"工作表 {cell.Worksheet.Name} 的 {cell.Address} 公式缺少已保存的计算结果，请在 Excel/WPS 中重新计算并保存后再比较。");

    public void SaveAs(Stream output, CancellationToken token = default)
    {
        if (csv)
        {
            using var writer = new StreamWriter(output, new UTF8Encoding(bom), leaveOpen: true);
            using var csvWriter = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            { Delimiter = ",", NewLine = "\r\n", HasHeaderRecord = false });
            var sheet = model.Worksheets.First();
            for (var row = 1; row <= csvWidths.Count; row++)
            {
                token.ThrowIfCancellationRequested();
                var last = Math.Max(csvWidths[row - 1], sheet.Row(row).LastCellUsed()?.Address.ColumnNumber ?? 0);
                for (var col = 1; col <= last; col++)
                {
                    var cell = sheet.Cell(row, col);
                    csvWriter.WriteField(cell.DataType == XLDataType.Text ? cell.GetString() : cell.GetFormattedString(CultureInfo.InvariantCulture));
                }
                csvWriter.NextRecord();
            }
            return;
        }
        if (binary is null) { model.SaveAs(output); return; }
        var styles = new Dictionary<(short, string), ICellStyle>();
        foreach (var sheet in model.Worksheets)
        foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.All))
        {
            token.ThrowIfCancellationRequested();
            if (cell.HasFormula) continue;
            var key = (sheet.Name, cell.Address.RowNumber, cell.Address.ColumnNumber);
            var format = cell.Style.NumberFormat.Format;
            if (original.TryGetValue(key, out var old) && old.Value.Equals(cell.Value) && old.Format == format) continue;
            var targetSheet = binary.GetSheet(sheet.Name);
            var row = targetSheet.GetRow(key.Item2 - 1) ?? targetSheet.CreateRow(key.Item2 - 1);
            var dest = row.GetCell(key.Item3 - 1) ?? row.CreateCell(key.Item3 - 1);
            switch (cell.DataType)
            {
                case XLDataType.Blank: dest.SetCellType(CellType.Blank); break;
                case XLDataType.Text: dest.SetCellValue(cell.GetString()); break;
                case XLDataType.Number: dest.SetCellValue(cell.GetDouble()); break;
                case XLDataType.Boolean: dest.SetCellValue(cell.GetBoolean()); break;
                case XLDataType.DateTime: dest.SetCellValue(cell.GetDateTime()); break;
                case XLDataType.TimeSpan: dest.SetCellValue(cell.GetTimeSpan().TotalDays); break;
                case XLDataType.Error: dest.SetCellErrorValue(ToExcelError(cell.Value.GetError())); break;
            }
            if (dest.CellStyle.GetDataFormatString() != format)
            {
                var styleKey = (dest.CellStyle.Index, format);
                if (!styles.TryGetValue(styleKey, out var style))
                {
                    style = binary.CreateCellStyle(); style.CloneStyleFrom(dest.CellStyle);
                    style.DataFormat = binary.CreateDataFormat().GetFormat(format); styles.Add(styleKey, style);
                }
                dest.CellStyle = style;
            }
        }
        binary.Write(output, true);
    }

    private static XLError FromExcelError(byte value) => value switch
    {
        0 => XLError.NullValue, 7 => XLError.DivisionByZero, 15 => XLError.IncompatibleValue,
        23 => XLError.CellReference, 29 => XLError.NameNotRecognized, 36 => XLError.NumberInvalid,
        42 => XLError.NoValueAvailable, _ => throw new InvalidDataException("Unknown XLS error code.")
    };
    private static byte ToExcelError(XLError value) => value switch
    {
        XLError.NullValue => 0, XLError.DivisionByZero => 7, XLError.IncompatibleValue => 15,
        XLError.CellReference => 23, XLError.NameNotRecognized => 29, XLError.NumberInvalid => 36,
        XLError.NoValueAvailable => 42, _ => throw new InvalidDataException("Unknown cell error.")
    };

    public void Dispose() { binary?.Dispose(); model.Dispose(); }
}
