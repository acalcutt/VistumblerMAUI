using CommunityToolkit.Mvvm.ComponentModel;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// One row in the Map-colors settings table: a "bucket" (the live scan's active/dead
/// pseudo-buckets, or a WifiDB history age bucket) and its Open / WEP / Secure circle
/// colors as 6-char hex strings (no '#'). Edits persist immediately via
/// <see cref="MapColors"/>, matching how the other MAUI settings save on change.
/// </summary>
public partial class MapBucketColorRow : ObservableObject
{
    public string BucketKey   { get; }
    public string DisplayName { get; }

    [ObservableProperty] private string _openHex;
    [ObservableProperty] private string _wepHex;
    [ObservableProperty] private string _secureHex;

    public MapBucketColorRow(string bucketKey, string displayName)
    {
        BucketKey   = bucketKey;
        DisplayName = displayName;
        _openHex    = MapColors.Get(bucketKey, "Open");
        _wepHex     = MapColors.Get(bucketKey, "Wep");
        _secureHex  = MapColors.Get(bucketKey, "Secure");
    }

    partial void OnOpenHexChanged(string value)   => MapColors.Set(BucketKey, "Open",   value);
    partial void OnWepHexChanged(string value)    => MapColors.Set(BucketKey, "Wep",    value);
    partial void OnSecureHexChanged(string value) => MapColors.Set(BucketKey, "Secure", value);
}
