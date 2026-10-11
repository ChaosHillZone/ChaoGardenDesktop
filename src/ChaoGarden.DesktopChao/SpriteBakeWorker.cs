using System.Text.Json;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.ChaoPresentation;

namespace ChaoGarden.DesktopChao;

/// <summary>One dedicated graphics thread, cancellable/coalesced requests, immutable publication.</summary>
public sealed class SpriteBakeWorker : IDisposable
{
    private readonly string resources;
    private readonly string cache;
    private readonly AutoResetEvent wake = new(false);
    private readonly object gate = new();
    private readonly Thread thread;
    private CancellationTokenSource? active;
    private (ChaoEntity Chao, string Key, bool Force)? pending;
    private int revision;
    private bool disposed;
    public event Action<string, string, bool>? Ready;
    public event Action<string>? Status;
    public event Action<Exception>? Failed;
    public bool IsBusy
    {
        get { lock (gate) { return active != null || pending != null; } }
    }

    public SpriteBakeWorker(string resources, string cache)
    {
        this.resources = resources;
        this.cache = cache;
        thread = new Thread(Run) { IsBackground = true, Name = "Chao photobooth" };
        thread.Start();
    }

    public void Request(ChaoEntity chao, string key, bool force = false)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            revision++;
            pending = (BoothJson.Clone(chao), key, force);
            active?.Cancel();
            wake.Set();
        }
    }

    private bool Current(int version)
    {
        lock (gate)
        {
            return !disposed && revision == version;
        }
    }

    private void Run()
    {
        while (true)
        {
            wake.WaitOne();
            (ChaoEntity Chao, string Key, bool Force) job;
            int version;
            CancellationTokenSource cancellation;
            lock (gate)
            {
                if (disposed) return;
                if (pending == null) continue;
                job = pending.Value;
                pending = null;
                version = revision;
                active = cancellation = new CancellationTokenSource();
            }
            try
            {
                string full = Path.Combine(cache, job.Key, "full");
                string? existing = job.Force ? null : PublishedDirectory(full);
                if (existing != null)
                {
                    if (Current(version)) Ready?.Invoke(existing, job.Key, true);
                    continue;
                }
                using HiddenGraphicsHost graphics = new();
                foreach (bool directions in new[] { false, true })
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (Current(version))
                    {
                        Status?.Invoke(directions ? "Baking cursor look directions…" : "Baking Chao animations…");
                    }
                    string folder = Path.Combine(cache, job.Key, directions ? "full" : "base");
                    string? generation = job.Force ? null : PublishedDirectory(folder);
                    if (generation == null)
                    {
                        BoothConfig recipe = directions ? DesktopRecipes.Create(true) : DesktopRecipes.CreateBase();
                        ExportManifest result = BakePipeline.Bake(recipe, job.Chao,
                            graphics.Device, resources, null, folder, cancellation.Token);
                        if (result.Warnings.Count > 0)
                        {
                            throw new InvalidDataException("Sprite framing failed: " + result.Warnings[0]);
                        }
                        generation = PublishedDirectory(folder)!;
                    }
                    if (Current(version)) Ready?.Invoke(generation, job.Key, directions);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (Current(version)) Failed?.Invoke(ex);
            }
            finally
            {
                lock (gate)
                {
                    if (active == cancellation) active = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    public static string? PublishedDirectory(string output)
    {
        string latest = Path.Combine(output, "latest.json");
        if (!File.Exists(latest)) return null;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(latest));
        string path = BoothJson.ResourcePath(output, document.RootElement.GetProperty("manifest").GetString()!);
        if (!File.Exists(path)) return null;
        return Path.GetDirectoryName(path);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            pending = null;
            active?.Cancel();
            wake.Set();
        }
        if (thread.Join(10000)) wake.Dispose();
    }
}
