using AttendanceMiddleware_without_db.Data;
using AttendanceMiddleware_without_db.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQL Server — middleware's own DB for audit logs and registered employees
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<RabbitMqPublisherService>();
builder.Services.AddSingleton<SqlEmployeeService>();
builder.Services.AddHostedService<EmployeeRegisteredConsumerService>();

// Attendance routing — now uses RabbitMQ instead of HTTP
// Scoped because it creates a new RabbitMQ connection per request
builder.Services.AddScoped<AttendanceRoutingService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Run EF Core migrations on startup — creates/updates DB schema automatically
await app.ApplyMigrationsAsync();

// Connect to middleware's own RabbitMQ on startup
var publisher = app.Services.GetRequiredService<RabbitMqPublisherService>();
publisher.Connect();

// Clean disconnect when app shuts down
app.Lifetime.ApplicationStopping.Register(() => publisher.Disconnect());

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();