using AttendanceMiddleware_without_db.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RabbitMqPublisherService>();
builder.Services.AddHostedService<EmployeeRegisteredConsumerService>(); 
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var publisher = app.Services.GetRequiredService<RabbitMqPublisherService>();
publisher.Connect();

app.Lifetime.ApplicationStopping.Register(() => publisher.Disconnect());

app.UseSwagger();
app.UseSwaggerUI();
// app.UseHttpsRedirection();
app.MapControllers();
app.Run();