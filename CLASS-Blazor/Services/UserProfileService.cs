using CLASS_Blazor.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CLASS_Blazor.Services;

public sealed class UserProfileService(
    HttpClient httpClient,
    ILogger<UserProfileService> logger)
{
    private const string UserApiUrl = "api/class/user";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UserProfileResult> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var usersResult = await GetUsersAsync(cancellationToken);

        if (!usersResult.Success)
        {
            return UserProfileResult.Failed(usersResult.ErrorMessage ?? "Die User konnten nicht geladen werden.");
        }

        return UserProfileResult.NotAuthenticated();
    }

    public async Task<UserListResult> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(UserApiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return UserListResult.Failed($"Die User konnten nicht geladen werden ({(int)response.StatusCode}).");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return UserListResult.Loaded(ParseUsers(json));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Users could not be loaded from {UserApiUrl}.", UserApiUrl);
            return UserListResult.Failed($"Die User konnten nicht geladen werden: {exception.Message}");
        }
    }

    public async Task<UserProfileResult> LoginAsync(string email, CancellationToken cancellationToken = default)
    {
        var usersResult = await GetUsersAsync(cancellationToken);

        if (!usersResult.Success)
        {
            return UserProfileResult.Failed(usersResult.ErrorMessage ?? "Login fehlgeschlagen.");
        }

        var user = usersResult.Users.FirstOrDefault(user =>
            string.Equals(user.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

        return user is null
            ? UserProfileResult.Failed("Für diese E-Mail-Adresse wurde kein Account gefunden.")
            : UserProfileResult.Authenticated(user);
    }

    public async Task<UserProfileResult> RegisterAsync(RegistrationFormModel form, CancellationToken cancellationToken = default)
    {
        var usersResult = await GetUsersAsync(cancellationToken);

        if (!usersResult.Success)
        {
            return UserProfileResult.Failed(usersResult.ErrorMessage ?? "Registrierung fehlgeschlagen.");
        }

        if (usersResult.Users.Any(user => string.Equals(user.Email.Trim(), form.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return UserProfileResult.Failed("Diese E-Mail-Adresse wird bereits verwendet.");
        }

        if (!form.BirthDate.HasValue)
        {
            return UserProfileResult.Failed("Bitte gib dein Geburtsdatum ein.");
        }

        try
        {
            var payload = new
            {
                first_name = form.FirstName.Trim(),
                last_name = form.LastName.Trim(),
                grade = form.Grade.Trim(),
                school_type = form.SchoolType.Trim(),
                email = form.Email.Trim(),
                password_hash = form.Password,
                birth_date = form.BirthDate.Value.ToString("yyyy-MM-dd")
            };

            using var response = await httpClient.PostAsJsonAsync(UserApiUrl, payload, SerializerOptions, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return UserProfileResult.Failed("Diese E-Mail-Adresse wird bereits verwendet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UserProfileResult.Failed($"Der Account konnte nicht erstellt werden ({(int)response.StatusCode}).");
            }

            var createdUserResult = await LoginAsync(form.Email, cancellationToken);

            if (!createdUserResult.IsAuthenticated || createdUserResult.User is null)
            {
                return createdUserResult;
            }

            if (!string.IsNullOrWhiteSpace(form.Description))
            {
                await UpdateProfileAsync(createdUserResult.User.Uid, form.Description, form.Grade, form.SchoolType, cancellationToken);
                createdUserResult.User.Description = form.Description.Trim();
            }

            return createdUserResult;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "User could not be created at {UserApiUrl}.", UserApiUrl);
            return UserProfileResult.Failed($"Der Account konnte nicht erstellt werden: {exception.Message}");
        }
    }

    private async Task UpdateProfileAsync(int userId, string description, string grade, string schoolType, CancellationToken cancellationToken)
    {
        var payload = new
        {
            description = description.Trim(),
            grade = grade.Trim(),
            school_type = schoolType.Trim()
        };

        using var response = await httpClient.PutAsJsonAsync($"{UserApiUrl}/{userId}", payload, SerializerOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("User profile update failed for user {UserId} with status {StatusCode}.", userId, response.StatusCode);
        }
    }

    private static IReadOnlyList<UserProfile> ParseUsers(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<UserProfile>>(json, SerializerOptions) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<UserProfile>>(valueElement.GetRawText(), SerializerOptions) ?? [];
        }

        var singleUser = document.RootElement.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<UserProfile>(json, SerializerOptions)
            : null;

        return singleUser is null ? [] : [singleUser];
    }
}

public sealed record UserListResult(
    IReadOnlyList<UserProfile> Users,
    bool Success,
    string? ErrorMessage)
{
    public static UserListResult Loaded(IReadOnlyList<UserProfile> users)
    {
        return new(users, Success: true, ErrorMessage: null);
    }

    public static UserListResult Failed(string errorMessage)
    {
        return new([], Success: false, errorMessage);
    }
}

public sealed record UserProfileResult(
    UserProfile? User,
    bool IsAuthenticated,
    string? ErrorMessage)
{
    public static UserProfileResult Authenticated(UserProfile user)
    {
        return new(user, IsAuthenticated: true, ErrorMessage: null);
    }

    public static UserProfileResult NotAuthenticated()
    {
        return new(User: null, IsAuthenticated: false, ErrorMessage: null);
    }

    public static UserProfileResult Failed(string errorMessage)
    {
        return new(User: null, IsAuthenticated: false, ErrorMessage: errorMessage);
    }
}
