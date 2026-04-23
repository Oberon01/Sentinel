namespace Sentinel.Core;

public interface IMonitor
{
    string Name { get; }
    Task<MonitorResult> CheckAsync(CancellationToken cancellationToken = default);
}