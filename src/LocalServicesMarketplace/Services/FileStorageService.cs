using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Services;

/// <summary>
/// Stores uploads on disk under Storage:Root.
///   public/  -> served at /uploads (profile photos only)
///   private/ -> never served statically; verification documents are streamed by an authorised action
/// Files are validated by size, extension AND magic bytes, and saved under random names.
/// </summary>
public class FileStorageService : IFileStorageService
{
    private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        [".jpeg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        [".png"] = ("image/png", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
        [".webp"] = ("image/webp", [[0x52, 0x49, 0x46, 0x46]])
    };

    private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> DocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", [[0x25, 0x50, 0x44, 0x46]]),
        [".jpg"] = ImageTypes[".jpg"],
        [".jpeg"] = ImageTypes[".jpeg"],
        [".png"] = ImageTypes[".png"]
    };

    private readonly StorageOptions _options;
    private readonly string _publicRoot;
    private readonly string _privateRoot;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(IOptions<StorageOptions> options, IWebHostEnvironment env, ILogger<FileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        var root = Path.IsPathRooted(_options.Root) ? _options.Root : Path.Combine(env.ContentRootPath, _options.Root);
        _publicRoot = Path.Combine(root, "public");
        _privateRoot = Path.Combine(root, "private", "verification");
        Directory.CreateDirectory(Path.Combine(_publicRoot, "profiles"));
        Directory.CreateDirectory(_privateRoot);
    }

    public string PublicRoot => _publicRoot;

    public async Task<ServiceResult<string>> SaveProfileImageAsync(IFormFile file, CancellationToken ct = default)
    {
        var check = await ValidateAsync(file, ImageTypes, _options.MaxProfileImageBytes, ct);
        if (!check.Succeeded) return ServiceResult<string>.Fail(check.Error!);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var name = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(_publicRoot, "profiles", name);
        await using (var stream = File.Create(path))
        {
            await file.CopyToAsync(stream, ct);
        }
        return ServiceResult<string>.Ok($"/uploads/profiles/{name}");
    }

    public void DeletePublicFile(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl) || !relativeUrl.StartsWith("/uploads/profiles/", StringComparison.Ordinal)) return;
        var fileName = Path.GetFileName(relativeUrl);
        var path = Path.Combine(_publicRoot, "profiles", fileName);
        TryDelete(path);
    }

    public async Task<ServiceResult<(string StoredName, string ContentType)>> SavePrivateDocumentAsync(IFormFile file, CancellationToken ct = default)
    {
        var check = await ValidateAsync(file, DocumentTypes, _options.MaxDocumentBytes, ct);
        if (!check.Succeeded) return ServiceResult<(string, string)>.Fail(check.Error!);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var name = $"{Guid.NewGuid():N}{ext}";
        await using (var stream = File.Create(Path.Combine(_privateRoot, name)))
        {
            await file.CopyToAsync(stream, ct);
        }
        return ServiceResult<(string, string)>.Ok((name, DocumentTypes[ext].ContentType));
    }

    public Stream? OpenPrivateDocument(string storedName)
    {
        // storedName comes from the database, but never trust it as a path.
        var safe = Path.GetFileName(storedName);
        var path = Path.Combine(_privateRoot, safe);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void DeletePrivateDocument(string storedName) => TryDelete(Path.Combine(_privateRoot, Path.GetFileName(storedName)));

    private static async Task<ServiceResult> ValidateAsync(IFormFile? file, Dictionary<string, (string ContentType, byte[][] Signatures)> allowed, long maxBytes, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ServiceResult.Fail("Choose a file to upload.");
        if (file.Length > maxBytes)
            return ServiceResult.Fail($"The file is too large. The limit is {maxBytes / (1024 * 1024)} MB.");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !allowed.TryGetValue(ext, out var type))
            return ServiceResult.Fail($"Only {string.Join(", ", allowed.Keys.Select(k => k.TrimStart('.').ToUpperInvariant()).Distinct())} files are accepted.");

        var header = new byte[8];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);
        var matches = type.Signatures.Any(sig => read >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig));
        return matches
            ? ServiceResult.Ok()
            : ServiceResult.Fail("The file content does not match its extension.");
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete file {Path}", path);
        }
    }
}
