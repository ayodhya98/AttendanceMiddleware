using Microsoft.EntityFrameworkCore;

namespace AttendanceMiddleware_without_db.Data
{
    public static class DatabaseMigrationExtensions
    {
        public static async Task ApplyMigrationsAsync(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

            const int maxRetries = 10;
            var delay = TimeSpan.FromSeconds(5);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    logger.LogInformation("Migration attempt {Attempt}/{Max}...", attempt, maxRetries);
                    await using var db = await factory.CreateDbContextAsync();
                    await db.Database.MigrateAsync();
                    logger.LogInformation("✅ Database ready.");
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        logger.LogError(ex, "❌ Migration failed after {Max} attempts. Aborting.", maxRetries);
                        throw;
                    }
                    logger.LogWarning("⚠️ Attempt {Attempt} failed: {Message}. Retrying in {Delay}s...",
                        attempt, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
        }
    }
}