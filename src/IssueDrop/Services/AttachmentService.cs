using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;
using IssueDrop.Infrastructure;
using IssueDrop.Models;
using Clipboard = System.Windows.Clipboard;

namespace IssueDrop.Services;

public sealed class AttachmentService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".gif", ".jpg", ".jpeg", ".svg" };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".jpg", ".jpeg", ".svg", ".mp4", ".mov", ".webm",
        ".pdf", ".docx", ".pptx", ".xlsx", ".xls", ".xlsm", ".odt", ".ods", ".odp",
        ".rtf", ".doc", ".txt", ".md", ".csv", ".tsv", ".log", ".json", ".jsonc",
        ".c", ".cs", ".cpp", ".css", ".drawio", ".dmp", ".html", ".htm", ".java",
        ".js", ".ipynb", ".patch", ".php", ".cpuprofile", ".pdb", ".py", ".sh",
        ".sql", ".ts", ".tsx", ".xml", ".yaml", ".yml", ".zip", ".gz", ".tgz"
    };

    public async Task<IReadOnlyList<AttachmentItem>> AddFromClipboardAsync(Guid draftId)
    {
        var results = new List<AttachmentItem>();
        if (Clipboard.ContainsImage())
        {
            var image = Clipboard.GetImage();
            if (image is not null)
            {
                var name = $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.png";
                var path = GetUniqueDestination(draftId, name);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                encoder.Save(stream);
                results.Add(CreateItem(path, name));
            }
        }
        else if (Clipboard.ContainsFileDropList())
        {
            StringCollection files = Clipboard.GetFileDropList();
            results.AddRange(await AddFilesAsync(draftId, files.Cast<string>()));
        }
        return results;
    }

    public async Task<IReadOnlyList<AttachmentItem>> AddFilesAsync(Guid draftId, IEnumerable<string> filePaths)
    {
        var results = new List<AttachmentItem>();
        foreach (var source in filePaths.Where(File.Exists))
        {
            var info = new FileInfo(source);
            var extension = info.Extension;
            if (!SupportedExtensions.Contains(extension))
                throw new InvalidOperationException($"{info.Name} is not a supported GitHub attachment type.");
            var limit = ImageExtensions.Contains(extension) ? 10L * 1024 * 1024 : 25L * 1024 * 1024;
            if (info.Length > limit)
                throw new InvalidOperationException($"{info.Name} exceeds GitHub's {limit / 1024 / 1024} MB limit.");

            var destination = GetUniqueDestination(draftId, info.Name);
            await using var input = File.OpenRead(source);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output);
            results.Add(CreateItem(destination, Path.GetFileName(destination)));
        }
        return results;
    }

    public void Remove(AttachmentItem attachment)
    {
        try { if (File.Exists(attachment.LocalPath)) File.Delete(attachment.LocalPath); }
        catch (IOException) { }
    }

    private static string GetUniqueDestination(Guid draftId, string originalName)
    {
        var directory = Path.Combine(AppPaths.Attachments, draftId.ToString("N"));
        Directory.CreateDirectory(directory);
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(originalName.Select(c => invalid.Contains(c) ? '_' : c));
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(directory, $"{stem}-{suffix}{Path.GetExtension(safeName)}");
    }

    private static AttachmentItem CreateItem(string path, string fileName)
    {
        var info = new FileInfo(path);
        var extension = info.Extension;
        return new AttachmentItem
        {
            FileName = fileName,
            LocalPath = path,
            Size = info.Length,
            IsImage = ImageExtensions.Contains(extension),
            ContentType = extension.ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
                ".svg" => "image/svg+xml", ".pdf" => "application/pdf", _ => "application/octet-stream"
            }
        };
    }
}
