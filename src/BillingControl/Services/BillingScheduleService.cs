using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Services;

public sealed class BillingScheduleService(AppDbContext db)
{
    public static DateOnly? NormalizeNextPeriodStart(Frequency frequency, DateOnly? nextPeriodStart, bool isNew, DateOnly startDate) =>
        frequency == Frequency.AdHoc ? null : nextPeriodStart ?? (isNew ? startDate : null);

    public static bool IsChanged(BillingSchedule schedule, Frequency frequency, DateOnly? nextPeriodStart, int anchorDay) =>
        schedule.Frequency != frequency || schedule.NextPeriodStart != nextPeriodStart || schedule.AnchorDay != anchorDay;

    public async Task ValidateAsync(Engagement engagement, Frequency frequency, DateOnly? nextPeriodStart, int anchorDay, bool checkOverlap = true)
    {
        Finance.Require(Enum.IsDefined(frequency), "Select a valid billing frequency.");
        Finance.Require(anchorDay is >= 1 and <= 31, "The recurring day must be between 1 and 31.");
        Finance.Require(nextPeriodStart == null || (nextPeriodStart >= engagement.StartDate && (engagement.EndDate == null || nextPeriodStart <= engagement.EndDate)), "Next service period must be inside the engagement dates.");
        if (!checkOverlap || nextPeriodStart == null || frequency == Frequency.AdHoc) return;

        var periodEnd = Finance.Months(frequency) > 0
            ? Finance.Next(nextPeriodStart.Value, frequency, anchorDay).AddDays(-1)
            : (engagement.EndDate ?? nextPeriodStart.Value);
        if (engagement.EndDate is { } endDate && periodEnd > endDate) periodEnd = endDate;
        var overlaps = await db.BillingRecords.AnyAsync(x =>
            x.EngagementId == engagement.Id &&
            x.Status != BillingStatus.Cancelled &&
            x.PeriodStart <= periodEnd && x.PeriodEnd >= nextPeriodStart);
        Finance.Require(!overlaps, "The next scheduled period overlaps an existing billing record. Choose a later date.");
    }
}
