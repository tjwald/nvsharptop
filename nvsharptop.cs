#:property PublishAot true
#:package Spectre.Console@*

using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;


var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var cliParameters = CliParameters.Create(args);
var devices = new DeviceCollection();
AnsiConsole.Clear();
int i = 0;
while (!cts.Token.IsCancellationRequested)
{
    devices.Update();

    int consoleWidth = Console.WindowWidth;
    int graphHeight = 10;
    int yAxisWidth = 6;
    int availableWidth, graphWidth;
    (graphWidth, availableWidth) = CalculateGraphWidth(consoleWidth, yAxisWidth);

    if (ShouldDisplay(devices.LastDisplay, cliParameters.DisplayInterval))
    {
        devices.UpdateDeviceHistory(graphWidth);
        List<IRenderable> renderables = [];
        foreach (var device in devices)
        {
            var samples = devices.GetHistory(device)?.GetSamples() ?? Array.Empty<DeviceSample>();
            renderables.AddRange(RenderDeviceChartSpectre(device, samples, graphWidth, graphHeight, availableWidth));
        }
        renderables.AddRange(RenderDeviceTableSpectre(devices.ToList(), cliParameters.DisplayInterval));
        devices.LastDisplay = DateTime.UtcNow;

        Console.CursorVisible = false;
        Console.SetCursorPosition(0, 0);
        var grid = new Grid();
        grid.AddColumn();
        foreach (var renderable in renderables)
        {
            grid.AddRow(renderable);
        }
        AnsiConsole.Write(grid);
    }
    SleepLoop(cliParameters.SampleInterval, cts.Token);
}


static (int graphWidth, int availableWidth) CalculateGraphWidth(int consoleWidth, int yAxisWidth)
{
    int availableWidth = consoleWidth - yAxisWidth;
    int graphWidth = (int)Math.Floor((double)availableWidth / 3) - 1;
    return (graphWidth, availableWidth);
}

static bool ShouldDisplay(DateTime lastDisplay, double displayInterval)
{
    return (DateTime.UtcNow - lastDisplay).TotalSeconds >= displayInterval;
}

static void SleepLoop(double seconds, CancellationToken token)
{
    int ticks = (int)(seconds * 10);
    for (int i = 0; i < ticks && !token.IsCancellationRequested; i++)
        token.WaitHandle.WaitOne(100);
}

static string Truncate(string s, int m) => s.Length <= m ? s : s[..(m - 1)] + "…";

// Display logic
static List<IRenderable> RenderDeviceChartSpectre(DeviceInfo device, DeviceSample[] samples, int graphWidth, int graphHeight, int _)
{
    List<IRenderable> renderables = [
        new Markup($"[bold]{device.Type} {device.Id}[/] [grey]{Truncate(device.Name, 30)}[/]\n")
    ];
    var grid = new Grid();
    grid.AddColumn();
    grid.AddColumn();
    for (int row = graphHeight - 1; row >= 0; row--)
    {
        int percent = (int)Math.Round((row + 1) * 100.0 / graphHeight);
        string yLabel = (percent % 5 == 0 || row == graphHeight - 1) ? $"[grey]{percent,3}%[/] │" : "    │";
        var bars = new List<string>();
        int pad = graphWidth - samples.Length;
        for (int i = 0; i < pad; i++)
        {
            bars.Add("[grey]··[/]");
            if (i != graphWidth - 1) bars.Add(" ");
        }
        for (int idx = 0; idx < samples.Length; idx++)
        {
            var s = samples[idx];
            bool uOn = s.Util * graphHeight / 100 > row;
            bool mOn = s.MemPct * graphHeight / 100 > row;
            string utilColor = s.Util >= 80 ? "red" : s.Util >= 40 ? "yellow" : "green";
            if ((percent % 5 == 0 || row == graphHeight - 1) && !uOn && !mOn)
                bars.Add("[grey]··[/]");
            else
                bars.Add($"{(uOn ? $"[{utilColor}]█[/]" : " ")}{(mOn ? "[cyan]█[/]" : " ")}");
            if (idx != samples.Length - 1 || pad > 0)
                bars.Add(" ");
        }
        grid.AddRow(new Markup(yLabel), new Markup(string.Join("", bars)));
    }
    int chartWidth = graphWidth * 3 - 1;
    grid.AddRow(new Markup("     └"), new Markup("[grey]" + new string('─', chartWidth) + "[/]"));
    renderables.Add(grid);
    return renderables;
}

