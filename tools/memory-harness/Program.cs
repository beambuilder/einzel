// What a solve or a diffusive step holds, per node, in a fresh process.
//
//   memory-harness nodes   <model.json>                    what `einzel estimate` says, and the grids
//   memory-harness fields  <model.json> [live]             assemble every field element, as `einzel run` does
//   memory-harness diffuse <model.json> [live] [declared]  one step of the model's density, or with
//                                                          `declared` its whole declared flight
//
// Two quantities, one per process, because measuring one disturbs the other:
//
//   default  the peak WORKING SET above the process's baseline, with the managed heap polled
//            lightly beside it. What the operating system had to supply to an unconstrained
//            process - garbage the collector had not yet reclaimed included.
//   live     the peak LIVE SET: what survived a forced full collection, sampled continuously
//            while the work runs. What `einzel estimate` reports, and what has to fit.
//
// Forcing collections every few hundred microseconds is what makes the live peak measurable
// and is also what keeps the working set down, so a run that sampled both would report a
// working set no ordinary run reaches. One JSON line on stdout. See README.md.

using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;

var options = args.Skip(2).ToHashSet(StringComparer.Ordinal);

// The mode is checked before anything runs: an unknown one used to fall through to the diffusive
// branch and fail there on a field that was never built.
if (args.Length < 2 || args[0] is not ("nodes" or "fields" or "diffuse") || options.Except(["live", "declared"]).Any())
{
    Console.Error.WriteLine("usage: memory-harness nodes|fields|diffuse <model.json> [live] [declared]");
    return 2;
}

var mode = args[0];
var path = Path.GetFullPath(args[1]);
var live = options.Contains("live");

// The figure under test, and the grids it was computed from.
var estimate = EstimateCommand.Execute(path, calibrate: false);

if (mode == "nodes")
{
    Console.WriteLine(JsonSerializer.Serialize(Elements(estimate)));
    return 0;
}

var validation = ModelValidator.Validate(
    ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path));

if (!validation.IsValid)
{
    Console.Error.WriteLine(validation.Errors[0]);
    return 1;
}

var model = validation.Model!;
IElectrostaticField? field = null;
IReadOnlyList<Einzel.Core.Results.ValidityWarning>? warnings = null;

if (mode == "diffuse")
{
    // The field is built BEFORE the baseline is taken: what is measured is the density run, not
    // the solve beneath it - which is how `einzel estimate` separates the two terms as well.
    (field, warnings) = FieldAssembly.BuildReported(model);

    // ONE STEP, unless asked for the declared flight. A plain run allocates everything it holds
    // before or during its first step - the coefficients and the face operator - and every
    // later step reuses it, so one step is its peak and the drift tube's 1,500 us would only
    // repeat it, for minutes under the live sampler.
    //
    // NOT SO WITH A SELF-FIELD, and one step under-reads it. The first Poisson solve is done
    // before the loop, when the step's own arrays do not exist yet; every later refresh solves
    // inside the loop, on top of them. So a mean-field run is measured over a declared flight
    // long enough to refresh several times - `declared`.
    if (!options.Contains("declared"))
    {
        model = model with { MaximumFlightTimeSi = 1e-12 };
    }
}

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var self = Process.GetCurrentProcess();
self.Refresh();

var peakBefore = self.PeakWorkingSet64;
var commitBefore = self.PeakPagedMemorySize64;
var heapBefore = GC.GetTotalMemory(forceFullCollection: true);
var liveBefore = Survived();
var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);

long heapPeak = 0;
long livePeak = liveBefore;
var stop = 0;

var monitor = new Thread(live ? SampleLive : PollHeap)
{
    IsBackground = true,
    Priority = ThreadPriority.AboveNormal,
};

monitor.Start();

var watch = Stopwatch.StartNew();
object? kept = null;

try
{
    if (mode == "fields")
    {
        kept = FieldAssembly.BuildReported(model);
    }
    else if (model.IsMixture)
    {
        kept = DiffusionRun.ExecuteMixture(model, field!, warnings!);
    }
    else
    {
        kept = DiffusionRun.Execute(model, field!, warnings!);
    }
}
finally
{
    watch.Stop();
    Volatile.Write(ref stop, 1);
    monitor.Join();
}

self.Refresh();

var peakAfter = self.PeakWorkingSet64;
var commitAfter = self.PeakPagedMemorySize64;
var allocated = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;

// What the finished work still holds, with the result kept alive until after the collection.
var retained = GC.GetTotalMemory(forceFullCollection: true) - heapBefore;
GC.KeepAlive(kept);
GC.KeepAlive(field);

const double Mebi = 1024.0 * 1024.0;

Console.WriteLine(JsonSerializer.Serialize(new
{
    mode,
    quantity = live ? "live" : "workingSet",
    model = Path.GetFileName(path),

    // Nodes the work was done on: the field grids, or the density grid for a diffusive run.
    gridNodes = mode == "diffuse" ? DiffusionRun.GridFor(model).NodeCount : 0L,
    species = Math.Max(1, model.Species.Count),
    elements = Elements(estimate),
    estimateMiB = estimate.MemoryMiB,

    seconds = watch.Elapsed.TotalSeconds,

    // Peak working set and peak commit above what the process held before the work began.
    // In a live run these are depressed by the sampler's own collections - read them from a
    // default run.
    deltaPeakWorkingSetMiB = (peakAfter - peakBefore) / Mebi,
    deltaPeakCommitMiB = (commitAfter - commitBefore) / Mebi,
    baselinePeakWorkingSetMiB = peakBefore / Mebi,

    // The managed heap's peak above its start, dead objects included (default runs only).
    polledHeapPeakMiB = live ? (double?)null : (heapPeak - heapBefore) / Mebi,

    // The live set's peak above its start (live runs only).
    liveSetPeakMiB = live ? (livePeak - liveBefore) / Mebi : (double?)null,

    // Everything allocated over the run: how much the collector had to free.
    allocatedMiB = allocated / Mebi,

    // What the finished work still holds.
    retainedMiB = retained / Mebi,

    gen2Collections = GC.CollectionCount(2),
    serverGc = GCSettings.IsServerGC,
}));

return 0;

// Light polling: what the heap holds, garbage included. Does not provoke a collection.
void PollHeap()
{
    while (Volatile.Read(ref stop) == 0)
    {
        heapPeak = Math.Max(heapPeak, GC.GetTotalMemory(forceFullCollection: false));
        Thread.Sleep(1);
    }
}

// SolveMemoryTests' sampler: a forced full collection, then the collector's own account of what
// survived it, waiting as long again as the collection took so the work keeps half the time.
void SampleLive()
{
    var clock = Stopwatch.StartNew();

    while (Volatile.Read(ref stop) == 0)
    {
        var started = clock.Elapsed;

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        livePeak = Math.Max(livePeak, Survived());

        var resume = clock.Elapsed + (clock.Elapsed - started);

        while (clock.Elapsed < resume && Volatile.Read(ref stop) == 0)
        {
            Thread.SpinWait(32);
        }
    }
}

// What the last full blocking collection left live, from that collection's own record. Reading
// GC.GetTotalMemory after the collection instead counts whatever the work allocated in between.
static long Survived()
{
    var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
    var survived = 0L;

    foreach (var generation in info.GenerationInfo)
    {
        survived += generation.SizeAfterBytes - generation.FragmentationAfterBytes;
    }

    return survived;
}

static object[] Elements(EstimateOutcome estimate) =>
    [.. estimate.Elements.Select(e => new { e.Type, e.NodeCount, e.MemoryMiB })];
