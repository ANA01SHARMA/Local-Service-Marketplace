using System.Security.Claims;
using LocalServicesMarketplace.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Infrastructure;

/// <summary>Adds display claims (full name, avatar) to the auth cookie so layouts need no DB lookup.</summary>
public class AppClaimsPrincipalFactory(UserManager<User> userManager, RoleManager<Role> roleManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User, Role>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("full_name", user.FullName));
        if (!string.IsNullOrEmpty(user.ProfileImagePath))
            identity.AddClaim(new Claim("avatar", user.ProfileImagePath));
        if (user.MustChangePassword)
            identity.AddClaim(new Claim("must_change_password", "true"));
        return identity;
    }
}
