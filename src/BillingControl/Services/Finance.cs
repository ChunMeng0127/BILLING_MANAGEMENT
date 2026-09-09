using BillingControl.Models;

namespace BillingControl.Services;

public static class Finance
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static void Require(bool condition, string message) { if (!condition) throw new BusinessException(message); }
    public static void PositiveMoney(decimal value) => Require(value > 0 && value <= 9999999999.99m && Money(value) == value, "Amount must be positive, at most RM9,999,999,999.99, with no more than two decimal places.");
    public static void Percentage(decimal p) => Require(p >= 0 && p <= 100 && decimal.Round(p, 4) == p, "Percentages must be between 0 and 100, with at most four decimal places.");
    public static decimal[] Split(decimal amount, params decimal[] percentages)
    {
        PositiveMoney(amount);
        foreach (var p in percentages) Percentage(p);
        Require(percentages.Sum() == 100m, "Revenue-sharing percentages must total exactly 100%.");
        // Largest-remainder cent allocation: exact total, nonnegative shares, deterministic ties.
        var raw = percentages.Select(p => amount * p / 100m).ToArray();
        var result = raw.Select(x => decimal.Floor(x * 100m) / 100m).ToArray();
        var cents = (int)((amount - result.Sum()) * 100m);
        foreach (var i in Enumerable.Range(0, raw.Length).OrderByDescending(i => raw[i] - result[i]).ThenBy(i => i).Take(cents)) result[i] += .01m;
        return result;
    }
    public static decimal WorkerEntitlement(decimal lcmGross, decimal percent)
    { Percentage(percent); Require(lcmGross >= 0, "LCM gross share cannot be negative."); return Money(lcmGross * percent / 100m); }
    public static void ValidateAllocation(decimal amount, decimal entitlement, decimal alreadyPaid)
    {
        PositiveMoney(amount);
        var unpaid = entitlement - alreadyPaid;
        Require(unpaid > 0, "This assignment has no unpaid worker entitlement.");
        Require(amount <= unpaid, "Allocation exceeds this assignment's unpaid entitlement.");
    }
    public static int Months(Frequency frequency) => frequency switch { Frequency.Monthly => 1, Frequency.Every2Months => 2, Frequency.Quarterly => 3, Frequency.HalfYearly => 6, Frequency.Yearly => 12, _ => 0 };
    public static DateOnly Next(DateOnly start, Frequency frequency, int anchorDay)
    {
        var date = start.AddMonths(Months(frequency));
        return new(date.Year, date.Month, Math.Min(anchorDay, DateTime.DaysInMonth(date.Year, date.Month)));
    }
    public static string Label(object value) => value switch
    {
        Frequency.Every2Months => "Every 2 Months",
        Frequency.HalfYearly => "Half-Yearly",
        Frequency.OneOff => "One-Off",
        Frequency.AdHoc => "Ad-Hoc",
        BillingStatus.WorkInProgress => "Work In Progress",
        BillingStatus.ReadyToBill => "Ready to Bill",
        BillingStatus.PartiallyPaid => "Partially Paid",
        BillingInvoiceState.PartiallyInvoiced => "Partially Invoiced",
        BillingInvoiceState.FullyInvoiced => "Fully Invoiced",
        WorkStatus.InProgress => "In Progress",
        ProgressStatus.NotStarted => "Not Started",
        ProgressStatus.InProgress => "In Progress",
        ProgressStatus.Blocked => "Blocked",
        ProgressStatus.Completed => "Completed",
        InvoiceFlow.AccountingFirmToCustomer => "Accounting Firm → Customer",
        InvoiceFlow.ManagerToAccountingFirm => "Manager → Accounting Firm",
        InvoiceFlow.LcmToManager => "LCM MGT → Manager",
        InvoiceStatus.Issued => "Issued",
        InvoiceStatus.PartiallyPaid => "Partially Paid",
        InvoiceStatus.Paid => "Paid",
        InvoiceStatus.Cancelled => "Cancelled",
        _ => value.ToString() ?? ""
    };
}
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
    public BusinessException(string message, Exception innerException) : base(message, innerException) { }
}
