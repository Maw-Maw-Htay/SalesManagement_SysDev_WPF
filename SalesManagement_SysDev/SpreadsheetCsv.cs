namespace SalesManagement_SysDev;

internal static class SpreadsheetCsv
{
    public static string EscapeText(string value)
    {
        // Excel can interpret leading operators as formulas even within a quoted CSV field.
        var start = 0;
        while (start < value.Length && (char.IsWhiteSpace(value[start]) || value[start] == '\uFEFF')) start++;
        var leadingControl = value.Length > 0 && value[0] is '\t' or '\r' or '\n';
        if (leadingControl || (start < value.Length && value[start] is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
