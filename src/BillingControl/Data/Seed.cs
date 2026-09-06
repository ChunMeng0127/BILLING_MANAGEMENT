using BillingControl.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Data;

public static class Seed
{
    public static async Task Initialize(IServiceProvider services, IConfiguration config)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "User" }) if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new(role)));
        var users = services.GetRequiredService<UserManager<AppUser>>();
        if (!await users.Users.AnyAsync())
        {
            var email = config["BootstrapAdmin:Email"] ?? throw new InvalidOperationException("Set BootstrapAdmin__Email for the first migration.");
            var password = config["BootstrapAdmin:Password"] ?? throw new InvalidOperationException("Set BootstrapAdmin__Password for the first migration.");
            var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
            Check(await users.CreateAsync(admin, password)); Check(await users.AddToRoleAsync(admin, "Admin"));
        }
        if (!config.GetValue<bool>("Seed:Demo")) return;
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.Customers.AnyAsync()) return;
        var customer = new Customer { Name = "Sample Trading Sdn Bhd", RegistrationNumber = "DEMO-001", Notes = "Sample data — replace before operational use." };
        var service = new Service { Name = "Monthly bookkeeping" };
        var firm = new BusinessParty { Name = "X Group" };
        var manager = new Manager { Name = "Signitive PLT" };
        db.Workers.Add(new() { Name = "Sample Worker", Type = WorkerType.Freelancer });
        var start = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        db.Engagements.Add(new() { Customer = customer, Service = service, BusinessParty = firm, Manager = manager, StartDate = start, BillingAmount = 1000m, Schedule = new() { Frequency = Frequency.Monthly, AnchorDay = 1, NextPeriodStart = start } });
        await db.SaveChangesAsync();
    }
    public static void Check(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description))); }
}
