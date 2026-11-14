using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// A Wi-Fi adapter or cellular modem in Settings, ticked to scan with it, as each is in the original's (and
/// VistumblerCS's) Interface menu. <paramref name="changed"/> is told when the box is ticked or unticked.
/// </summary>
public sealed partial class InterfaceChoice(string id, string name, bool isChecked, Action<InterfaceChoice> changed) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;

    [ObservableProperty] private bool _isChecked = isChecked;

    /// <summary>Puts the box back without telling anyone, when the change it was ticked for isn't allowed.</summary>
    internal void Reset(bool value)
    {
        _resetting = true;
        try { IsChecked = value; }
        finally { _resetting = false; }
    }

    private bool _resetting;

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_resetting) changed(this);
    }

    /// <summary>Tapping the name ticks or unticks the box.</summary>
    [RelayCommand]
    private void Toggle() => IsChecked = !IsChecked;
}
