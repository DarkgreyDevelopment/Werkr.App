using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Auth;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Roles;
using Werkr.Data.Identity.Services;

namespace Werkr.Server.Identity;

/// <summary>
/// Seeds default roles, role-permission mappings, an initial admin account, and optionally a test
/// operator account on application startup.
/// </summary>
public static partial class IdentitySeeder {
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

                // Record the initial password hash in history for reuse prevention
                PasswordHistoryService historyService = scope.ServiceProvider
                    .GetRequiredService<PasswordHistoryService>( );
                await historyService.RecordAsync( admin.Id, admin.PasswordHash! );

                ILogger logger = services.GetRequiredService<ILoggerFactory>( )
                    .CreateLogger( "Werkr.Identity.Seeder" );
                LogAdminCreated( logger );

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
                        LogPasswordWritten( logger );
                    } catch (Exception ex) {
                        LogPasswordWriteFailed( logger, ex, passwordFilePath );
                    }
                }
            } else {
                ILogger logger = services.GetRequiredService<ILoggerFactory>( )
                    .CreateLogger( "Werkr.Identity.Seeder" );
                foreach (IdentityError error in result.Errors) {
                    LogAdminCreateFailed( logger, error.Code, error.Description );
                }
            }
        }

        // Optionally seed a test operator account for automated browser testing.
        // Gated by Werkr:SeedTestOperator = true. Never enable in production.
        IConfiguration config = services.GetRequiredService<IConfiguration>();
        if (string.Equals( config["Werkr:SeedTestOperator"], "true", StringComparison.OrdinalIgnoreCase )) {
            await SeedTestOperatorAsync( services, userManager, config );
        }
    }

    /// <summary>
    /// Seeds a test operator account with a known password and no forced password-change or 2FA gates.
    /// Intended exclusively for automated browser testing (e.g., BrowserTester agent) in non-production
    /// environments. The password is read from the <c>Werkr:TestOperatorPassword</c> configuration key.
    /// </summary>
    private static async Task SeedTestOperatorAsync(
        IServiceProvider services,
        UserManager<WerkrUser> userManager,
        IConfiguration configuration
    ) {
        const string Email = "operator@werkr.local";

        WerkrUser? existing = await userManager.FindByEmailAsync(Email);
        if (existing is not null) {
            return;
        }

        string? password = configuration["Werkr:TestOperatorPassword"];
        if (string.IsNullOrWhiteSpace( password )) {
            ILogger logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Werkr.Identity.Seeder");
            LogTestOperatorPasswordMissing( logger );
            return;
        }

        WerkrUser operatorUser = new()
        {
            UserName = Email,
            Email = Email,
            Name = "Test Operator",
            Enabled = true,
            ChangePassword = false,
            Requires2FA = false,
            EmailConfirmed = true,
        };

        IdentityResult result = await userManager.CreateAsync(operatorUser, password);
        if (result.Succeeded) {
            _ = await userManager.AddToRoleAsync( operatorUser, DefaultRoles.Operator.ToString( ) );

            // Record the initial password hash in history for reuse prevention
            using IServiceScope historyScope = services.CreateScope( );
            PasswordHistoryService historyService = historyScope.ServiceProvider
                .GetRequiredService<PasswordHistoryService>( );
            await historyService.RecordAsync( operatorUser.Id, operatorUser.PasswordHash! );

            ILogger logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Werkr.Identity.Seeder");
            LogTestOperatorCreated( logger );

            Console.WriteLine( );
            Console.WriteLine( "╔══════════════════════════════════════════════════════╗" );
            Console.WriteLine( "║  TEST OPERATOR ACCOUNT CREATED                      ║" );
            Console.WriteLine( "║  Email:    operator@werkr.local                     ║" );
            Console.WriteLine( "║  ⚠ This account is for automated testing only       ║" );
            Console.WriteLine( "╚══════════════════════════════════════════════════════╝" );
            Console.WriteLine( );

            string? passwordFilePath = configuration["Werkr:WriteTestOperatorPasswordToFile"];
            if (!string.IsNullOrWhiteSpace( passwordFilePath )) {
                try {
                    await File.WriteAllTextAsync( passwordFilePath, password );
                    LogPasswordWritten( logger );
                } catch (Exception ex) {
                    LogPasswordWriteFailed( logger, ex, passwordFilePath );
                }
            }
        } else {
            ILogger logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Werkr.Identity.Seeder");
            foreach (IdentityError error in result.Errors) {
                LogTestOperatorCreateFailed( logger, error.Code, error.Description );
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

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Default admin account created: admin@werkr.local — change the password on first login." )]
    private static partial void LogAdminCreated( ILogger logger );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Admin password written to file." )]
    private static partial void LogPasswordWritten( ILogger logger );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Failed to write admin password to {FilePath}" )]
    private static partial void LogPasswordWriteFailed( ILogger logger, Exception ex, string filePath );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Failed to create default admin: {Code} — {Description}" )]
    private static partial void LogAdminCreateFailed( ILogger logger, string code, string description );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Test operator account created: operator@werkr.local — for automated testing only." )]
    private static partial void LogTestOperatorCreated( ILogger logger );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Werkr:SeedTestOperator is true but Werkr:TestOperatorPassword is not set. Skipping test operator seed." )]
    private static partial void LogTestOperatorPasswordMissing( ILogger logger );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Failed to create test operator: {Code} — {Description}" )]
    private static partial void LogTestOperatorCreateFailed( ILogger logger, string code, string description );
}
