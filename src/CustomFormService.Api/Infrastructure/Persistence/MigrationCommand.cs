using Microsoft.EntityFrameworkCore;

namespace CustomFormService.Api.Infrastructure.Persistence;

public static class MigrationCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await database.Database.MigrateAsync();
            Console.WriteLine("Database migrations completed.");
            return 0;
        }
        catch (Exception)
        {
            // Connection errors can contain credentials or infrastructure details.
            Console.Error.WriteLine("Database migration failed. Check database configuration and connectivity.");
            return 1;
        }
    }
}
