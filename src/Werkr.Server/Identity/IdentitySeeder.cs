using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Werkr.Common.Auth;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Roles;

namespace Werkr.Server.Identity;

/// <summary>
/// Seeds default roles, role-permission mappings, and an initial admin account on application startup.
/// </summary>
public static class IdentitySeeder {
    /// <summary>
    /// Default permission sets for each role.
    /// Admin gets all permissions. Operator gets Read + Execute. Viewer gets Read only.
    /// </summary>
    private static readonly Dictionary<DefaultRoles, Permission[]> s_defaultPermissions = new( ) {
        [DefaultRoles.Admin] = [
            Permission.Create,
            Permission.Read,
            Permission.Update,
            Permission.Delete,
            Permission.Execute,
            Permission.Admin,
        ],
        [DefaultRoles.Operator] = [
            Permission.Read,
            Permission.Execute,
        ],
        [DefaultRoles.Viewer] = [
            Permission.Read,
        ],
    };

    /// <summary>
    /// Seeds default roles, role-permission mappings, and an initial admin account if none exists.
    /// Called during application startup.
    /// </summary>
    /// <param name="services">The application's root <see cref="IServiceProvider"/>.</param>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>( );
        UserManager<WerkrUser> userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<WerkrUser>>( );
        WerkrIdentityDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<WerkrIdentityDbContext>( );

        // Seed roles
        foreach (string role in Enum.GetNames<DefaultRoles>( )) {
            if (!await roleManager.RoleExistsAsync( role )) {
                _ = await roleManager.CreateAsync( new IdentityRole( role ) );
            }
        }

        // Seed role-permission mappings
        await SeedPermissionsAsync( roleManager, dbContext );

        // Seed default admin if no admin exists
        IList<WerkrUser> admins = await userManager.GetUsersInRoleAsync(
            DefaultRoles.Admin.ToString()
        );

        if (admins.Count == 0) {
            string generatedPassword = GenerateDefaultAdminPassword( );

            WerkrUser admin = new( ) {
                UserName = "admin@werkr.local",
                Email = "admin@werkr.local",
                Name = "Default Admin",
                Enabled = true,
                ChangePassword = true,
                Requires2FA = true,
                EmailConfirmed = true
            };

            IdentityResult result = await userManager.CreateAsync( admin, generatedPassword );
            if (result.Succeeded) {
                _ = await userManager.AddToRoleAsync( admin, DefaultRoles.Admin.ToString( ) );

                ILogger logger = services.GetRequiredService<ILoggerFactory>( )
                    .CreateLogger( "Werkr.Identity.Seeder" );
                logger.LogWarning( "Default admin account created: admin@werkr.local — change the password on first login." );

                // Write sensitive credentials only to stdout (not to Serilog sinks)
                Console.WriteLine( );
                Console.WriteLine( "╔══════════════════════════════════════════════════════╗" );
                Console.WriteLine( "║  DEFAULT ADMIN ACCOUNT CREATED                      ║" );
                Console.WriteLine( "║  Email:    admin@werkr.local                        ║" );
                Console.WriteLine( $"║  Password: {generatedPassword,-41}║" );
                Console.WriteLine( "║  ⚠ Change this password immediately on first login  ║" );
                Console.WriteLine( "╚══════════════════════════════════════════════════════╝" );
                Console.WriteLine( );

                // Optionally write to a file specified by Werkr:WriteAdminPasswordToFile
                IConfiguration configuration = services.GetRequiredService<IConfiguration>( );
                string? passwordFilePath = configuration["Werkr:WriteAdminPasswordToFile"];
                if (!string.IsNullOrWhiteSpace( passwordFilePath )) {
                    try {
                        await File.WriteAllTextAsync( passwordFilePath, generatedPassword );
                        logger.LogInformation( "Admin password written to file." );
                    } catch (Exception ex) {
                        logger.LogError( ex, "Failed to write admin password to {FilePath}", passwordFilePath );
                    }
                }
            } else {
                ILogger logger = services.GetRequiredService<ILoggerFactory>( )
                    .CreateLogger( "Werkr.Identity.Seeder" );
                foreach (IdentityError error in result.Errors) {
                    logger.LogError( "Failed to create default admin: {Code} — {Description}",
                        error.Code, error.Description
                    );
                }
            }
        }

    }

    /// <summary>
    /// Ensures that every <see cref="Permission"/> defined in <see cref="s_defaultPermissions"/> for each <see cref="DefaultRoles"/> entry has a corresponding <see cref="RolePermission"/> row in the database. Missing mappings are inserted; existing ones are left untouched.
    /// </summary>
    private static async Task SeedPermissionsAsync(
        RoleManager<IdentityRole> roleManager,
        WerkrIdentityDbContext dbContext
    ) {
        foreach ((DefaultRoles defaultRole, Permission[] permissions) in s_defaultPermissions) {
            IdentityRole? role = await roleManager.FindByNameAsync( defaultRole.ToString( ) );
            if (role is null) {
                continue;
            }

            foreach (Permission permission in permissions) {
                bool exists = await dbContext.RolePermissions
                    .AnyAsync( rp => rp.RoleId == role.Id && rp.Permission == permission );

                if (!exists) {
                    _ = dbContext.RolePermissions.Add( new RolePermission {
                        RoleId = role.Id,
                        Permission = permission,
                    } );
                }
            }
        }

        _ = await dbContext.SaveChangesAsync( );
    }

    /// <summary>
    /// Generates a cryptographically random 24-character password that satisfies typical ASP.NET Core Identity password-complexity rules. The password is guaranteed to contain at least one uppercase letter, one lowercase letter, one digit, and one symbol before the remaining characters are filled from the full character set and Fisher-Yates shuffled.
    /// </summary>
    private static string GenerateDefaultAdminPassword( ) {
        const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string Lower = "abcdefghijklmnopqrstuvwxyz";
        const string Digits = "0123456789";
        const string Symbols = "!@#$%^&*()-_=+[]{};:,.?";
        const string All = Upper + Lower + Digits + Symbols;

        Span<char> passwordChars = stackalloc char[24];
        passwordChars[0] = Upper[RandomNumberGenerator.GetInt32( Upper.Length )];
        passwordChars[1] = Lower[RandomNumberGenerator.GetInt32( Lower.Length )];
        passwordChars[2] = Digits[RandomNumberGenerator.GetInt32( Digits.Length )];
        passwordChars[3] = Symbols[RandomNumberGenerator.GetInt32( Symbols.Length )];

        for (int i = 4; i < passwordChars.Length; i++) {
            passwordChars[i] = All[RandomNumberGenerator.GetInt32( All.Length )];
        }

        for (int i = passwordChars.Length - 1; i > 0; i--) {
            int j = RandomNumberGenerator.GetInt32( i + 1 );
            (passwordChars[i], passwordChars[j]) = (passwordChars[j], passwordChars[i]);
        }

        return new string( passwordChars );
    }
}
