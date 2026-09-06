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
        if (ex is BusinessException or DbUpdateException || ex is PostgresException { SqlState: "40001" or "40P01" })
        {
            TempData["Error"] = ex is BusinessException ? ex.Message : "The record changed, overlaps existing data, or violates a data rule. Refresh and try again.";
            var referer = Request.Headers.Referer.ToString();
            var path = Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Authority == Request.Host.Value ? uri.PathAndQuery : "/";
            context.Result = new LocalRedirectResult(Url.IsLocalUrl(path) ? path : "/"); context.ExceptionHandled = true;
        }
    }
    protected void ValidForm() { if (!ModelState.IsValid) throw new BusinessException("Some values are missing or invalid. Please check the form and try again."); }
}
