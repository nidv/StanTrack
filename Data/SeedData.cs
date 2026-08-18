using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StanTrack.Models;

namespace StanTrack.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider services)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var config = services.GetRequiredService<IConfiguration>();

            string[] roles = ["Fan", "Admin"];
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            var adminEmail = config["SeedAdmin:Email"] ?? "admin@stantrack.local";
            var adminPassword = config["SeedAdmin:Password"];
            if (string.IsNullOrEmpty(adminPassword))
            {
                return;
            }

            var existing = await userManager.FindByEmailAsync(adminEmail);
            if (existing is null)
            {
                var admin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    DisplayName = "Admin"
                };
                var result = await userManager.CreateAsync(admin, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                }
            }
            else if (!await userManager.IsInRoleAsync(existing, "Admin"))
            {
                await userManager.AddToRoleAsync(existing, "Admin");
            }
        }
    }
}
