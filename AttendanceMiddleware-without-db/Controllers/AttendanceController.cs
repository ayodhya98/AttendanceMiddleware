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
        private readonly ILogger<AttendanceController> _logger;

        public AttendanceController(
            RabbitMqPublisherService publisher,
            ILogger<AttendanceController> logger)
        {
            _publisher = publisher;
            _logger = logger;
        }


        [HttpPost("pull")]
        public ActionResult<ApiResponse> ZKTReceiveAttendance(
            [FromBody] List<ZKTAttendanceData> data)
        {
            var jsonString = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });

            _logger.LogInformation("Received attendance: {Json}", jsonString);

            var result = _publisher.RouteAndPublish(data);

            if (!result.Success)
                return StatusCode(500, result);

            return Ok(result);
        }

        // See all registered company mappings and their queues
        // URL: http://YOUR_MIDDLEWARE_SERVER/api/attendance/mappings
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
    }
}