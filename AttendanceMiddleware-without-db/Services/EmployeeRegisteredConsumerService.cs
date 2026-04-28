using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace AttendanceMiddleware_without_db.Services
{

    public static class RegisteredEmployeeStore
    {
        public static readonly List<EmployeeRegisteredMessage> Employees = new();
    }

    public class EmployeeRegisteredMessage
    {
        public string ApplicationUserId { get; set; } = string.Empty;
        public string EmpNo { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyCode { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    public class EmployeeRegisteredConsumerService : BackgroundService
    {
        private readonly ILogger<EmployeeRegisteredConsumerService> _logger;
        private readonly IConfiguration _config;
        private IConnection _connection;
        private IModel _channel;

        public EmployeeRegisteredConsumerService(
            ILogger<EmployeeRegisteredConsumerService> logger,
            IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Connect();
                    await ConsumeAsync(stoppingToken);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Employee consumer failed. Retrying in 10s...");
                    await Task.Delay(10000, stoppingToken);
                }
            }
        }

        private void Connect()
        {
            // Connects to HRM RabbitMQ to consume employee.registered queue
            var factory = new ConnectionFactory
            {
                HostName = _config["HrmRabbitMQ:Host"] ?? "rabbitmq",
                Port = int.Parse(_config["HrmRabbitMQ:Port"] ?? "5672"),
                UserName = _config["HrmRabbitMQ:Username"] ?? "guest",
                Password = _config["HrmRabbitMQ:Password"] ?? "guest",
                VirtualHost = _config["HrmRabbitMQ:VirtualHost"] ?? "/",
                DispatchConsumersAsync = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.QueueDeclare(
                queue: "employee.registered",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            _logger.LogInformation(
                "Employee consumer connected to HRM RabbitMQ at {Host}",
                _config["HrmRabbitMQ:Host"]);
        }

        private async Task ConsumeAsync(CancellationToken stoppingToken)
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.Received += async (_, ea) =>
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                try
                {
                    var message = JsonConvert.DeserializeObject<EmployeeRegisteredMessage>(json);

                    if (message == null)
                    {
                        _logger.LogWarning("Null employee message received. Skipping.");
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                        return;
                    }

                    message.ReceivedAt = DateTime.UtcNow;

                    // Save to in-memory store — replace with MongoDB later
                    RegisteredEmployeeStore.Employees.Add(message);

                    _logger.LogInformation(
                        "New employee received: EmpNo={EmpNo} Company={CompanyName} Code={CompanyCode}",
                        message.EmpNo, message.CompanyName, message.CompanyCode);

                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing employee message. Requeuing.");
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                }

                await Task.CompletedTask;
            };

            _channel.BasicConsume(
                queue: "employee.registered",
                autoAck: false,
                consumer: consumer);

            _logger.LogInformation("Listening on queue: employee.registered");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            try { _channel?.Close(); } catch { }
            try { _connection?.Close(); } catch { }
        }
    }
}