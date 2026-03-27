namespace AttendanceMiddleware_without_db.DTOs
{
    public class ZKTAttendanceData
    {
        public string EmpId { get; set; } = string.Empty;
        public string AttTime { get; set; } = string.Empty;
        public string CheckingStatus { get; set; } = string.Empty;
        public string VerifyType { get; set; } = string.Empty;
        public string DeviceID { get; set; } = string.Empty;
    }

    // What we publish to RabbitMQ
    public class AttendanceMessage
    {
        public string EmpId { get; set; } = string.Empty;
        public string AttTime { get; set; } = string.Empty;
        public string CheckingStatus { get; set; } = string.Empty;
        public string VerifyType { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    }

    // Standard API response
    public class ApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public object Data { get; set; }
    }
}
