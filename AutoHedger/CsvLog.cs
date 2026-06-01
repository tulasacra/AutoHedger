using System.Text;

namespace AutoHedger;

public class CsvLog
{
    private readonly string _logFile;
    private readonly List<object> _header;
    private readonly char _separator;

    public CsvLog(string logFile, List<object> header, char separator)
    {
        _logFile = logFile;
        _header = header;
        _separator = separator;
    }

    public void Log(List<object> items)
    {
        if (!File.Exists(_logFile))
        {
            File.WriteAllText(_logFile, BuildLine(_header) + Environment.NewLine);
        }

        File.AppendAllText(_logFile, BuildLine(items) + Environment.NewLine);
    }

    private string BuildLine(List<object> items)
    {
        StringBuilder logBuilder = new(300);
        for (int i = 0; i < items.Count; i++)
        {
            string value = items[i]?.ToString() ?? "";
            value = value.Replace('\r', ' ').Replace('\n', ' ').Replace(_separator, ' ');
            logBuilder.Append(value);
            if (i < items.Count - 1)
                logBuilder.Append(_separator);
        }
        return logBuilder.ToString();
    }
}