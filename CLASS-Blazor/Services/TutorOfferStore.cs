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
            "Tagesschule",
            "/images/tutors/tutor-2.png");

        tutors =
        [
            new(
                CurrentTutor.Name,
                CurrentTutor.Age,
                CurrentTutor.Email,
                "Ich helfe euch mit vielen verschiedenen Fächern.",
                CurrentTutor.SchoolInfo,
                ["Systemtechnik (ET)", "Systemtechnik (IT)"],
                4.0m,
                8,
                120,
                new DateOnly(2026, 4, 23),
                CurrentTutor.ImageUrl),
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
