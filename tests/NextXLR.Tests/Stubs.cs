namespace NextXLR.Services;

public class TrayIconService
{
    public virtual void ShowNotification(string title, string message) { }
}

public static class StorageLoggerService
{
    public static void Log(string target, string value, string trigger = "Manuell", string? filePath = null) { }
}
