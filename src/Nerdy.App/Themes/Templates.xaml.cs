using Microsoft.UI.Xaml;

namespace Nerdy.App.Themes;

/// <summary>Shared data templates (they use x:Bind, which needs a code-behind class).</summary>
public sealed partial class Templates : ResourceDictionary
{
    public Templates() => InitializeComponent();
}
