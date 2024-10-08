namespace AutoHedger;

public static class ConsoleWidgets
{
    public static void DisplayTable(List<List<string>> rows, bool firstRowIsHeaders = true, bool borders = true)
    {
        if (rows == null || rows.Count == 0)
            return;

        int[] columnWidths = new int[rows[0].Count];
        for (int i = 0; i < rows[0].Count; i++)
        {
            columnWidths[i] = rows.Max(row => row[i].Length);
        }

        string separator = "|" + string.Join("|", columnWidths.Select(w => new string('-', w + 2))) + "|";

        if (borders)
            Console.WriteLine(separator);

        for (int i = 0; i < rows.Count; i++)
        {
            List<string> row = rows[i];
            string line = "|";

            for (int j = 0; j < row.Count; j++)
            {
                line += $" {row[j].PadLeft(columnWidths[j])} |";
            }

            Console.WriteLine(line);

            if (i == 0 && firstRowIsHeaders)
                Console.WriteLine(separator);
        }

        if (borders)
            Console.WriteLine(separator);
    }
    
    public static string Format(this decimal? value, int decimals = 8, int padSize = 17)
    {
        if (!value.HasValue)
        {
            return "??";
        }

        return value.Value.ToString($"N{decimals}").PadLeft(padSize);
    }
}