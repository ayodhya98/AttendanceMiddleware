using AttendanceMiddleware_without_db.Data;
using AttendanceMiddleware_without_db.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQL Server
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<RabbitMqPublisherService>();
builder.Services.AddSingleton<SqlEmployeeService>();
builder.Services.AddHostedService<EmployeeRegisteredConsumerService>();

// Attendance routing — HTTP client with SSL bypass for dev
builder.Services.AddHttpClient<AttendanceRoutingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

var publisher = app.Services.GetRequiredService<RabbitMqPublisherService>();
publisher.Connect();

app.Lifetime.ApplicationStopping.Register(() => publisher.Disconnect());

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();