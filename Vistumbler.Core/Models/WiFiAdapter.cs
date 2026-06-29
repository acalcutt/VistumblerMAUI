namespace Vistumbler.Core.Models;

/// <summary>
/// Represents a discovered WiFi adapter/interface.
/// </summary>
public class WiFiAdapter
{
    public string Id   { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public override string ToString() => Name;
}
