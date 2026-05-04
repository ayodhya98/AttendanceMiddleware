using AttendanceMiddleware_without_db.Data;
using AttendanceMiddleware_without_db.DTOs;
using AttendanceMiddleware_without_db.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;

namespace AttendanceMiddleware_without_db.Services
{
    public class AttendanceRoutingService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<AttendanceRoutingService> _logger;
        private readonly IConfiguration _config;

        public AttendanceRoutingService(
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<AttendanceRoutingService> logger,
            IConfiguration config)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _config = config;
        }

        public async Task<(int sent, int failed, int unknown)> RouteAttendanceAsync(
            List<ZKTAttendanceData> records)
        {
            int sent = 0, failed = 0, unknown = 0;

            await using var db = await _dbFactory.CreateDbContextAsync();

            // ── STEP 1: Extract all unique EmpIds from incoming records ──────
            // We do this upfront so we can batch-query the DB in one round trip
            // instead of querying per record (N+1 problem prevention)
            var empIds = records
                .Where(r => !string.IsNullOrWhiteSpace(r.EmpId))
                .Select(r => r.EmpId)
                .Distinct()
                .ToList();

            // ── STEP 2: Single DB query — get all matching registered employees
            // RegisteredEmployees was populated when HRM synced employees via
            // the employee.registered RabbitMQ queue
            var employees = await db.RegisteredEmployees
                .Where(e => empIds.Contains(e.EmpNo))
                .ToListAsync();

            // ── STEP 3: Group attendance records by CompanyName ──────────────
            // Multiple employees from different companies could arrive in one
            // batch from a device. We sort them by company so we publish each
            // group to the correct company queue in HRM RabbitMQ.
            // Key = CompanyName, Value = list of attendance records for that company
            var grouped = new Dictionary<string, (string companyName, List<ZKTAttendanceData> records)>();

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.EmpId))
                {
                    unknown++;
                    continue;
                }

                // Look up employee by EmpNo (EmpId from device = EmpNo in HRM)
                var employee = employees.FirstOrDefault(e => e.EmpNo == record.EmpId);

                if (employee == null || string.IsNullOrWhiteSpace(employee.CompanyName))
                {
                    // Employee not in our system — could be a device misconfiguration
                    // or employee was never synced from HRM
                    _logger.LogWarning(
                        "EmpId={EmpId} not found in RegisteredEmployees. Skipping.",
                        record.EmpId);

                    // Save failure to audit log — so admin can investigate
                    db.AttendanceLogs.Add(new AttendanceLog
                    {
                        EmpId = record.EmpId,
                        AttTime = record.AttTime,
                        CheckingStatus = record.CheckingStatus,
                        VerifyType = record.VerifyType,
                        DeviceId = record.DeviceID,
                        Status = "Failed",
                        FailureReason = "Employee not found in RegisteredEmployees",
                        ReceivedAt = DateTime.UtcNow
                    });

                    unknown++;
                    continue;
                }

                var companyName = employee.CompanyName;

                if (!grouped.ContainsKey(companyName))
                    grouped[companyName] = (companyName, new List<ZKTAttendanceData>());

                grouped[companyName].records.Add(record);
            }

            // Save unknown/failed records to audit log
            await db.SaveChangesAsync();

            // ── STEP 4: Publish each company's attendance to HRM RabbitMQ ───
            // Each company in HRM has its own queue: attendance.{CompanyName}
            // HRM's RabbitMqService.ConsumeMiddlewareQueueAsync listens on this queue
            // and calls ProcessAttendanceAsync to save attendance to HRM DB
            foreach (var (companyName, (_, groupRecords)) in grouped)
            {
                var logs = new List<AttendanceLog>();

                try
                {
                    // Connect to HRM RabbitMQ — separate from middleware's own RabbitMQ
                    // HrmRabbitMQ config points to the rabbitmq container in HRM network
                    var factory = new ConnectionFactory
                    {
                        HostName = _config["HrmRabbitMQ:Host"] ?? "rabbitmq",
                        Port = int.Parse(_config["HrmRabbitMQ:Port"] ?? "5672"),
                        UserName = _config["HrmRabbitMQ:Username"] ?? "guest",
                        Password = _config["HrmRabbitMQ:Password"] ?? "guest",
                        VirtualHost = _config["HrmRabbitMQ:VirtualHost"] ?? "/"
                    };

                    using var connection = factory.CreateConnection();
                    using var channel = connection.CreateModel();

                    // Declare the same exchange HRM RabbitMqService declared
                    // Must match exactly — same name, type, durable flag
                    channel.ExchangeDeclare(
                        exchange: "attendance",
                        type: ExchangeType.Direct,
                        durable: true,
                        autoDelete: false);

                    // Queue name pattern must match HRM ConsumeMiddlewareQueueAsync
                    // HRM reads company name from DB and listens on attendance.{CompanyName}
                    var queueName = $"attendance.{companyName}";

                    channel.QueueDeclare(
                        queue: queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false);

                    channel.QueueBind(
                        queue: queueName,
                        exchange: "attendance",
                        routingKey: companyName);

                    // Publish each attendance record individually
                    // HRM processes them one at a time via BasicQos prefetchCount=1
                    foreach (var record in groupRecords)
                    {
                        // Shape must match AttendanceMessage class in HRM RabbitMqService
                        // HRM deserializes this as AttendanceMessage then maps to ZKTAttendanceData
                        var message = new
                        {
                            EmpId = record.EmpId,
                            AttTime = record.AttTime,
                            CheckingStatus = record.CheckingStatus,
                            VerifyType = record.VerifyType,
                            DeviceId = record.DeviceID,
                            CompanyName = companyName,
                            PublishedAt = DateTime.UtcNow
                        };

                        var json = JsonConvert.SerializeObject(message);
                        var body = Encoding.UTF8.GetBytes(json);

                        var props = channel.CreateBasicProperties();
                        props.Persistent = true;       // survives RabbitMQ restart
                        props.ContentType = "application/json";
                        props.MessageId = Guid.NewGuid().ToString();

                        channel.BasicPublish(
                            exchange: "attendance",
                            routingKey: companyName,
                            basicProperties: props,
                            body: body);

                        sent++;

                        logs.Add(new AttendanceLog
                        {
                            EmpId = record.EmpId,
                            AttTime = record.AttTime,
                            CheckingStatus = record.CheckingStatus,
                            VerifyType = record.VerifyType,
                            DeviceId = record.DeviceID,
                            CompanyName = companyName,
                            Status = "Sent",
                            ReceivedAt = DateTime.UtcNow,
                            SentAt = DateTime.UtcNow
                        });

                        _logger.LogInformation(
                            "Published EmpId={EmpId} → queue: {Queue}",
                            record.EmpId, queueName);
                    }
                }
                catch (Exception ex)
                {
                    failed += groupRecords.Count;
                    _logger.LogError(ex,
                        "Failed to publish attendance for company {Company}", companyName);

                    foreach (var r in groupRecords)
                    {
                        logs.Add(new AttendanceLog
                        {
                            EmpId = r.EmpId,
                            AttTime = r.AttTime,
                            CheckingStatus = r.CheckingStatus,
                            VerifyType = r.VerifyType,
                            DeviceId = r.DeviceID,
                            CompanyName = companyName,
                            Status = "Failed",
                            FailureReason = ex.Message,
                            ReceivedAt = DateTime.UtcNow
                        });
                    }
                }

                // Save audit logs for this company batch
                await using var db2 = await _dbFactory.CreateDbContextAsync();
                db2.AttendanceLogs.AddRange(logs);
                await db2.SaveChangesAsync();
            }

            return (sent, failed, unknown);
        }
    }
}