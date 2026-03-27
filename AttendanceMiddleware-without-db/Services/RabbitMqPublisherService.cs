using AttendanceMiddleware_without_db.DTOs;
using AttendanceMiddleware_without_db.Settings;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;

namespace AttendanceMiddleware_without_db.Services
{
    public class RabbitMqPublisherService
    {
        private readonly ILogger<RabbitMqPublisherService> _logger;
        private readonly IConfiguration _config;

        private IConnection _connection;
        private IModel _channel;
        private const string ExchangeName = "attendance";

        public RabbitMqPublisherService(
            ILogger<RabbitMqPublisherService> logger,
            IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        // Called once on app startup from Program.cs
        // Connects and pre-declares all queues defined in CompanyDeviceMappings
        public void Connect()
        {
            var factory = new ConnectionFactory
            {
                HostName = _config["RabbitMQ:Host"] ?? "localhost",
                Port = int.Parse(_config["RabbitMQ:Port"] ?? "5672"),
                UserName = _config["RabbitMQ:Username"] ?? "guest",
                Password = _config["RabbitMQ:Password"] ?? "guest",
                VirtualHost = _config["RabbitMQ:VirtualHost"] ?? "/"
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Direct exchange — routes message to exact queue by company name
            // Durable — survives RabbitMQ restart
            _channel.ExchangeDeclare(
                exchange: ExchangeName,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false);

            // Pre-declare a queue for every registered company
            foreach (var mapping in CompanyDeviceMappings.All)
            {
                var queueName = $"attendance.{mapping.CompanyName}";

                _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false);

                _channel.QueueBind(
                    queue: queueName,
                    exchange: ExchangeName,
                    routingKey: mapping.CompanyName);

                _logger.LogInformation(
                    "Queue ready: {Queue} → DeviceID: {DeviceId}",
                    queueName, mapping.DeviceId);
            }

            _logger.LogInformation("RabbitMQ publisher connected successfully.");
        }

        // Called by controller
        // Loops through records, finds company by DeviceID, publishes to queue
        public ApiResponse RouteAndPublish(List<ZKTAttendanceData> records)
        {
            if (records == null || records.Count == 0)
                return new ApiResponse { Success = false, Message = "No records provided." };

            int published = 0, skipped = 0;

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.EmpId) ||
                    string.IsNullOrWhiteSpace(record.DeviceID))
                {
                    _logger.LogWarning("Record missing EmpId or DeviceID — skipping.");
                    skipped++;
                    continue;
                }

                // Find company by DeviceID using helper
                var mapping = CompanyDeviceMappings.GetByDeviceId(record.DeviceID);

                if (mapping == null)
                {
                    _logger.LogWarning(
                        "Unknown DeviceID={DeviceId} EmpId={EmpId} — not in mappings. Skipping.",
                        record.DeviceID, record.EmpId);
                    skipped++;
                    continue;
                }

                try
                {
                    Publish(new AttendanceMessage
                    {
                        EmpId = record.EmpId,
                        AttTime = record.AttTime,
                        CheckingStatus = record.CheckingStatus,
                        VerifyType = record.VerifyType,
                        DeviceId = record.DeviceID,
                        CompanyName = mapping.CompanyName,
                        PublishedAt = DateTime.UtcNow
                    }, mapping.CompanyName);

                    published++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to publish EmpId={EmpId} DeviceID={DeviceId}",
                        record.EmpId, record.DeviceID);
                    skipped++;
                }
            }

            var message = skipped == 0
                ? $"All {published} records published successfully."
                : $"{published} published, {skipped} skipped.";

            _logger.LogInformation(message);

            return new ApiResponse { Success = true, Message = message };
        }

        // Publishes one message to the correct queue
        private void Publish(AttendanceMessage message, string companyName)
        {
            if (_channel == null)
                throw new InvalidOperationException(
                    "RabbitMQ channel is not open. Call Connect() first.");

            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            var props = _channel.CreateBasicProperties();

            props.Persistent = true;             
            props.ContentType = "application/json";
            props.MessageId = Guid.NewGuid().ToString();

            _channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: companyName,
                basicProperties: props,
                body: body);

            _logger.LogInformation(
                "Published EmpId={EmpId} → attendance.{Company}",
                message.EmpId, companyName);
        }

        public void Disconnect()
        {
            try { _channel?.Close(); } catch { }
            try { _connection?.Close(); } catch { }
        }
    }
}