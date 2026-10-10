using System.Windows.Markup;
using Zfl.Core;

namespace Zfl.App;

/// XAML'de çeviri: Text="{l:T tab.play}". Dil değişince pencere yeniden kurulur.
public sealed class TExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider sp) => Loc.T(key);
}
