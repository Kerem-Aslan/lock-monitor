using System.Diagnostics;
using System.Globalization;
using LockMonitor.Rendering;
using LockMonitor.Sampling;
using LockMonitor.Sensors;

// Usage: LockMonitor.Probe [--interval <seconds>] [--seconds <duration>] [--size small|medium|large]
// Reads the widget's sensors the same way the provider does and prints values plus the cost of
// each sensor, so data sources can be checked and measured without deploying the widget.

double intervalSeconds = 2;
double durationSeconds = 20;
var size = CardSize.Small;
for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--interval": intervalSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--seconds": durationSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--size": size = Enum.Parse<CardSize>(args[++i], ignoreCase: true); break;
    }
}

var sensors = SensorCatalog.CreateAll().Where(s => s.RequiredSize <= size).ToArray();
var runners = sensors.Select(s => new SensorRunner(s, TimeProvider.System)).ToArray();
var costs = new double[sensors.Length];
var process = Process.GetCurrentProcess();
var interval = TimeSpan.FromSeconds(intervalSeconds);

Console.WriteLine($"Lock Monitor probe: {sensors.Length} sensors, size={size}, interval={intervalSeconds}s, duration={durationSeconds}s");

// Priming pass (rate counters need two readings), like the scheduler does.
var snapshot = new SystemSnapshot();
foreach (var r in runners)
{
    r.Sample(snapshot);
}
Thread.Sleep(SamplingScheduler.PrimeDelay);

var cpuAtStart = process.TotalProcessorTime;
var wallAtStart = Stopwatch.GetTimestamp();
int ticks = 0;
while (Stopwatch.GetElapsedTime(wallAtStart).TotalSeconds < durationSeconds)
{
    snapshot = snapshot.Clone();
    snapshot.Timestamp = DateTimeOffset.UtcNow;
    for (int i = 0; i < runners.Length; i++)
    {
        long t0 = Stopwatch.GetTimestamp();
        runners[i].Sample(snapshot);
        costs[i] += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }
    ticks++;
    Console.WriteLine(
        $"{snapshot.Timestamp.ToLocalTime():HH:mm:ss}  CPU {Fmt(snapshot.CpuPercent),6}  RAM {Fmt(snapshot.MemoryPercent),6}" +
        $"  GPU {Fmt(snapshot.GpuPercent),6}  [{snapshot.GpuName ?? "busiest adapter"}]");
    Thread.Sleep(interval);
}

var wall = Stopwatch.GetElapsedTime(wallAtStart);
process.Refresh();
var cpu = process.TotalProcessorTime - cpuAtStart;
Console.WriteLine();
Console.WriteLine("Average cost per tick:");
for (int i = 0; i < sensors.Length; i++)
{
    Console.WriteLine($"  {sensors[i].Name,-12} {costs[i] / ticks,8:0.00} ms");
}
Console.WriteLine($"Process CPU: {cpu.TotalMilliseconds:0} ms over {wall.TotalSeconds:0.0} s = " +
    $"{100.0 * cpu.TotalMilliseconds / wall.TotalMilliseconds / Environment.ProcessorCount:0.000} % of all cores " +
    $"({100.0 * cpu.TotalMilliseconds / wall.TotalMilliseconds:0.00} % of one core)");
Console.WriteLine($"Private memory: {process.PrivateMemorySize64 / 1048576.0:0.0} MB, working set: {process.WorkingSet64 / 1048576.0:0.0} MB");

foreach (var r in runners)
{
    r.Close();
}

static string Fmt(double? value) => value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "N/A";
