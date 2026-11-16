using System.Windows.Markup;

namespace UtilitiesManager;

/// <summary>
/// Texte traduit dans un XAML : <c>{local:Tr 'Installed version'}</c>. Le texte anglais
/// sert de clé (<see cref="L.T(string)"/>), lu à la création de la fenêtre.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(text);
}
