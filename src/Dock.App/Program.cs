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
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
