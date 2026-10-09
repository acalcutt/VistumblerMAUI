namespace VistumblerMAUI.Localization;

/// <summary>
/// <c>{loc:T Key}</c>: the text for Key in the chosen language. Works anywhere a string does, including a binding's
/// StringFormat (<c>{Binding Count, StringFormat={loc:T Scan_Total}}</c>). Pages are rebuilt when the language
/// changes (<see cref="LanguageSettings"/>), so the text is looked up once.
/// </summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class TExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
