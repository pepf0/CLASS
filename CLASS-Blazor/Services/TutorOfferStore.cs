using CLASS_Blazor.Models;

namespace CLASS_Blazor.Services;

public sealed class TutorOfferStore
{
    private readonly List<TutorOffer> tutors;

    public TutorOfferStore()
    {
        CurrentTutor = new TutorAccount(
            "Dominik Kautz",
            17,
            "kautz.d23@htlwienwest.at",
            "3AHIT, Tagesschule",
            "/images/tutors/tutor-2.png");

        tutors =
        [
            new(CurrentTutor.Name, CurrentTutor.Age, CurrentTutor.Email, "Ich bin Dominik und unterstütze dich kurz und verständlich bei MEDT, INSY und Deutsch.", CurrentTutor.SchoolInfo, ["MEDT", "INSY", "Deutsch"], 3.9m, 14, 17, new DateOnly(2026, 5, 29), CurrentTutor.ImageUrl),
            new("Noel Hollnthoner", 16, "hollnthoner.n23@htlwienwest.at", "Mein Name ist Noel und ich helfe dir gerne bei SEW, Ethik und Sport.", "3AHIT, Tagesschule", ["SEW", "Ethik", "Sport"], 4.2m, 10, 15, new DateOnly(2026, 5, 24), "/images/tutors/tutor-4.png"),
            new("Alisandro Gourie", 18, "gourie.a23@htlwienwest.at", "Ich bin Alisandro und erkläre dir SEW, NWT und INSY Schritt für Schritt.", "3AHIT, Tagesschule", ["SEW", "NWT", "INSY"], 4.5m, 17, 18, new DateOnly(2026, 6, 9), "/images/tutors/tutor-5.png"),
            new("Eljon Bacaj", 19, "bacaj.e23@htlwienwest.at", "Ich bin Eljon und unterstütze dich mit ruhigen Erklärungen in SEW, MEDT und INSY.", "3AHIT, Tagesschule", ["SEW", "MEDT", "INSY"], 4.8m, 23, 19, new DateOnly(2026, 5, 18), "/images/tutors/tutor-6.png"),
            new("Bekir Dayi", 16, "dayi.b23@htlwienwest.at", "Mein Name ist Bekir und ich motiviere dich gerne im Fach Sport.", "3AHIT, Tagesschule", ["Sport"], 5.0m, 11, 16, new DateOnly(2026, 6, 17), "/images/tutors/tutor-7.png"),
        ];
    }

    public TutorAccount CurrentTutor { get; }

    public IReadOnlyList<TutorOffer> Tutors => tutors;

    public TutorOffer GetCurrentTutorOffer()
    {
        return tutors.First(tutor => string.Equals(tutor.Email, CurrentTutor.Email, StringComparison.OrdinalIgnoreCase));
    }

    public void SaveCurrentTutorOffer(TutorOfferFormModel form)
    {
        var existingOffer = GetCurrentTutorOffer();
        var updatedOffer = existingOffer with
        {
            Description = form.Description.Trim(),
            Subjects = form.Subjects
                .Where(subject => !string.IsNullOrWhiteSpace(subject))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PricePerHour = form.PricePerHour,
            ExpiresOn = form.ExpiresOn ?? existingOffer.ExpiresOn
        };

        var index = tutors.FindIndex(tutor => string.Equals(tutor.Email, CurrentTutor.Email, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            tutors[index] = updatedOffer;
        }
    }
}
