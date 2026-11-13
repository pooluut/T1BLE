using System.Diagnostics;
using System.Text.Json;

namespace T1BLE;

internal static class PerformanceProbe
{
    public static void Run()
    {
        ApplicationConfiguration.Initialize();
        using var context = new BackgroundVoiceContext("88:BF:7A:4F:E5:42");
        var ui = SynchronizationContext.Current!;
        var samples = new List<object>();
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var initialCpu = process.TotalProcessorTime;
        double maxDelay = 0;
        int replies = 0;
        using var sampler = new System.Threading.Timer(_ =>
        {
            var sent = Stopwatch.GetTimestamp();
            ui.Post(_ =>
            {
                double delay = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
                maxDelay = Math.Max(maxDelay, delay);
                replies++;
                process.Refresh();
                samples.Add(new { seconds = stopwatch.Elapsed.TotalSeconds, delayMs = delay,
                    cpuSeconds = (process.TotalProcessorTime - initialCpu).TotalSeconds,
                    workingSetMB = process.WorkingSet64 / 1048576.0,
                    privateMB = process.PrivateMemorySize64 / 1048576.0,
                    threads = process.Threads.Count, handles = process.HandleCount });
                Directory.CreateDirectory("logs");
                File.WriteAllText("logs/performance-probe.json", JsonSerializer.Serialize(new
                {
                    elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                    cpuPercentOneCore = (process.TotalProcessorTime - initialCpu).TotalSeconds / stopwatch.Elapsed.TotalSeconds * 100,
                    maxUiDelayMs = maxDelay, replies, samples
                }, new JsonSerializerOptions { WriteIndented = true }));
                if (stopwatch.Elapsed < TimeSpan.FromSeconds(30)) return;
                context.ExitThread();
            }, null);
        }, null, 500, 500);
        Application.Run(context);
    }
}
