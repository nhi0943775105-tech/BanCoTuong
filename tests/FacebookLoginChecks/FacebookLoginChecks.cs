using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using _23DTHD5_Demo.Data;
using _23DTHD5_Demo.Models;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Exercises Microsoft's actual OAuth handler with a fake backchannel. Never contacts Facebook.
// Uses SQL Server and cleans only accounts with a unique, per-run email suffix.
internal static class FacebookLoginChecks
{
    private static int checks;
    private static readonly string suffix = "@" + Guid.NewGuid().ToString("N") + ".example.test";

    public static async Task Main()
    {
        var oldId = Environment.GetEnvironmentVariable("Authentication__Facebook__AppId");
        var oldSecret = Environment.GetEnvironmentVariable("Authentication__Facebook__AppSecret");
        try
        {
            Environment.SetEnvironmentVariable("Authentication__Facebook__AppId", " ");
            Environment.SetEnvironmentVariable("Authentication__Facebook__AppSecret", " ");
            using (var disabled = Factory())
            using (var client = Client(disabled))
            {
                var status = await client.GetFromJsonAsync<JsonElement>("/api/auth/facebook/status");
                Check(!status.GetProperty("enabled").GetBoolean(), "Facebook is disabled without credentials");
                using var response = await Post(client, "/api/auth/facebook/start");
                Check(response.Headers.Location?.OriginalString == "/?authError=facebook_not_configured", "Missing configuration has a friendly redirect");
            }

            Environment.SetEnvironmentVariable("Authentication__Facebook__AppId", "123456789012345");
            Environment.SetEnvironmentVariable("Authentication__Facebook__AppSecret", "integration-test-secret");
            using var factory = Factory();
            try { await Run(factory); }
            finally
            {
                using var scope = factory.Services.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await database.Users.Where(x => x.Email != null && x.Email.EndsWith(suffix)).ExecuteDeleteAsync();
            }
            Console.WriteLine($"PASS: {checks} Facebook OAuth and SQL Server checks (simulated Facebook responses).");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Authentication__Facebook__AppId", oldId);
            Environment.SetEnvironmentVariable("Authentication__Facebook__AppSecret", oldSecret);
        }
    }

    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<FacebookOptions>(FacebookDefaults.AuthenticationScheme, options =>
                    options.Backchannel = new HttpClient(new FakeFacebook())));
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost:7026"), AllowAutoRedirect = false, HandleCookies = true
    });

    private static async Task Run(WebApplicationFactory<Program> factory)
    {
        using var client = Client(factory);
        using (var noCsrf = await client.PostAsync("/api/auth/facebook/start", new FormUrlEncodedContent([])))
            Check(noCsrf.StatusCode == HttpStatusCode.BadRequest, "Facebook start requires CSRF");
        using (var noCookie = await client.GetAsync("/api/auth/facebook/callback"))
            Check(noCookie.Headers.Location?.OriginalString == "/?authError=facebook_failed", "Callback cannot be forged without external cookie");

        var newProfile = new Profile("subject-new", "new" + suffix, new string('N', 40), true);
        FakeFacebook.Profiles["new-code"] = newProfile;
        var location = await OAuth(client, "new-code");
        Check(location == "/", "First Facebook login returns to lobby");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var userId = me.GetProperty("account").GetProperty("id").GetString()!;
        Check(me.GetProperty("account").GetProperty("username").GetString()!.StartsWith("fb"), "Facebook account uses the fb username prefix");
        Check(me.GetProperty("account").GetProperty("displayName").GetString()!.Length == 30, "Facebook name fits the schema");
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await database.Users.SingleAsync(x => x.Id == userId);
            Check(user.PasswordHash is null && user.EmailConfirmed, "Facebook account has no invented password and has a confirmed provider email");
            Check(await database.UserLogins.AnyAsync(x => x.UserId == userId && x.LoginProvider == "Facebook" && x.ProviderKey == newProfile.Id), "Provider subject persisted in SQL");
        }
        using (var forgot = await Post(client, "/api/auth/forgot-password", new { email = newProfile.Email }))
            Check(forgot.IsSuccessStatusCode, "Forgot password keeps a generic response");
        using (var reset = await Post(client, "/api/auth/reset-password", new { code = "123456", password = "Changed!123", confirmPassword = "Changed!123" }))
            Check(reset.StatusCode == HttpStatusCode.BadRequest, "Demo OTP cannot create a password for a Facebook-only account");
        using (var logout = await Post(client, "/api/auth/logout")) Check(logout.IsSuccessStatusCode, "Facebook user can logout");
        using (var loggedOut = await client.GetAsync("/api/auth/me")) Check(loggedOut.StatusCode == HttpStatusCode.Unauthorized, "Logout clears the session");

        using var returning = Client(factory);
        Check(await OAuth(returning, "new-code") == "/", "Returning Facebook login succeeds");
        var returningMe = await returning.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Check(returningMe.GetProperty("account").GetProperty("id").GetString() == userId, "Returning login uses the same account");

        using var cancelled = Client(factory);
        var state = await Start(cancelled);
        using (var response = await cancelled.GetAsync("/signin-facebook?error=access_denied&state=" + Uri.EscapeDataString(state)))
            Check(response.Headers.Location?.OriginalString == "/?authError=facebook_cancelled", "Consent cancellation is handled");
        using (var response = await cancelled.GetAsync("/signin-facebook?code=new-code&state=tampered"))
            Check(response.Headers.Location?.OriginalString == "/?authError=facebook_failed", "Tampered state is rejected");
        FakeFacebook.Profiles["unverified-code"] = new("subject-unverified", "unverified" + suffix, "Unverified", false);
        Check(await OAuth(cancelled, "unverified-code") == "/?authError=facebook_email_missing", "New Facebook login without email is rejected");
        using var outsider = Client(factory);
        state = await Start(cancelled);
        using (var stolenState = await outsider.GetAsync("/signin-facebook?code=new-code&state=" + Uri.EscapeDataString(state)))
            Check(stolenState.Headers.Location?.OriginalString == "/?authError=facebook_failed", "State without the browser correlation cookie is rejected");

        string localId;
        var localName = "local" + Guid.NewGuid().ToString("N")[..10];
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var local = new ApplicationUser { UserName = localName, DisplayName = "Local Player", Email = "local" + suffix, EmailConfirmed = true };
            Check((await users.CreateAsync(local, "LocalPass!123")).Succeeded, "Local account fixture created");
            localId = local.Id;
        }
        FakeFacebook.Profiles["local-code"] = new("subject-local", "local" + suffix, "Facebook Name", true);
        using var linking = Client(factory);
        Check(await OAuth(linking, "local-code") == "/?authError=facebook_link_required", "Matching email requires the local account password");
        using (var anonymous = await linking.GetAsync("/api/auth/me"))
            Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Matching email alone does not log in");
        using (var wrong = await Post(linking, "/api/auth/facebook/link", new { identity = localName, password = "WrongPass!123" }))
            Check(wrong.StatusCode == HttpStatusCode.Unauthorized, "Wrong local password cannot link Facebook");
        using (var forgedLink = await Post(outsider, "/api/auth/facebook/link", new { identity = localName, password = "LocalPass!123" }))
            Check(forgedLink.StatusCode == HttpStatusCode.BadRequest, "Password alone without Facebook authentication cannot link");
        using (var linked = await Post(linking, "/api/auth/facebook/link", new { identity = localName, password = "LocalPass!123", rememberMe = true }))
            Check(linked.IsSuccessStatusCode, "Correct password links Facebook");
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(await database.UserLogins.AnyAsync(x => x.UserId == localId && x.ProviderKey == "subject-local"), "Link belongs to the existing local user");
            Check(await database.Users.CountAsync(x => x.Email == "local" + suffix) == 1, "Link does not create duplicate users");
        }
        using var linkedReturn = Client(factory);
        Check(await OAuth(linkedReturn, "local-code") == "/", "Linked account can subsequently log in with Facebook");

        // Users already signed in (including Google-only users) can explicitly attach Facebook.
        FakeFacebook.Profiles["attach-code"] = new("subject-attached", "different" + suffix, "Another Facebook Name", true);
        Check(await OAuth(linking, "attach-code") == "/?authNotice=facebook_linked", "Authenticated user can link Facebook without a local-password flow");
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(await database.UserLogins.AnyAsync(x => x.UserId == localId && x.ProviderKey == "subject-attached"), "Authenticated link targets the current user");
            Check(!await database.Users.AnyAsync(x => x.Email == "different" + suffix), "Authenticated linking does not create a second account");
        }
        Check(await OAuth(linking, "new-code") == "/?authError=facebook_link_conflict", "Cannot attach a Facebook ID owned by another user");
        var stillMe = await linking.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Check(stillMe.GetProperty("account").GetProperty("id").GetString() == localId, "Failed linking preserves the current account");
        using var attachedReturn = Client(factory);
        Check(await OAuth(attachedReturn, "attach-code") == "/", "Attached Facebook logs into the original account");

        // Revoking email later must not break an already linked provider identity.
        FakeFacebook.Profiles["attach-code"] = FakeFacebook.Profiles["attach-code"] with { Verified = false };
        using var missingEmailReturn = Client(factory);
        Check(await OAuth(missingEmailReturn, "attach-code") == "/", "Returning linked Facebook can log in without email");

        // A link initiated under one session must fail if that user logs out during OAuth.
        var boundState = await Start(linking);
        using (var logoutDuringLink = await Post(linking, "/api/auth/logout"))
            Check(logoutDuringLink.IsSuccessStatusCode, "Can logout during an unfinished link");
        using (var providerReturn = await linking.GetAsync("/signin-facebook?code=attach-code&state=" + Uri.EscapeDataString(boundState)))
        using (var appReturn = await linking.GetAsync(providerReturn.Headers.Location))
            Check(appReturn.Headers.Location?.OriginalString == "/?authError=facebook_failed", "Link is bound to its initiating login session");

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(userId))!;
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));
        }
        using var locked = Client(factory);
        Check(await OAuth(locked, "new-code") == "/?authError=facebook_locked", "Facebook sign-in respects account lockout");
    }

    private static async Task<string> Start(HttpClient client)
    {
        using var response = await Post(client, "/api/auth/facebook/start");
        Check(response.StatusCode == HttpStatusCode.Redirect, "Start challenges Facebook");
        var location = response.Headers.Location!;
        Check(location.Host == "www.facebook.com", "Challenge goes to Facebook");
        Check(location.AbsolutePath == "/v22.0/dialog/oauth", "Facebook uses the maintained API endpoints instead of the old package defaults");
        var query = QueryHelpers.ParseQuery(location.Query);
        Check(query["redirect_uri"] == "https://localhost:7026/signin-facebook", "OAuth callback URL is correct");
        Check(!string.IsNullOrEmpty(query["state"]) && !string.IsNullOrEmpty(query["code_challenge"]), "Challenge has state and PKCE");
        return query["state"].ToString();
    }

    private static async Task<string?> OAuth(HttpClient client, string code)
    {
        var state = await Start(client);
        using var providerCallback = await client.GetAsync("/signin-facebook?code=" + code + "&state=" + Uri.EscapeDataString(state));
        Check(providerCallback.StatusCode == HttpStatusCode.Redirect, "Provider response is handled");
        Check(providerCallback.Headers.Location?.OriginalString.StartsWith("/api/auth/facebook/callback") == true,
            "Middleware did not reach app callback: " + providerCallback.Headers.Location);
        using var callback = await client.GetAsync(providerCallback.Headers.Location);
        Check(callback.StatusCode == HttpStatusCode.Redirect, "App callback completes with a redirect");
        return callback.Headers.Location?.OriginalString;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        request.Content = body is null ? new FormUrlEncodedContent([]) : JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }

    internal record Profile(string Id, string Email, string Name, bool Verified);
    private sealed class FakeFacebook : HttpMessageHandler
    {
        internal static Dictionary<string, Profile> Profiles { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://graph.facebook.com/v22.0/oauth/access_token")
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                var code = form["code"].ToString();
                Check(!string.IsNullOrEmpty(form["code_verifier"]), "Token exchange includes the PKCE verifier");
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = code, token_type = "Bearer", expires_in = 3600 }) };
            }
            if (request.RequestUri.AbsoluteUri.StartsWith("https://graph.facebook.com/v22.0/me?", StringComparison.Ordinal))
            {
                var query = QueryHelpers.ParseQuery(request.RequestUri.Query); var profile = Profiles[query["access_token"].ToString()]; Check(!string.IsNullOrEmpty(query["appsecret_proof"]), "Graph request includes app secret proof"); Check(query["fields"].ToString().Split(',').Contains("email"), "Graph request asks for email");
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = profile.Id, email = profile.Verified ? profile.Email : null, name = profile.Name }) };
            }
            Console.Error.WriteLine("Unexpected backchannel URL: " + request.RequestUri);
            throw new InvalidOperationException("Unexpected backchannel URL: " + request.RequestUri);
        }
    }
}
