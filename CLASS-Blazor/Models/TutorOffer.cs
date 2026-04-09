namespace CLASS_Blazor.Models;

public sealed record TutorOffer(
    string Name,
    int Age,
    string Email,
    string Description,
    string SchoolInfo,
    string[] Subjects,
    decimal Rating,
    int ReviewCount,
    int PricePerHour,
    DateOnly ExpiresOn,
    string ImageUrl
);
