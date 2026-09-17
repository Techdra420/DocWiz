using System.Collections.Concurrent;
using DocWiz.Build.Pdf;

namespace DocWiz.App;

public sealed class PdfJob
{
    public required string Id { get; init; }
    public string Status { get; set; } = "running"; // running | done | error
    public ExportResult? Result { get; set; }
    public string? Error { get; set; }
    public string? OutDir { get; set; }
}

/// <summary>
/// In-memory registry for background PDF-export jobs. PDF export is slow (~30-60s),
/// so the editor starts it here and returns a job id immediately; the client polls
/// for the outcome. Jobs live for the session only.
/// </summary>
public sealed class PdfJobRegistry
{
    private readonly ConcurrentDictionary<string, PdfJob> _jobs = new();

    public PdfJob Start(string projectRoot, ExportSelection selection, string outDir, string? edgeExe)
    {
        var job = new PdfJob { Id = Guid.NewGuid().ToString("N"), OutDir = outDir };
        _jobs[job.Id] = job;
        _ = Task.Run(() =>
        {
            try { job.Result = PdfExporter.Export(projectRoot, selection, outDir, edgeExe); job.Status = "done"; }
            catch (Exception ex) { job.Error = ex.Message; job.Status = "error"; }
        });
        return job;
    }

    /// <summary>Test seam: registers an already-terminal job without launching Edge, so the
    /// add/get/evict contract is unit-testable Edge-free (the real <see cref="Start"/> path runs
    /// a live export). Not used in production.</summary>
    internal PdfJob SeedTerminal(string status)
    {
        var job = new PdfJob { Id = Guid.NewGuid().ToString("N"), Status = status };
        _jobs[job.Id] = job;
        return job;
    }

    /// <summary>
    /// Returns the job, evicting it once it reaches a terminal state (<c>done</c>/<c>error</c>)
    /// so its <see cref="ExportResult"/> is freed after the client reads the outcome. Safe
    /// because the poller (app.js <c>pollPdf</c>) stops the moment it sees a non-running
    /// status — it never re-GETs a terminal job — so the entry is delivered exactly once.
    /// </summary>
    public PdfJob? Get(string id)
    {
        if (!_jobs.TryGetValue(id, out var j))
            return null;
        if (j.Status is "done" or "error")
            _jobs.TryRemove(id, out _);
        return j;
    }
}
