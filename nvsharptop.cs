#:property PublishAot true
#:package Spectre.Console@*

using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;


// Main function at the top
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
(double sampleInterval, double displayInterval) = ParseIntervals(args);
var state = NvSharpTopInit();
AnsiConsole.Clear();
int i = 0;
while (!cts.Token.IsCancellationRequested)
{
    var gpus = QueryGpus();
    CollectGpuSamples(state, gpus);

    int consoleWidth = Console.WindowWidth;
    int graphHeight = 10;
    int yAxisWidth = 6;
    int availableWidth, graphWidth;
    (graphWidth, availableWidth) = CalculateGraphWidth(consoleWidth, yAxisWidth);

    if (ShouldDisplay(state, displayInterval))
    {
        UpdateGpuHistory(state, gpus, graphWidth);
        List<IRenderable> renderables = [];
        foreach (var gpu in gpus)
        {
            var samples = state.History[gpu.Index].GetSamples();
            renderables.AddRange(RenderGpuChartSpectre(gpu, samples, graphWidth, graphHeight, availableWidth));
        }
        renderables.AddRange(RenderGpuTableSpectre(gpus, displayInterval));
        state.LastDisplay = DateTime.UtcNow;

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
    SleepLoop(sampleInterval, cts.Token);
}


static (int graphWidth, int availableWidth) CalculateGraphWidth(int consoleWidth, int yAxisWidth)
{
    int availableWidth = consoleWidth - yAxisWidth;
    int graphWidth = (int)Math.Floor((double)availableWidth / 3) - 1;
    return (graphWidth, availableWidth);
}

static NvSharpTopState NvSharpTopInit() => new NvSharpTopState();

static void CollectGpuSamples(NvSharpTopState state, List<GpuInfo> gpus)
{
    foreach (var g in gpus)
    {
        if (!state.SampleBuffer.TryGetValue(g.Index, out var buf))
            state.SampleBuffer[g.Index] = buf = new List<GpuSample>();
        int memPct = (int)(g.MemUsed * 100.0 / g.MemTotal);
        buf.Add(new GpuSample(g.Util, memPct));
    }
}

static bool ShouldDisplay(NvSharpTopState state, double displayInterval)
{
    return (DateTime.UtcNow - state.LastDisplay).TotalSeconds >= displayInterval;
}

static void UpdateGpuHistory(NvSharpTopState state, List<GpuInfo> gpus, int graphWidth)
{
    foreach (var g in gpus)
    {
        if (!state.History.TryGetValue(g.Index, out var hist))
            state.History[g.Index] = hist = new GpuHistory();
        var buf = state.SampleBuffer[g.Index];
        int avgUtil = (int)buf.Average(x => x.Util);
        int avgMem = (int)buf.Average(x => x.MemPct);
        hist.AddSample(new GpuSample(avgUtil, avgMem), graphWidth);
        buf.Clear();
    }
}

static List<GpuInfo> QueryGpus()
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
        .Select(a => new GpuInfo(
            int.Parse(a[0]), a[1],
            int.Parse(a[2]), int.Parse(a[3]),
            int.Parse(a[4]), int.Parse(a[5])
        ))
        .ToList();
}

static void SleepLoop(double seconds, CancellationToken token)
{
    int ticks = (int)(seconds * 10);
    for (int i = 0; i < ticks && !token.IsCancellationRequested; i++)
        token.WaitHandle.WaitOne(100);
}

static (double sampleInterval, double displayInterval) ParseIntervals(string[] args)
{
    double sample = ParseArg(args, "--sample-interval", 0.1);
    double display = ParseArg(args, "--display-interval", 3);
    return (sample, display);
}

static double ParseArg(string[] args, string name, double def)
{
    for (int i = 0; i + 1 < args.Length; i++)
        if (args[i] == name && double.TryParse(args[i + 1], out var t) && t > 0) return t;
    return def;
}

static string Truncate(string s, int m) => s.Length <= m ? s : s[..(m - 1)] + "…";

// Display logic
static List<IRenderable> RenderGpuChartSpectre(GpuInfo gpu, GpuSample[] samples, int graphWidth, int graphHeight, int _)
{
    List<IRenderable> renderables = [
        new Markup($"[bold]GPU {gpu.Index}[/] [grey]{Truncate(gpu.Name, 30)}[/]\n")
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

static List<IRenderable> RenderGpuTableSpectre(List<GpuInfo> gpus, double pollTime)
{
    List<IRenderable> renderables = [];
    var table = new Table();
    table.AddColumn("GPU");
    table.AddColumn("Name");
    table.AddColumn("Temp");
    table.AddColumn("Util");
    table.AddColumn("Mem");
    foreach (var g in gpus)
    {
        string tempColor = g.Temp >= 80 ? "red" : g.Temp >= 60 ? "yellow" : "green";
        string utilColor = g.Util >= 80 ? "red" : g.Util >= 40 ? "yellow" : "green";
        table.AddRow(
            $"[bold]{g.Index}[/]",
            g.Name,
            $"[{tempColor}]{g.Temp}°C[/]",
            $"[{utilColor}]{g.Util}%[/]",
            $"[cyan]{g.MemUsed}[/]/[grey]{g.MemTotal}[/]"
        );
    }
    renderables.Add(table);
    renderables.Add(new Markup($"[bold]NvHtop[/] [grey][[{DateTime.Now:HH:mm:ss}]][/], Refresh every {pollTime}s\n"));
    return renderables;
}

// Data structures
class GpuSample
{
    public int Util { get; set; }
    public int MemPct { get; set; }
    public GpuSample(int util, int memPct) { Util = util; MemPct = memPct; }
}

class GpuHistory
{
    public Queue<GpuSample> Samples { get; } = new();
    public void AddSample(GpuSample sample, int maxSamples)
    {
        Samples.Enqueue(sample);
        while (Samples.Count > maxSamples) Samples.Dequeue();
    }
    public GpuSample[] GetSamples() => Samples.ToArray();
}

record GpuInfo(int Index, string Name, int Temp, int Util, int MemUsed, int MemTotal);

class NvSharpTopState
{
    public Dictionary<int, GpuHistory> History = new();
    public Dictionary<int, List<GpuSample>> SampleBuffer = new();
    public DateTime LastDisplay = DateTime.UtcNow;
}