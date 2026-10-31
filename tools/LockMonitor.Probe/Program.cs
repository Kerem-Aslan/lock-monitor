using System.Diagnostics;
using System.Globalization;
using LockMonitor.Rendering;
using LockMonitor.Sampling;
using LockMonitor.Sensors;

// Usage: LockMonitor.Probe [--interval <seconds>] [--seconds <duration>] [--size small|medium|large]
//                          [--no-gpu-temp] [--json] [--write-cards <dir>] [--templates <dir>]
// Reads the widget's sensors the same way the provider does and prints values plus the cost of
// each sensor, so data sources can be checked and measured without deploying the widget.
// --json also prints the card data the widget would send for the last sample.

double intervalSeconds = 2;
double durationSeconds = 20;
var size = CardSize.Large;
bool gpuTemperature = true;
bool json = false;
string? cardsDir = null;
string templatesDir = Path.Combine("src", "LockMonitor.Widget", "Templates");
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--interval": intervalSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--seconds": durationSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--size": size = Enum.Parse<CardSize>(args[++i], ignoreCase: true); break;
        case "--no-gpu-temp": gpuTemperature = false; break;
        case "--json": json = true; break;
        case "--write-cards": cardsDir = args[++i]; break;
        case "--templates": templatesDir = args[++i]; break;
    }
}

var adapters = new GpuAdapterCatalog();
Console.WriteLine("Adapters:");
foreach (var a in adapters.Adapters)
{
    Console.WriteLine($"  0x{a.Luid:X}  {a.Name}  vendor=0x{a.VendorId:X4}  dedicated={Units.Bytes(a.DedicatedVideoMemory)}" +
        (a.Luid == adapters.Default?.Luid ? "  (default)" : ""));
}
Console.WriteLine($"  hybrid graphics: {adapters.IsHybrid}");

var interval = TimeSpan.FromSeconds(intervalSeconds);
var plan = new SamplingPlan(true, size, interval,
    gpuTemperature && adapters.Default is { } gpu ? new LuidSet([gpu.Luid]) : default);
var sensors = SensorCatalog.CreateAll(adapters).Where(s => s.IsNeededFor(plan)).ToArray();
var runners = sensors.Select(s => new SensorRunner(s, TimeProvider.System)).ToArray();
var costs = new double[sensors.Length];
var process = Process.GetCurrentProcess();

Console.WriteLine($"Probe: {sensors.Length} sensors ({string.Join(", ", sensors.Select(s => s.Name))}), size={size}, interval={intervalSeconds}s, duration={durationSeconds}s");

// Priming pass (rate counters need two readings), like the scheduler does.
var snapshot = new SystemSnapshot();
foreach (var r in runners)
{
    r.Sample(snapshot, plan);
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
        runners[i].Sample(snapshot, plan);
        costs[i] += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }
    ticks++;

    var g = CardData.SelectGpu(snapshot, null);
    string line = $"{snapshot.Timestamp.ToLocalTime():HH:mm:ss}  CPU {Pct(snapshot.CpuPercent)}  RAM {Pct(snapshot.MemoryPercent)}  GPU {Pct(g?.UsagePercent)}";
    if (size >= CardSize.Medium)
    {
        line += $"  T {(g?.TemperatureCelsius is { } t ? Units.Celsius(t) : "N/A"),5}" +
                $"  VRAM {(g is { MemoryUsedBytes: { } u, MemoryTotalBytes: { } tot } ? Units.UsedOfTotal(u, tot) : "N/A")}" +
                $"  NET ↓{(snapshot.NetworkDownBytesPerSecond is { } d ? Units.Rate(d) : "N/A")} ↑{(snapshot.NetworkUpBytesPerSecond is { } up ? Units.Rate(up) : "N/A")}" +
                $"  {string.Join(" ", (snapshot.Drives ?? []).Select(d => $"{d.Name} {d.Percent:0}%"))}";
    }
    Console.WriteLine(line);
    Thread.Sleep(interval);
}

var wall = Stopwatch.GetElapsedTime(wallAtStart);
process.Refresh();
var cpu = process.TotalProcessorTime - cpuAtStart;
Console.WriteLine();
Console.WriteLine("Average cost per tick:");
for (int i = 0; i < sensors.Length; i++)
{
    Console.WriteLine($"  {sensors[i].Name,-16} {costs[i] / ticks,8:0.00} ms");
}
Console.WriteLine($"  {"total",-16} {costs.Sum() / ticks,8:0.00} ms");
Console.WriteLine($"Process CPU: {cpu.TotalMilliseconds:0} ms over {wall.TotalSeconds:0.0} s = " +
    $"{100.0 * cpu.TotalMilliseconds / wall.TotalMilliseconds:0.00} % of one core");
Console.WriteLine($"Private memory: {process.PrivateMemorySize64 / 1048576.0:0.0} MB, working set: {process.WorkingSet64 / 1048576.0:0.0} MB");

if (json)
{
    Console.WriteLine();
    Console.WriteLine(CardData.Build(snapshot, size, new CardOptions(null, gpuTemperature)));
}

if (cardsDir is not null)
{
    // Real templates + data from this sample, for tools/template-check (expands them with the
    // official Adaptive Cards templating engine).
    var store = TemplateStore.LoadFrom(templatesDir);
    Directory.CreateDirectory(cardsDir);
    File.WriteAllText(Path.Combine(cardsDir, "template-card.json"), store.Composed);
    File.WriteAllText(Path.Combine(cardsDir, "template-settings.json"), store.Settings ?? "{}");
    foreach (var s in Enum.GetValues<CardSize>())
    {
        File.WriteAllText(Path.Combine(cardsDir, $"data-{s.ToString().ToLowerInvariant()}.json"),
            CardData.Build(snapshot, s, new CardOptions(null, gpuTemperature)));
    }
    File.WriteAllText(Path.Combine(cardsDir, "data-pending.json"), CardData.Build(null, CardSize.Large));
    File.WriteAllText(Path.Combine(cardsDir, "data-settings.json"),
        SettingsCard.BuildData(WidgetSettings.Default with { DriveList = "C:,D:" }, adapters.Adapters, adapters.Default,
            DriveCatalog.FixedDrives(), DriveCatalog.SystemDriveName));
    Console.WriteLine($"Wrote templates and sample data to {Path.GetFullPath(cardsDir)}");
}

foreach (var r in runners)
{
    r.Close();
}

static string Pct(double? value) => (value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "N/A").PadLeft(6);
