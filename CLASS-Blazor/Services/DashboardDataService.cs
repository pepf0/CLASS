using CLASS_Blazor.Models;

namespace CLASS_Blazor.Services;

public sealed class DashboardDataService
{
    public Task<DashboardSnapshot> GetDashboardAsync()
    {
        var snapshot = new DashboardSnapshot(
            BookedHours: 9,
            CompletedHours: 7,
            ActiveOffers: 1,
            TotalRevenue: 640m,
            CurrentMonthRevenue: 126m,
            HasOffer: true,
            RevenueByMonth:
            [
                new("Jan", 72m),
                new("Feb", 96m),
                new("Mar", 126m),
                new("Apr", 126m),
                new("Mai", 0m),
                new("Jun", 0m),
                new("Jul", 0m),
                new("Aug", 0m),
                new("Sep", 0m),
                new("Okt", 0m),
                new("Nov", 0m),
                new("Dez", 0m)
            ],
            HoursByMonth:
            [
                new("Nov", 3m),
                new("Dez", 6m),
                new("Jan", 4m),
                new("Feb", 5m),
                new("Mar", 7m),
                new("Apr", 7m)
            ],
            SubjectHours:
            [
                new("INSY", 3m, "#7c3aed"),
                new("MEDT", 2m, "#0ea5e9"),
                new("Deutsch", 1.5m, "#10b981"),
                new("SEW", 0.5m, "#f97316")
            ],
            OpenRequests:
            [
                new(Guid.NewGuid(), "Mira Novak", "INSY", 18, DateOnly.FromDateTime(DateTime.Today.AddDays(-1))),
                new(Guid.NewGuid(), "Jonas Berger", "MEDT", 20, DateOnly.FromDateTime(DateTime.Today.AddDays(-2))),
                new(Guid.NewGuid(), "Aylin Demir", "Deutsch", 16, DateOnly.FromDateTime(DateTime.Today.AddDays(-3)))
            ],
            Activities:
            [
                new(Guid.NewGuid(), "Neue Anfrage", "Mira m\u00f6chte INSY-Nachhilfe buchen.", DateTime.Now.AddMinutes(-18), "info"),
                new(Guid.NewGuid(), "Stunde abgeschlossen", "Deutsch-Einheit mit Leon wurde markiert.", DateTime.Now.AddHours(-2), "success"),
                new(Guid.NewGuid(), "Anfrage angenommen", "MEDT-Termin f\u00fcr morgen wurde best\u00e4tigt.", DateTime.Now.AddHours(-5), "success"),
                new(Guid.NewGuid(), "Angebot aktualisiert", "Preis und F\u00e4cherauswahl wurden gespeichert.", DateTime.Now.AddDays(-1), "neutral")
            ]);

        return Task.FromResult(snapshot);
    }
}
