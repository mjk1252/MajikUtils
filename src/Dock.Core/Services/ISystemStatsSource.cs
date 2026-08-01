namespace Dock.Core.Services;

public interface ISystemStatsSource
{
    event EventHandler<(double CpuPercent, double GpuPercent)>? Updated;
    void Start();
    void Stop();
}
