using Timer = System.Timers.Timer;

namespace ConsoleWidgets;

public static class Widgets
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
    
    public static string Format(this double value, int decimals = 8, int padSize = 17, bool showPlus = false)
    {
        return ((decimal?)value).Format(decimals, padSize, showPlus);
    }

    public static string Format(this decimal value, int decimals = 8, int padSize = 17, bool showPlus = false)
    {
        return ((decimal?)value).Format(decimals, padSize, showPlus);
    }
    
    public static string Format(this decimal? value, int decimals = 8, int padSize = 17, bool showPlus = false)
    {
        if (!value.HasValue)
        {
            return "??".PadLeft(padSize);
        }

        return ((showPlus && value.Value >= 0 ? "+" : "") + value.Value.ToString($"N{decimals}")).PadLeft(padSize);
    }

    public static void Write(string s, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(s);
        Console.ResetColor();
    }

    public static void WriteLine(string s, ConsoleColor color)
    {
        Write(s, color);
        Console.WriteLine();
    }

}
public class Spinner
{
    private Timer timer;
    //private string[] frames = new[] { "|", "/", "-", "\\" };
    private string[] frames = new[] { "|", "+", "-", "+" };
    private int currentFrame = 0;
    private int left;
    private int top;

    public Spinner()
    {
        timer = new Timer(TimeSpan.FromMilliseconds(250));
        timer.Elapsed += (sender, e) => Tick();
    }

    public void Start()
    {
        left = Console.CursorLeft;
        top = Console.CursorTop;
        timer.Start();
    }

    public void Stop()
    {
        timer.Stop();
        Console.SetCursorPosition(left, top);
        Console.Write(" ");
        Console.SetCursorPosition(left, top);
    }

    private void Tick()
    {
        int originalLeft = Console.CursorLeft;
        int originalTop = Console.CursorTop;

        Console.SetCursorPosition(left, top);
        Console.Write(frames[currentFrame]);
        Console.SetCursorPosition(originalLeft, originalTop);

        currentFrame = (currentFrame + 1) % frames.Length;
    }
}

public class Menu
{
    private bool enabled = true;
    private readonly Dictionary<ConsoleKey, (string Description, Action Action)> menuOptions;
    private readonly ConsoleKey exitOptionKey;
    private readonly string exitOptionDescription;

    public Menu(ConsoleKey exitOptionKey = ConsoleKey.Q, string exitOptionDescription = "Quit")
    {
        this.exitOptionKey = exitOptionKey;
        this.exitOptionDescription = exitOptionDescription;
        menuOptions = new Dictionary<ConsoleKey, (string, Action)>();
    }
    public void AddOption(ConsoleKey key, string description, Action action)
    {
        if (menuOptions.ContainsKey(key) || exitOptionKey == key)
            throw new ArgumentException($"An option with the key '{key}' already exists.", nameof(key));

        menuOptions[key] = (description, action);
    }

    public void RemoveOptions(Func<KeyValuePair<ConsoleKey, (string Description, Action Action)>, bool> predicate)
    {
        var keysToRemove = menuOptions.Where(predicate).Select(x => x.Key).ToList();
        foreach (var key in keysToRemove)
        {
            menuOptions.Remove(key);
        }
    }

    public void Show()
    {
        foreach (var option in menuOptions)
        {
            var keyDisplay = option.Key >= ConsoleKey.D0 && option.Key <= ConsoleKey.D9 
                ? ((int)option.Key - (int)ConsoleKey.D0).ToString() 
                : option.Key.ToString();
            Console.WriteLine($"[{keyDisplay}] {option.Value.Description}");
        }

        Console.WriteLine($"[{exitOptionKey}] {exitOptionDescription}");
        this.Enable();
    }

    /// <summary>
    /// Starts the menu loop, waits for keypresses, and executes the corresponding action.
    /// </summary>
    public async Task Start()
    {
        while (true)
        {
            if (enabled && Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;

                if (key == exitOptionKey)
                {
                    break;
                }

                if (menuOptions.ContainsKey(key))
                {
                    menuOptions[key].Action.Invoke();
                }
            }

            await Task.Delay(100);
        }
    }
    
    public void Disable()
    {
        enabled = false;
    }
    
    public void Enable()
    {
        enabled = true;
    }
}
