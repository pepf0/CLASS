using CLASS_Blazor.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CLASS_Blazor.Services;

public sealed class UserProfileService(
    HttpClient httpClient,
    ILogger<UserProfileService> logger,
    ProfileImageStorageService profileImageStorageService)
{
    private const string UserApiUrl = "user";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UserProfileResult> GetCurrentUserAsync(string authToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return UserProfileResult.NotAuthenticated();
        }

        try
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, $"{UserApiUrl}/check_login", authToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return UserProfileResult.NotAuthenticated();
            }

            if (!response.IsSuccessStatusCode)
            {
                return UserProfileResult.Failed($"Der Login konnte nicht geprueft werden ({(int)response.StatusCode}).");
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthApiResponse>(SerializerOptions, cancellationToken);

            return authResponse?.User is null
                ? UserProfileResult.NotAuthenticated()
                : UserProfileResult.Authenticated(authResponse.User, authToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Current user could not be loaded from {UserApiUrl}/check_login.", UserApiUrl);
            return UserProfileResult.Failed($"Der Login konnte nicht geprueft werden: {exception.Message}");
        }
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

    public async Task<UserProfileResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                email = email.Trim(),
                password
            };

            using var response = await httpClient.PostAsJsonAsync($"{UserApiUrl}/login", payload, SerializerOptions, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return UserProfileResult.Failed("E-Mail oder Passwort ist falsch.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return UserProfileResult.Failed("Zu viele Loginversuche. Bitte versuche es spaeter erneut.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UserProfileResult.Failed($"Login fehlgeschlagen ({(int)response.StatusCode}).");
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthApiResponse>(SerializerOptions, cancellationToken);

            return authResponse?.User is null || string.IsNullOrWhiteSpace(authResponse.Token)
                ? UserProfileResult.Failed("Login fehlgeschlagen.")
                : UserProfileResult.Authenticated(authResponse.User, authResponse.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Login failed at {UserApiUrl}/login.", UserApiUrl);
            return UserProfileResult.Failed($"Login fehlgeschlagen: {exception.Message}");
        }
    }

    public async Task<UserProfileResult> RegisterAsync(RegistrationFormModel form, CancellationToken cancellationToken = default)
    {
        if (!form.BirthDate.HasValue)
        {
            return UserProfileResult.Failed("Bitte gib dein Geburtsdatum ein.");
        }

        if (!IsValidBirthDate(form.BirthDate.Value))
        {
            return UserProfileResult.Failed("Ungültiges Datum.");
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
                password = form.Password,
                birth_date = form.BirthDate.Value.ToString("yyyy-MM-dd"),
                description = form.Description.Trim()
            };

            using var response = await httpClient.PostAsJsonAsync($"{UserApiUrl}/create_user", payload, SerializerOptions, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return UserProfileResult.Failed("Diese E-Mail-Adresse wird bereits verwendet.");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return UserProfileResult.Failed("Die eingegebenen Registrierungsdaten sind ungueltig.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UserProfileResult.Failed($"Der Account konnte nicht erstellt werden ({(int)response.StatusCode}).");
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthApiResponse>(SerializerOptions, cancellationToken);

            if (authResponse?.User is null || string.IsNullOrWhiteSpace(authResponse.Token))
            {
                return UserProfileResult.Failed("Der Account wurde erstellt, aber der Login-Token fehlt.");
            }

            if (!string.IsNullOrWhiteSpace(form.CroppedProfileImageDataUrl))
            {
                try
                {
                    await profileImageStorageService.SaveProfileImageDataUrlAsync(
                        authResponse.User.Uid,
                        form.CroppedProfileImageDataUrl,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Profile image could not be saved for new user {UserId}.", authResponse.User.Uid);
                }
            }

            return UserProfileResult.Authenticated(authResponse.User, authResponse.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "User could not be created at {UserApiUrl}/create_user.", UserApiUrl);
            return UserProfileResult.Failed($"Der Account konnte nicht erstellt werden: {exception.Message}");
        }
    }

    private static bool IsValidBirthDate(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var earliestBirthDate = today.AddYears(-100);

        return birthDate >= earliestBirthDate && birthDate <= today;
    }

    public async Task<bool> UpdateProfileAsync(
        int userId,
        string description,
        string grade,
        string schoolType,
        string authToken,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            description = description.Trim(),
            grade = grade.Trim(),
            school_type = schoolType.Trim()
        };

        using var request = CreateAuthorizedRequest(HttpMethod.Put, $"{UserApiUrl}/{userId}", authToken);
        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("User profile update failed for user {UserId} with status {StatusCode}.", userId, response.StatusCode);
        }

        return response.IsSuccessStatusCode;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string requestUri, string authToken)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        return request;
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
    string? AuthToken,
    bool IsAuthenticated,
    string? ErrorMessage)
{
    public static UserProfileResult Authenticated(UserProfile user, string authToken)
    {
        return new(user, authToken, IsAuthenticated: true, ErrorMessage: null);
    }

    public static UserProfileResult NotAuthenticated()
    {
        return new(User: null, AuthToken: null, IsAuthenticated: false, ErrorMessage: null);
    }

    public static UserProfileResult Failed(string errorMessage)
    {
        return new(User: null, AuthToken: null, IsAuthenticated: false, ErrorMessage: errorMessage);
    }
}

internal sealed class AuthApiResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public UserProfile? User { get; set; }
}
