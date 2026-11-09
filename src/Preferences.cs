using System.Text.Json;
namespace PeripheralBattery;
internal sealed class Preferences
{
    public int Offset { get; set; }
    public bool Locked { get; set; }
    public bool Visible { get; set; }=true;
    public string Theme { get; set; }="system";
    public string? MonitorIdentity { get; set; }
    public string? MonitorDevice { get; set; }
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PeripheralBattery");
    internal static string FilePath => Path.Combine(Folder,"preferences.json");
    internal static Preferences Load()
    {
        var source=FilePath;
        if(!File.Exists(source))
        {
            var legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MouseBattery","preferences.json");
            if(File.Exists(legacy))
            {
                source=legacy;
                try { Directory.CreateDirectory(Folder); File.Copy(legacy,FilePath,false); source=FilePath; }
                catch(IOException) { }
                catch(UnauthorizedAccessException) { }
            }
        }
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(source))??new(); }
        catch { return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath,JsonSerializer.Serialize(this,new JsonSerializerOptions { WriteIndented=true }));
    }
}
