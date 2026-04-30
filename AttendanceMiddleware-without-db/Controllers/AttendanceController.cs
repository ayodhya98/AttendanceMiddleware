using AttendanceMiddleware_without_db.DTOs;
using AttendanceMiddleware_without_db.Services;
using AttendanceMiddleware_without_db.Settings;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AttendanceMiddleware_without_db.Controllers
{
    [ApiController]
    [Route("api/attendance")]
    public class AttendanceController : ControllerBase
    {
        private readonly RabbitMqPublisherService _publisher;
        private readonly SqlEmployeeService _employeeService;
        private readonly AttendanceRoutingService _routingService;
        private readonly ILogger<AttendanceController> _logger;

        public AttendanceController(
            RabbitMqPublisherService publisher,
            SqlEmployeeService employeeService,
            AttendanceRoutingService routingService,
            ILogger<AttendanceController> logger)
        {
            _publisher = publisher;
            _employeeService = employeeService;
            _routingService = routingService;
            _logger = logger;
        }

        // Device sends attendance here — middleware routes to correct HRM
        [HttpPost("pull")]
        public async Task<ActionResult<ApiResponse>> ZKTReceiveAttendance(
            [FromBody] List<ZKTAttendanceData> data)
        {
            var jsonString = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            _logger.LogInformation("Received attendance: {Json}", jsonString);

            var (sent, failed, unknown) = await _routingService.RouteAttendanceAsync(data);

            var message = $"Sent={sent} Failed={failed} Unknown={unknown}";
            _logger.LogInformation("Attendance routing complete: {Message}", message);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = message,
                Data = new { Sent = sent, Failed = failed, Unknown = unknown }
            });
        }

        [HttpPost("register-employee")]
        public ActionResult<ApiResponse> RegisterEmployee([FromBody] EmployeeRegistrationDto dto)
        {
            if (dto == null ||
                string.IsNullOrWhiteSpace(dto.ApplicationUserId) ||
                string.IsNullOrWhiteSpace(dto.CompanyCode))
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "ApplicationUserId and CompanyCode are required."
                });

            _logger.LogInformation(
                "New employee registered: EmpNo={EmpNo} Company={CompanyName} Code={CompanyCode}",
                dto.EmpNo, dto.CompanyName, dto.CompanyCode);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = $"Employee {dto.EmpNo} registered for {dto.CompanyName}.",
                Data = dto
            });
        }

        [HttpGet("mappings")]
        public IActionResult GetMappings()
        {
            return Ok(CompanyDeviceMappings.All.Select(m => new
            {
                m.DeviceId,
                m.CompanyName,
                m.HrmBaseUrl,
                QueueName = $"attendance.{m.CompanyName}"
            }));
        }

        [HttpGet("registered-employees")]
        public async Task<IActionResult> GetRegisteredEmployees()
        {
            var employees = await _employeeService.GetAllAsync();
            var successCount = employees.Count(e => e.Status == "Success");
            var failedCount = employees.Count(e => e.Status == "Failed");
            var pendingCount = employees.Count(e => e.Status == "Pending");

            return Ok(new ApiResponse
            {
                Success = true,
                Message = $"Total={employees.Count} Success={successCount} Failed={failedCount} Pending={pendingCount}",
                Data = employees
            });
        }

        [HttpGet("registered-employees/failed")]
        public async Task<IActionResult> GetFailedEmployees()
        {
            var failed = await _employeeService.GetFailedAsync();
            return Ok(new ApiResponse
            {
                Success = true,
                Message = $"{failed.Count} failed employees.",
                Data = failed
            });
        }

        [HttpGet("registered-employees/summary")]
        public async Task<IActionResult> GetSummary()
        {
            var total = await _employeeService.CountAsync();
            var success = await _employeeService.CountByStatusAsync("Success");
            var failed = await _employeeService.CountByStatusAsync("Failed");
            var pending = await _employeeService.CountByStatusAsync("Pending");

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Sync summary",
                Data = new { Total = total, Success = success, Failed = failed, Pending = pending }
            });
        }

        [HttpGet("health")]
        public async Task<IActionResult> Health()
        {
            var total = await _employeeService.CountAsync();
            var success = await _employeeService.CountByStatusAsync("Success");
            var failed = await _employeeService.CountByStatusAsync("Failed");

            return Ok(new
            {
                Status = "Running",
                DatabaseConnected = true,
                Employees = new { Total = total, Success = success, Failed = failed },
                Timestamp = DateTime.UtcNow
            });
        }
    }
}