namespace VistumblerMAUI.Services;

/// <summary>
/// The web pages the hamburger menu's Links… offers, after the original Vistumbler's Help, WifiDB and Support
/// Vistumbler menus. The WifiDB pages follow the site in Settings → WifiDB.
/// </summary>
public static class AppLinks
{
    /// <summary>
    /// Donate and Store are left out of the Google Play build, since Play doesn't allow apps it distributes to
    /// link to payments outside Google Play.
    /// </summary>
#if PLAY_STORE
    public const bool ShowSupportLinks = false;
#else
    public const bool ShowSupportLinks = true;
#endif

    public static IReadOnlyList<(string Title, string Url)> All
    {
        get
        {
            var wifiDb = WifiDbSettings.Url.TrimEnd('/') + "/";
            var links = new List<(string, string)>
            {
                ("Vistumbler website",          "https://www.vistumbler.net/"),
                ("Vistumbler wiki",             "https://gitlab.techidiots.net/techidiots-llc/Vistumbler/-/wikis/home"),
                ("Vistumbler forum",            "https://forum.vistumbler.net/"),
                ("VistumblerMAUI source code",  "https://gitlab.techidiots.net/techidiots-llc/VistumblerMAUI"),
                ("WifiDB website",              wifiDb),
                ("WifiDB live APs",             wifiDb + "opt/live.php"),
            };
            if (ShowSupportLinks)
            {
                links.Add(("Donate to Vistumbler", "https://donate.vistumbler.net/"));
                links.Add(("Vistumbler store",     "https://store.vistumbler.net/"));
            }
            return links;
        }
    }
}
