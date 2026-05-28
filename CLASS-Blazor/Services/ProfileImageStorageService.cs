using System.Text.RegularExpressions;

namespace CLASS_Blazor.Services;

public sealed partial class ProfileImageStorageService(IWebHostEnvironment environment)
{
    private const string ProfileImageDirectory = "images/user";
    private const int MaxProfileImageBytes = 2 * 1024 * 1024;

    public string GetProfileImageUrl(int userId)
    {
        if (userId <= 0)
        {
            return string.Empty;
        }

        var path = GetProfileImagePath(userId);

        if (!File.Exists(path))
        {
            return string.Empty;
        }

        var version = File.GetLastWriteTimeUtc(path).Ticks;
        return $"/images/user/{userId}.webp?v={version}";
    }

    public async Task<string> SaveProfileImageDataUrlAsync(
        int userId,
        string imageDataUrl,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("Das Profilbild kann keinem User zugeordnet werden.");
        }

        var match = WebpDataUrlRegex().Match(imageDataUrl);

        if (!match.Success)
        {
            throw new InvalidOperationException("Das Profilbild muss als WebP gespeichert werden.");
        }

        var imageBytes = Convert.FromBase64String(match.Groups["data"].Value);

        if (imageBytes.Length == 0 || imageBytes.Length > MaxProfileImageBytes)
        {
            throw new InvalidOperationException("Das Profilbild ist zu groß.");
        }

        var directory = GetProfileImageDirectoryPath();
        Directory.CreateDirectory(directory);

        var path = GetProfileImagePath(userId);
        await File.WriteAllBytesAsync(path, imageBytes, cancellationToken);

        return GetProfileImageUrl(userId);
    }

    private string GetProfileImagePath(int userId)
    {
        return Path.Combine(GetProfileImageDirectoryPath(), $"{userId}.webp");
    }

    private string GetProfileImageDirectoryPath()
    {
        return Path.Combine(environment.WebRootPath, ProfileImageDirectory.Replace('/', Path.DirectorySeparatorChar));
    }

    [GeneratedRegex("^data:image/webp;base64,(?<data>[A-Za-z0-9+/=]+)$", RegexOptions.Compiled)]
    private static partial Regex WebpDataUrlRegex();
}
