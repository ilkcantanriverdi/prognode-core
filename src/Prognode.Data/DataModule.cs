namespace Prognode.Data;

/// <summary>
/// Storage provider sınırı.
/// SQLite provider bir sonraki adımda burada başlayacak.
/// Core hiçbir zaman doğrudan SQLite sınıflarına bağımlı olmayacak.
/// </summary>
public static class DataModule
{
    public const string CurrentProvider = "Not configured";
}
