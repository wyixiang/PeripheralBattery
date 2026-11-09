using System.Text.Json;
namespace MouseBattery;
internal sealed class Preferences
{
    public int Offset { get; set; }
    public bool Locked { get; set; }
    public bool Visible { get; set; }=true;
    public string Theme { get; set; }="system";
    public string? MonitorIdentity { get; set; }
    public string? MonitorDevice { get; set; }
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MouseBattery");
    internal static string FilePath => Path.Combine(Folder,"preferences.json");
    internal static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath))??new(); }
        catch { return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath,JsonSerializer.Serialize(this,new JsonSerializerOptions { WriteIndented=true }));
    }
}
