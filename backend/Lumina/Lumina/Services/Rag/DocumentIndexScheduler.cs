using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Rag;

public interface IDocumentIndexScheduler
{
    void Schedule(Guid documentId);
    Task RecoverPendingAsync(CancellationToken ct);
}

/// <summary>In-process per-document debounce. A newer save cancels the pending timer; duplicate execution is serialized per document.</summary>
public sealed class DocumentIndexScheduler(IServiceScopeFactory scopes, IConfiguration config, ILogger<DocumentIndexScheduler> logger) : IDocumentIndexScheduler
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly TimeSpan _delay = TimeSpan.FromMilliseconds(Math.Clamp(config.GetValue("Rag:IndexDebounceMs", 2500), 250, 60000));

    public void Schedule(Guid documentId)
    {
        var cts = new CancellationTokenSource();
        _pending.AddOrUpdate(documentId, cts, (_, old) => { old.Cancel(); old.Dispose(); return cts; });
        _ = RunAsync(documentId, cts);
    }

    public async Task RecoverPendingAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Lumina.Data.LuminaDbContext>();
        var ids = await db.Documents.Where(d => d.IndexingStatus == "Pending"
            || d.IndexingStatus == "Indexing"
            || d.IndexingStatus == "NotIndexed"
            || d.IndexedContentVersion != d.ContentVersion).Select(d => d.Id).ToListAsync(ct);
        foreach (var id in ids) Schedule(id);
    }

    private async Task RunAsync(Guid id, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_delay, cts.Token);
            var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cts.Token);
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Lumina.Data.LuminaDbContext>();
                await db.Documents.Where(d => d.Id == id && d.IndexingStatus != "Indexing").ExecuteUpdateAsync(s => s.SetProperty(d => d.IndexingStatus, "Pending"), cts.Token);
                await scope.ServiceProvider.GetRequiredService<IDocumentIndexService>().IndexAsync(id, cts.Token);
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogWarning(ex, "Background indexing attempt failed for document {DocumentId}", id); }
        finally { _pending.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(id, cts)); cts.Dispose(); }
    }
}
