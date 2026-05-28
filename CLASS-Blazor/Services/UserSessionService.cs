using CLASS_Blazor.Models;

namespace CLASS_Blazor.Services;

public sealed class UserSessionService(ProfileImageStorageService profileImageStorageService)
{
    public UserProfile? CurrentUser { get; private set; }

    public string AuthToken { get; private set; } = string.Empty;

    public string ProfileImageUrl { get; private set; } = string.Empty;

    public bool IsLoggedIn => CurrentUser is not null;

    public void SignIn(UserProfile user, string authToken = "", string profileImageUrl = "")
    {
        CurrentUser = user;
        AuthToken = authToken;
        ProfileImageUrl = string.IsNullOrWhiteSpace(profileImageUrl)
            ? profileImageStorageService.GetProfileImageUrl(user.Uid)
            : profileImageUrl;
    }

    public void UpdateProfileImageUrl(string profileImageUrl)
    {
        ProfileImageUrl = profileImageUrl;
    }

    public void SignOut()
    {
        CurrentUser = null;
        AuthToken = string.Empty;
        ProfileImageUrl = string.Empty;
    }
}
