using System.Globalization;
using System.Text;
using TabularStudio.Core.Contracts;

namespace TabularStudio.Core.Services;

// Shared approved value rules; extraction preserves format standardization behavior.
internal static class ApprovedValueNormalization
{
    private const int ExcelMaximumSafeSignificantDigits = 15;
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly char[] ApprovedOuterWhitespace = [' ', '\u00A0', '\u3000'];
    internal static string NormalizeText(string value, FormatStandardizationOptions options)
    {
        var normalized = value;

        if (options.NormalizeUnicode)
        {
            normalized = normalized.Normalize(NormalizationForm.FormKC);
        }

        if (options.NormalizeFullWidthHalfWidth)
        {
            normalized = ConvertApprovedFullWidthCharacters(normalized);
        }

        if (options.RemoveTabsNewLinesAndHiddenCharacters)
        {
            normalized = RemoveApprovedHiddenCharacters(normalized);
        }

        if (options.TrimOuterWhitespace)
        {
            normalized = normalized.Trim(ApprovedOuterWhitespace);
        }

        return normalized;
    }

    private static string ConvertApprovedFullWidthCharacters(string value)
    {
        StringBuilder? builder = null;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var converted = character switch
            {
                >= '\uFF01' and <= '\uFF5E' => (char)(character - 0xFEE0),
                '\u3000' => ' ',
                _ => character
            };

            if (converted == character && builder is null)
            {
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, index);
            builder.Append(converted);
        }

        return builder?.ToString() ?? value;
    }

    private static string RemoveApprovedHiddenCharacters(string value)
    {
        StringBuilder? builder = null;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var shouldRemove = character <= '\u001F'
                || character == '\u007F'
                || character == '\u200B'
                || character == '\uFEFF';

            if (!shouldRemove && builder is null)
            {
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, index);
            if (!shouldRemove)
            {
                builder.Append(character);
            }
        }

        return builder?.ToString() ?? value;
    }

    internal static bool TryGetSafeExcelNumber(string value, out double number)
    {
        number = default;
        if (value.Length == 0)
        {
            return false;
        }

        var startIndex = value[0] == '-' ? 1 : 0;
        if (startIndex == value.Length || value[0] == '+')
        {
            return false;
        }

        var decimalPointIndex = -1;
        for (var index = startIndex; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '.')
            {
                if (decimalPointIndex >= 0)
                {
                    return false;
                }

                decimalPointIndex = index;
                continue;
            }

            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        var integerEndIndex = decimalPointIndex >= 0 ? decimalPointIndex : value.Length;
        var integerDigitCount = integerEndIndex - startIndex;
        if (integerDigitCount == 0
            || (decimalPointIndex >= 0 && decimalPointIndex == value.Length - 1))
        {
            return false;
        }

        if (integerDigitCount > 1 && value[startIndex] == '0')
        {
            return false;
        }

        var significantDigitCount = CountSignificantDigits(value, startIndex);
        if (significantDigitCount > ExcelMaximumSafeSignificantDigits)
        {
            return false;
        }

        if (!decimal.TryParse(
                value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var decimalValue))
        {
            return false;
        }

        if (decimalValue == decimal.Zero && value[0] == '-')
        {
            return false;
        }

        var doubleValue = (double)decimalValue;
        if (!double.IsFinite(doubleValue))
        {
            return false;
        }

        var roundTripText = doubleValue.ToString("G15", CultureInfo.InvariantCulture);
        if (!decimal.TryParse(
                roundTripText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var roundTripValue)
            || roundTripValue != decimalValue)
        {
            return false;
        }

        number = doubleValue;
        return true;
    }

    private static int CountSignificantDigits(string value, int startIndex)
    {
        var firstNonZeroFound = false;
        var count = 0;

        for (var index = startIndex; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '.')
            {
                continue;
            }

            if (!firstNonZeroFound && character == '0')
            {
                continue;
            }

            firstNonZeroFound = true;
            count++;
        }

        return count == 0 ? 1 : count;
    }

    internal static bool TryGetApprovedDate(
        string value,
        out DateTime date,
        out bool hasTime)
    {
        if (DateTime.TryParseExact(
                value,
                [DateTimeFormat.Replace('-', '/'), DateTimeFormat],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            hasTime = true;
            return true;
        }

        if (DateTime.TryParseExact(
                value,
                [DateFormat.Replace('-', '/'), DateFormat],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            hasTime = false;
            return true;
        }

        hasTime = false;
        return false;
    }

}
