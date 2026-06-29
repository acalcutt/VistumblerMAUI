using VistumblerMAUI.Views;

namespace VistumblerMAUI;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Import/Export are reached via navigation (from Settings), not the tab bar.
        Routing.RegisterRoute(nameof(ImportPage), typeof(ImportPage));
        Routing.RegisterRoute(nameof(ExportPage), typeof(ExportPage));
    }
}
