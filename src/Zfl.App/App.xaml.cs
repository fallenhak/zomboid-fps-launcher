using System.Windows;
using Zfl.Core;

namespace Zfl.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Loc.Lang = Loc.Resolve(LauncherState.Load(LauncherState.DefaultPath).Language);
        Log.Write($"--- launcher started, {Environment.OSVersion}");
        DispatcherUnhandledException += (_, a) => Log.Error("ui", a.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Write($"ERROR fatal: {a.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, a) => Log.Error("task", a.Exception);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Write("launcher closed");
        base.OnExit(e);
    }
}
