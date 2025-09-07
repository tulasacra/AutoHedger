using System.Text;

namespace AutoHedger;

public static class TxLog
{
    private const string _logFile = "_transaction_log.csv";
    
    public static void Log(string header, params object[] items)
    {
        if (!File.Exists(_logFile))
        {
            File.WriteAllText(_logFile, header + Environment.NewLine);
        }

        StringBuilder logBuilder = new(300);
        for (int i = 0; i < items.Length; i++)
        {
            logBuilder.Append(items[i]);
            if (i < items.Length - 1)
                logBuilder.Append('\t');
        }
        File.AppendAllText(_logFile, logBuilder.ToString() + Environment.NewLine);
    }
}