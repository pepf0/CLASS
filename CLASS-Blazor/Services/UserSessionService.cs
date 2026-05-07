using CLASS_Blazor.Models;

namespace CLASS_Blazor.Services;

public sealed class UserSessionService
{
    public UserProfile? CurrentUser { get; private set; }

    public string ProfileImageDataUrl { get; private set; } = string.Empty;

    public bool IsLoggedIn => CurrentUser is not null;

    public void SignIn(UserProfile user, string profileImageDataUrl = "")
    {
        CurrentUser = user;
        ProfileImageDataUrl = profileImageDataUrl;
    }

    public void SignOut()
    {
        CurrentUser = null;
        ProfileImageDataUrl = string.Empty;
    }
}
