namespace Dock.Core.Services;

public interface IWindowActivator
{
    void Activate(IntPtr handle);
    void ToggleActivate(IntPtr handle);
    void EndTask(IReadOnlyList<IntPtr> handles, IReadOnlyList<int> processIds);
}
