using AttendanceMiddleware_without_db.Entities;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace AttendanceMiddleware_without_db.Services
{
    public class EmployeeRegisteredConsumerService : BackgroundService
    {
        private readonly ILogger<EmployeeRegisteredConsumerService> _logger;
        private readonly IConfiguration _config;
        private readonly SqlEmployeeService _employeeService;
        private IConnection _connection;
        private IModel _channel;

        public EmployeeRegisteredConsumerService(
            ILogger<EmployeeRegisteredConsumerService> logger,
            IConfiguration config,
            SqlEmployeeService employeeService)
        {
            _logger = logger;
            _config = config;
            _employeeService = employeeService;
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
                string empNo = "unknown";
                string companyCode = "unknown";

                try
                {
                    var message = JsonConvert.DeserializeObject<EmployeeRegisteredMessage>(json);

                    if (message == null)
                    {
                        _logger.LogWarning("Null employee message received. Skipping.");
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                        return;
                    }

                    empNo = message.EmpNo;
                    companyCode = message.CompanyCode;
                    message.ReceivedAt = DateTime.UtcNow;

                    // Save to SQL Server with Status = Success
                    await _employeeService.UpsertEmployeeAsync(message);

                    _logger.LogInformation(
                        "Employee saved: EmpNo={EmpNo} Company={CompanyName} Status=Success",
                        message.EmpNo, message.CompanyName);

                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to process employee EmpNo={EmpNo}. Saving as Failed.",
                        empNo);

                    // Save failure record to DB — don't lose the info
                    try
                    {
                        await _employeeService.MarkFailedAsync(
                            empNo, companyCode, ex.Message);
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "Could not save failure record to DB.");
                    }

                    // Ack anyway — we've recorded the failure, no point requeueing forever
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
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