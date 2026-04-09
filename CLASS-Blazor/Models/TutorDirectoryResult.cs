namespace CLASS_Blazor.Models;

public sealed record TutorDirectoryResult(
    IReadOnlyList<TutorOffer> Tutors,
    bool LoadedFromApi,
    string? Message
);
