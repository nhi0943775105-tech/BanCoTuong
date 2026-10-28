using System.Security.Claims;
using _23DTHD5_Demo.Data;
using _23DTHD5_Demo.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace _23DTHD5_Demo.Services;

public class GoogleAccountService(ApplicationDbContext database, UserManager<ApplicationUser> users)
{
    public async Task<ApplicationUser?> CreateAsync(ExternalLoginInfo info)
    {
        var email = info.Principal.FindFirstValue(ClaimTypes.Email)!;
        var displayName = info.Principal.FindFirstValue(ClaimTypes.Name)?.Trim() ?? "Kỳ thủ";
        if (displayName.Length > 30) displayName = displayName[..30];
        if (displayName.Length > 0 && char.IsHighSurrogate(displayName[^1])) displayName = displayName[..^1];
        if (displayName.Length < 2) displayName = "Kỳ thủ";
        var user = new ApplicationUser
        {
            UserName = "gg" + Guid.NewGuid().ToString("N")[..16],
            DisplayName = displayName,
            Email = email,
            EmailConfirmed = true
        };

        // Account and provider link must succeed together, including simultaneous first sign-ins.
        await using var transaction = await database.Database.BeginTransactionAsync();
        var created = await users.CreateAsync(user);
        if (!created.Succeeded) return null;
        var linked = await users.AddLoginAsync(user, info);
        if (!linked.Succeeded) return null;
        await transaction.CommitAsync();
        return user;
    }
}
