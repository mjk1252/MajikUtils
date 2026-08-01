using System.Diagnostics;
using Dock.Core.Services;

namespace Dock.Interop.Windowing;

public sealed class SystemStatsSource : ISystemStatsSource, IDisposable
{
    private readonly Timer _timer;
    private readonly PerformanceCounter _cpuCounter = new("Processor", "% Processor Time", "_Total");
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly PerformanceCounterCategory? _gpuCategory;

    public event EventHandler<(double CpuPercent, double GpuPercent)>? Updated;

    public SystemStatsSource()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);

        // Rate counters like these read 0 on the very first sample -- they need a preceding
        // sample to measure a delta against. Priming here means the first real Poll() tick
        // already returns a meaningful value instead of a misleading 0%.
        SafeNextValue(_cpuCounter);

        try
        {
            _gpuCategory = new PerformanceCounterCategory("GPU Engine");
        }
        catch (InvalidOperationException)
        {
            // Not available (e.g. no GPU scheduler support, or running under RDP/a VM without
            // one) -- GPU reporting just stays at 0 rather than throwing.
            _gpuCategory = null;
        }
    }

    public void Start() => _timer.Change(0, 1500);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private void Poll()
    {
        var cpu = SafeNextValue(_cpuCounter);
        var gpu = PollGpu();
        Updated?.Invoke(this, (cpu, gpu));
    }

    private double PollGpu()
    {
        if (_gpuCategory is null)
            return 0;

        string[] instanceNames;
        try
        {
            instanceNames = _gpuCategory.GetInstanceNames();
        }
        catch (InvalidOperationException)
        {
            return 0;
        }

        // Windows exposes one "GPU Engine" instance per (process, adapter, engine type) --
        // there's no single overall "_Total" instance like the CPU counter has. Summing the 3D
        // engine instances is the same approximation most third-party GPU-usage widgets use and
        // tracks Task Manager's headline "GPU" percentage closely enough for a glanceable stat.
        var engineInstances = new HashSet<string>(
            instanceNames.Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var stale in _gpuCounters.Keys.Except(engineInstances).ToList())
        {
            _gpuCounters[stale].Dispose();
            _gpuCounters.Remove(stale);
        }

        double total = 0;
        foreach (var name in engineInstances)
        {
            if (!_gpuCounters.TryGetValue(name, out var counter))
            {
                try
                {
                    counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true);
                    SafeNextValue(counter);
                    _gpuCounters[name] = counter;
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
                {
                    // The instance can disappear between GetInstanceNames() and here if its
                    // owning process just exited -- skip it for this tick.
                }

                // Freshly primed counters need a subsequent tick before their delta is
                // meaningful, same as the CPU counter above.
                continue;
            }

            total += SafeNextValue(counter);
        }

        return Math.Min(total, 100);
    }

    private static double SafeNextValue(PerformanceCounter counter)
    {
        try
        {
            return counter.NextValue();
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cpuCounter.Dispose();

        foreach (var counter in _gpuCounters.Values)
            counter.Dispose();
    }
}
