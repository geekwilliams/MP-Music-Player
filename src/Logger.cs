public static class Logger
{
    public static void LogMessage(string message, string level)
    {
        DateTime date = DateTime.Now;
        if (level == "Error")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ {date.ToString("yyyy-MM-dd HH:mm:ss")} ]  " + message);
            Console.ResetColor();
        }
        else if (level == "Warning")
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[ {date.ToString("yyyy-MM-dd HH:mm:ss")} ]  " + message);
            Console.ResetColor();
        }
        else if (level.ToString() == "Info")
        {
            Console.WriteLine($"[ {date.ToString("yyyy-MM-dd HH:mm:ss")} ]  " + message);
        }
    }
}

public static class Log
{
    public static string Error = "Error";
    public static string Info = "Info";
    public static string Warning = "Warning";
}