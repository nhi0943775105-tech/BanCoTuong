using System.Security.Cryptography;
using _23DTHD5_Demo.Models;
using Microsoft.Extensions.Caching.Memory;

namespace _23DTHD5_Demo.Services;

// Demo OTP only. No email is sent. Pending passwords are hashed before entering this store.
public class PendingAuthStore(IMemoryCache cache)
{
    public const string DemoOtp = "123456";
    public const int OtpLifetimeSeconds = 120;
    private static string CookieName(string purpose) => $"HongKy.Pending.{purpose}";

    public void Set(HttpContext context, string purpose, PendingAuth state)
    {
        Remove(context, purpose);
        var key = RandomNumberGenerator.GetHexString(32);
        cache.Set(key, state, TimeSpan.FromMinutes(10));
        context.Response.Cookies.Append(CookieName(purpose), key, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromMinutes(10)
        });
    }

    public PendingAuth? Get(HttpContext context, string purpose)
    {
        var key = context.Request.Cookies[CookieName(purpose)];
        return key is not null && cache.TryGetValue(key, out PendingAuth? state) ? state : null;
    }

    public void Remove(HttpContext context, string purpose)
    {
        var key = context.Request.Cookies[CookieName(purpose)];
        if (key is not null) cache.Remove(key);
        context.Response.Cookies.Delete(CookieName(purpose), new CookieOptions { Path = "/api/auth" });
    }
}

public class PendingAuth
{
    public ApplicationUser? RegistrationUser { get; init; }
    public string? ResetUserId { get; init; }
    public string? ResetToken { get; init; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddSeconds(PendingAuthStore.OtpLifetimeSeconds);
    public int Attempts { get; set; }
    public int Resends { get; set; }
    public bool Consumed { get; set; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
