using BillingControl.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BillingControl.Controllers;

public abstract class AppController : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
    {
        var context = await next();
        var ex = context.Exception;
        if (ex is BusinessException or DbUpdateException || FinancialTransaction.IsSerializationConflict(ex))
        {
            TempData["Error"] = ex is BusinessException ? ex.Message : FinancialTransaction.IsSerializationConflict(ex) ? FinancialTransaction.ConflictMessage : "The record changed, overlaps existing data, or violates a data rule. Refresh and try again.";
            var referer = Request.Headers.Referer.ToString();
            var path = Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Authority == Request.Host.Value ? uri.PathAndQuery : "/";
            context.Result = new LocalRedirectResult(Url.IsLocalUrl(path) ? path : "/"); context.ExceptionHandled = true;
        }
    }
    protected void ValidForm()
    {
        if (ModelState.IsValid) return;
        var allocationError = ModelState
            .Where(x => x.Key.StartsWith("Allocations[", StringComparison.OrdinalIgnoreCase)
                || x.Key.StartsWith("amounts[", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Value?.Errors ?? [])
            .FirstOrDefault();
        var message = allocationError == null
            ? ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            : "Enter a valid allocation amount with no more than two decimal places.";
        throw new BusinessException(message ?? "Some values are missing or invalid. Please check the form and try again.");
    }

    protected static Dictionary<int, decimal> NormalizeAllocations(IEnumerable<KeyValuePair<int, decimal?>>? values, string emptyMessage)
    {
        var allocations = (values ?? [])
            .Where(x => x.Value.HasValue && x.Value.Value != 0)
            .ToDictionary(x => x.Key, x => x.Value!.Value);
        if (allocations.Count == 0) throw new BusinessException(emptyMessage);
        return allocations;
    }
}