static List<IRenderable> RenderDeviceTableSpectre(List<DeviceInfo> devices, double pollTime)
{
    List<IRenderable> renderables = [];
    var table = new Table();
    table.AddColumn("Type");
    table.AddColumn("Id");
    table.AddColumn("Name");
    table.AddColumn("Temp");
    table.AddColumn("Util");
    table.AddColumn("Mem");
    foreach (var device in devices)
    {
        string tempColor = device.Temp >= 80 ? "red" : device.Temp >= 60 ? "yellow" : "green";
        string utilColor = device.Util >= 80 ? "red" : device.Util >= 40 ? "yellow" : "green";
        table.AddRow(
            device.Type.ToString(),
            $"[bold]{device.Id}[/]",
            device.Name,
            $"[{tempColor}]{device.Temp}°C[/]",
            $"[{utilColor}]{device.Util}%[/]",
            $"[cyan]{device.MemUsed}[/]/[grey]{device.MemTotal}[/]"
        );
    }
    renderables.Add(table);
    renderables.Add(new Markup($"[bold]NvHtop[/] [grey][[{DateTime.Now:HH:mm:ss}]][/], Refresh every {pollTime}s\n"));
    return renderables;
}

// Data structures
class DeviceSample
{
    public int Util { get; set; }
    public int MemPct { get; set; }
    public DeviceSample(int util, int memPct) { Util = util; MemPct = memPct; }
}

class DeviceHistory
{
    public Queue<DeviceSample> Samples { get; } = new();
    public void AddSample(DeviceSample sample, int maxSamples)
    {
        Samples.Enqueue(sample);
        while (Samples.Count > maxSamples) Samples.Dequeue();
    }
    public DeviceSample[] GetSamples() => Samples.ToArray();
}

// Device type enum
public enum DeviceType
{
    Gpu
}

record DeviceInfo(string Id, string Name, int Temp, int Util, int MemUsed, int MemTotal, DeviceType Type);

class DeviceCollection : IEnumerable<DeviceInfo>
{
    public Dictionary<string, DeviceHistory> History = new();
    public Dictionary<string, List<DeviceSample>> SampleBuffer = new();
    public DateTime LastDisplay = DateTime.UtcNow;
    private List<DeviceInfo> devices = new();

    public DeviceHistory GetHistory(DeviceInfo device)
    {
        return History.TryGetValue(device.Id, out var hist) ? hist : null;
    }

    public void Update()
    {
        devices = QueryDevices();
        CollectDeviceSamples();
    }

    public void UpdateDeviceHistory(int graphWidth)
    {
        foreach (var device in devices)
        {
            if (!History.TryGetValue(device.Id, out var hist))
                History[device.Id] = hist = new DeviceHistory();
            var buf = SampleBuffer[device.Id];
            int avgUtil = (int)buf.Average(x => x.Util);
            int avgMem = (int)buf.Average(x => x.MemPct);
            hist.AddSample(new DeviceSample(avgUtil, avgMem), graphWidth);
            buf.Clear();
        }
    }

    private List<DeviceInfo> QueryDevices()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = "--query-gpu=index,name,temperature.gpu,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return outp
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(',', StringSplitOptions.TrimEntries))
            .Where(a => a.Length == 6)
            .Select(a => new DeviceInfo(
                a[0], a[1],
                int.Parse(a[2]), int.Parse(a[3]),
                int.Parse(a[4]), int.Parse(a[5]),
                DeviceType.Gpu
            ))
            .ToList();
    }

    private void CollectDeviceSamples()
    {
        foreach (var device in devices)
        {
            if (!SampleBuffer.TryGetValue(device.Id, out var buf))
                SampleBuffer[device.Id] = buf = new List<DeviceSample>();
            int memPct = (int)(device.MemUsed * 100.0 / device.MemTotal);
            buf.Add(new DeviceSample(device.Util, memPct));
        }
    }

    public IEnumerator<DeviceInfo> GetEnumerator() => devices.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

record CliParameters(double SampleInterval, double DisplayInterval)
{
    public static CliParameters Create(string[] args)
    {
        double sample = ParseArg(args, "--sample-interval", 0.1);
        double display = ParseArg(args, "--display-interval", 3);
        return new CliParameters(sample, display);
    }

    private static double ParseArg(string[] args, string name, double def)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == name && double.TryParse(args[i + 1], out var t) && t > 0) return t;
        return def;
    }
}