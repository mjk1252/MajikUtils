using Velopack;

namespace Dock.App;

/// <summary>
/// The real entry point now, in place of the one WPF's SDK used to generate from App.xaml's
/// <c>StartupUri</c>-free <c>ApplicationDefinition</c>.
///
/// <see cref="VelopackApp"/> has to run before anything else touches the filesystem or takes the
/// <see cref="SingleInstance"/> mutex: a launch that follows an install, an update, or an uninstall
/// carries hidden command-line flags Velopack uses to finish that step (moving shortcuts, cleaning
/// up an old version) and then exits without ever reaching the rest of this method. Anywhere else
/// in the startup path is too late to catch that.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Earlier than App's own handlers, which cannot exist until there is an App -- so a startup
        // that falls over before WPF is up still leaves a trace rather than nothing at all.
        //
        // Subscribing is not "touching the filesystem", so this is allowed to precede the Velopack
        // call: nothing is read or written unless the handler actually fires, and by then the
        // process is ending regardless.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Record("startup", e.ExceptionObject as Exception, fatal: true);

        // Left inline deliberately. Behind a helper this still ran first, but vpk looks for the
        // call in a method named Main and warns on every pack when it is anywhere else -- a
        // standing warning on the release build is worse than what moving it bought, which was
        // only that a missing Velopack.dll would fail late enough to be logged rather than
        // throwing as the JIT resolved Main. That is a packaging mistake, and the release build
        // is where it surfaces anyway.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
