using Dock.Core.Models;

namespace Dock.Core.Services;

public interface IWingetService
{
    IReadOnlyList<WingetResult> Search(string query);
    void Install(WingetResult result);
}
