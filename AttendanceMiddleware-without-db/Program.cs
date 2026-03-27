using AttendanceMiddleware_without_db.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddSingleton<RabbitMqPublisherService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Connect to RabbitMQ on startup and declare all queues
var publisher = app.Services.GetRequiredService<RabbitMqPublisherService>();
publisher.Connect();

// Disconnect cleanly when app shuts down
app.Lifetime.ApplicationStopping.Register(() => publisher.Disconnect());


app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();