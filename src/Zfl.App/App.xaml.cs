using System.Windows;
using Zfl.Core;

namespace Zfl.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Loc.Lang = Loc.Resolve(LauncherState.Load(LauncherState.DefaultPath).Language);
        base.OnStartup(e);
    }
}
