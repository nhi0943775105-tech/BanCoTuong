using System.Security.Claims;
using _23DTHD5_Demo.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace _23DTHD5_Demo.Services;

public class ApplicationClaimsFactory(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("display_name", user.DisplayName));
        return identity;
    }
}
