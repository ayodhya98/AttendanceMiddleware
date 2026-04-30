using AttendanceMiddleware_without_db.Data;
using AttendanceMiddleware_without_db.DTOs;
using AttendanceMiddleware_without_db.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace AttendanceMiddleware_without_db.Services
{
    public class AttendanceRoutingService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<AttendanceRoutingService> _logger;
        private readonly HttpClient _httpClient;

        public AttendanceRoutingService(
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<AttendanceRoutingService> logger,
            HttpClient httpClient)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<(int sent, int failed, int unknown)> RouteAttendanceAsync(
            List<ZKTAttendanceData> records)
        {
            int sent = 0, failed = 0, unknown = 0;

            await using var db = await _dbFactory.CreateDbContextAsync();

            // Group records by EmpId — look up each in RegisteredEmployees
            var empIds = records
                .Where(r => !string.IsNullOrWhiteSpace(r.EmpId))
                .Select(r => r.EmpId)
                .Distinct()
                .ToList();

            // Get all matching employees in one query
            var employees = await db.RegisteredEmployees
                .Where(e => empIds.Contains(e.EmpNo))
                .ToListAsync();

            // Group attendance by HrmBaseUrl
            var grouped = new Dictionary<string, (string companyCode, string companyName, List<ZKTAttendanceData> records)>();

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.EmpId))
                {
                    unknown++;
                    continue;
                }

                var employee = employees.FirstOrDefault(e => e.EmpNo == record.EmpId);

                if (employee == null || string.IsNullOrWhiteSpace(employee.HrmBaseUrl))
                {
                    _logger.LogWarning(
                        "Employee EmpId={EmpId} not found in RegisteredEmployees or missing HrmBaseUrl. Skipping.",
                        record.EmpId);

                    // Save as unknown to DB for audit
                    db.AttendanceLogs.Add(new AttendanceLog
                    {
                        EmpId = record.EmpId,
                        AttTime = record.AttTime,
                        CheckingStatus = record.CheckingStatus,
                        VerifyType = record.VerifyType,
                        DeviceId = record.DeviceID,
                        Status = "Failed",
                        FailureReason = "Employee not found in system",
                        ReceivedAt = DateTime.UtcNow
                    });

                    unknown++;
                    continue;
                }

                var baseUrl = employee.HrmBaseUrl.TrimEnd('/');

                if (!grouped.ContainsKey(baseUrl))
                    grouped[baseUrl] = (employee.CompanyCode, employee.CompanyName, new List<ZKTAttendanceData>());

                grouped[baseUrl].records.Add(record);
            }

            await db.SaveChangesAsync();

            // Send each group to the correct HRM
            foreach (var (hrmUrl, (companyCode, companyName, groupRecords)) in grouped)
            {
                var logs = new List<AttendanceLog>();

                try
                {
                    var json = JsonSerializer.Serialize(groupRecords);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var endpoint = $"{hrmUrl}/attendance/pull";
                    _logger.LogInformation(
                        "Sending {Count} attendance records to {Url} for company {Company}",
                        groupRecords.Count, endpoint, companyName);

                    var response = await _httpClient.PostAsync(endpoint, content);

                    if (response.IsSuccessStatusCode)
                    {
                        sent += groupRecords.Count;
                        _logger.LogInformation(
                            "Successfully sent {Count} records to {Company}",
                            groupRecords.Count, companyName);

                        foreach (var r in groupRecords)
                        {
                            logs.Add(new AttendanceLog
                            {
                                EmpId = r.EmpId,
                                AttTime = r.AttTime,
                                CheckingStatus = r.CheckingStatus,
                                VerifyType = r.VerifyType,
                                DeviceId = r.DeviceID,
                                CompanyCode = companyCode,
                                CompanyName = companyName,
                                HrmBaseUrl = hrmUrl,
                                Status = "Sent",
                                ReceivedAt = DateTime.UtcNow,
                                SentAt = DateTime.UtcNow
                            });
                        }
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        failed += groupRecords.Count;
                        _logger.LogWarning(
                            "HRM rejected attendance for {Company}. Status={Status} Error={Error}",
                            companyName, response.StatusCode, error);

                        foreach (var r in groupRecords)
                        {
                            logs.Add(new AttendanceLog
                            {
                                EmpId = r.EmpId,
                                AttTime = r.AttTime,
                                CheckingStatus = r.CheckingStatus,
                                VerifyType = r.VerifyType,
                                DeviceId = r.DeviceID,
                                CompanyCode = companyCode,
                                CompanyName = companyName,
                                HrmBaseUrl = hrmUrl,
                                Status = "Failed",
                                FailureReason = $"HRM returned {response.StatusCode}: {error}",
                                ReceivedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed += groupRecords.Count;
                    _logger.LogError(ex,
                        "Failed to send attendance to {Company} at {Url}", companyName, hrmUrl);

                    foreach (var r in groupRecords)
                    {
                        logs.Add(new AttendanceLog
                        {
                            EmpId = r.EmpId,
                            AttTime = r.AttTime,
                            CheckingStatus = r.CheckingStatus,
                            VerifyType = r.VerifyType,
                            DeviceId = r.DeviceID,
                            CompanyCode = companyCode,
                            CompanyName = companyName,
                            HrmBaseUrl = hrmUrl,
                            Status = "Failed",
                            FailureReason = ex.Message,
                            ReceivedAt = DateTime.UtcNow
                        });
                    }
                }

                await using var db2 = await _dbFactory.CreateDbContextAsync();
                db2.AttendanceLogs.AddRange(logs);
                await db2.SaveChangesAsync();
            }

            return (sent, failed, unknown);
        }
    }
}