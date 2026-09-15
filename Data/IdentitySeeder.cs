using FixPal.Infrastructure.Identity;
using FixPal.Models;
using Microsoft.AspNetCore.Identity;

namespace FixPal.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedRolesAndAdminAsync(
            IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var roleManager = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole>>();

            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var configuration = scope.ServiceProvider
                .GetRequiredService<IConfiguration>();

            foreach (var roleName in AppRoles.All)
            {
                if (await roleManager.RoleExistsAsync(roleName))
                {
                    continue;
                }

                var createRoleResult = await roleManager.CreateAsync(
                    new IdentityRole(roleName));

                if (!createRoleResult.Succeeded)
                {
                    var errors = string.Join(", ",
                        createRoleResult.Errors.Select(e => e.Description));

                    throw new InvalidOperationException(
                        $"Failed to create role '{roleName}'. {errors}");
                }
            }

            // Opt in with an immutable existing user ID selected by a trusted operator.
            if (!configuration.GetValue<bool>("BootstrapAdmin:Enabled"))
            {
                return;
            }

            var adminUserId = configuration["BootstrapAdmin:UserId"];
            if (string.IsNullOrWhiteSpace(adminUserId))
                throw new InvalidOperationException("Explicit BootstrapAdmin:UserId is required when provisioning is enabled.");
            var adminUser = await userManager.FindByIdAsync(adminUserId);
            if (adminUser == null)
            {
                throw new InvalidOperationException("The explicitly configured administrator account does not exist.");
            }

            if (await userManager.IsInRoleAsync(adminUser, AppRoles.Admin))
            {
                return;
            }

            var addRoleResult = await userManager.AddToRoleAsync(
                adminUser,
                AppRoles.Admin);

            if (!addRoleResult.Succeeded)
            {
                var errors = string.Join(", ",
                    addRoleResult.Errors.Select(e => e.Description));

                throw new InvalidOperationException(
                    $"Failed to assign the admin role. {errors}");
            }
        }
    }
}
