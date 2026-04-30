using AttendanceMiddleware_without_db.Data;
using AttendanceMiddleware_without_db.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceMiddleware_without_db.Services
{
    public class SqlEmployeeService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<SqlEmployeeService> _logger;

        public SqlEmployeeService(
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<SqlEmployeeService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        // Called by consumer — upsert and set status
        public async Task UpsertEmployeeAsync(EmployeeRegisteredMessage employee)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var existing = await db.RegisteredEmployees
                .FirstOrDefaultAsync(e =>
                    e.EmpNo == employee.EmpNo &&
                    e.CompanyCode == employee.CompanyCode);

            if (existing == null)
            {
                employee.Status = "Success";
                employee.ReceivedAt = DateTime.UtcNow;
                db.RegisteredEmployees.Add(employee);
                _logger.LogInformation(
                    "Inserting new employee: EmpNo={EmpNo} Status=Success", employee.EmpNo);
            }
            else
            {
                existing.FirstName = employee.FirstName;
                existing.LastName = employee.LastName;
                existing.FullName = employee.FullName;
                existing.CompanyName = employee.CompanyName;
                existing.ApplicationUserId = employee.ApplicationUserId;
                existing.PublishedAt = employee.PublishedAt;
                existing.ReceivedAt = DateTime.UtcNow;
                existing.Status = "Success";
                existing.FailureReason = null;
                existing.LastRetryAt = DateTime.UtcNow;
                existing.RetryCount += 1;
                _logger.LogInformation(
                    "Updating existing employee: EmpNo={EmpNo} Status=Success", employee.EmpNo);
            }

            await db.SaveChangesAsync();
        }

        // Mark a record as failed with reason
        public async Task MarkFailedAsync(string empNo, string companyCode, string reason)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var existing = await db.RegisteredEmployees
                .FirstOrDefaultAsync(e =>
                    e.EmpNo == empNo &&
                    e.CompanyCode == companyCode);

            if (existing != null)
            {
                existing.Status = "Failed";
                existing.FailureReason = reason;
                existing.LastRetryAt = DateTime.UtcNow;
                existing.RetryCount += 1;
                await db.SaveChangesAsync();
            }
            else
            {
                // Insert as failed so we have a record
                db.RegisteredEmployees.Add(new EmployeeRegisteredMessage
                {
                    EmpNo = empNo,
                    CompanyCode = companyCode,
                    Status = "Failed",
                    FailureReason = reason,
                    ReceivedAt = DateTime.UtcNow,
                    RetryCount = 1
                });
                await db.SaveChangesAsync();
            }
        }

        public async Task<List<EmployeeRegisteredMessage>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.RegisteredEmployees
                .OrderByDescending(e => e.ReceivedAt)
                .ToListAsync();
        }

        public async Task<List<EmployeeRegisteredMessage>> GetFailedAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.RegisteredEmployees
                .Where(e => e.Status == "Failed")
                .OrderByDescending(e => e.LastRetryAt)
                .ToListAsync();
        }

        public async Task<long> CountAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.RegisteredEmployees.LongCountAsync();
        }

        public async Task<long> CountByStatusAsync(string status)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.RegisteredEmployees
                .Where(e => e.Status == status)
                .LongCountAsync();
        }
    }
}