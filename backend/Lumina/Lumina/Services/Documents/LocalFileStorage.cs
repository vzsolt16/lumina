namespace Lumina.Services.Documents;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["Storage:LocalRootPath"];
        _rootPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(environment.ContentRootPath, "data", "documents")
                : configuredPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = GetSafePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await content.CopyToAsync(output, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSafePath(storageKey);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSafePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetSafePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
        {
            throw new InvalidOperationException("Invalid file storage key.");
        }

        var path = Path.GetFullPath(Path.Combine(_rootPath, storageKey));
        if (!path.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid file storage key.");
        }

        return path;
    }
}
