namespace Dock.Core.Services;

public interface IAppLauncher
{
    void Launch(string path, string? arguments = null);
}
