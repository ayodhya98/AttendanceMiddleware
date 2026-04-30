using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AttendanceMiddleware_without_db.Entities
{
    [Table("AttendanceLogs")]
    public class AttendanceLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string EmpId { get; set; } = string.Empty;
        public string AttTime { get; set; } = string.Empty;
        public string CheckingStatus { get; set; } = string.Empty;
        public string VerifyType { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string CompanyCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string HrmBaseUrl { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string? FailureReason { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
    }
}