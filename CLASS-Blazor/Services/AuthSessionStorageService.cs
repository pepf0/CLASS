using Microsoft.JSInterop;

namespace CLASS_Blazor.Services;

public sealed class AuthSessionStorageService(
    IHttpContextAccessor httpContextAccessor,
    IJSRuntime js,
    UserProfileService userProfileService,
    UserSessionService userSessionService,
    ILogger<AuthSessionStorageService> logger)
{
    public const string AuthCookieName = "class_auth_token";

    private Task<bool>? restoreTask;

    public Task<bool> RestoreFromCookieAsync()
    {
        if (userSessionService.IsLoggedIn)
        {
            return Task.FromResult(true);
        }

        restoreTask ??= RestoreFromCookieCoreAsync();

        return restoreTask;
    }

    public async ValueTask SaveTokenAsync(string authToken)
    {
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return;
        }

        await js.InvokeVoidAsync("authSession.setToken", authToken);
    }

    public async ValueTask ClearTokenAsync()
    {
        await js.InvokeVoidAsync("authSession.clearToken");
    }

    private async Task<bool> RestoreFromCookieCoreAsync()
    {
        var authToken = httpContextAccessor.HttpContext?.Request.Cookies[AuthCookieName];

        if (string.IsNullOrWhiteSpace(authToken))
        {
            return false;
        }

        try
        {
            var result = await userProfileService.GetCurrentUserAsync(authToken);

            if (result.IsAuthenticated && result.User is not null)
            {
                userSessionService.SignIn(result.User, result.AuthToken ?? authToken);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                logger.LogWarning("Persisted login could not be restored: {ErrorMessage}", result.ErrorMessage);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Persisted login could not be restored.");
        }

        return false;
    }
}
