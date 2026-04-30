using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AttendanceMiddleware_without_db.Entities
{
    [Table("RegisteredEmployees")]
    public class EmployeeRegisteredMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string ApplicationUserId { get; set; } = string.Empty;
        public string EmpNo { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyCode { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public DateTime ReceivedAt { get; set; }

        public string Status { get; set; } = "Pending"; 
        public string? FailureReason { get; set; }
        public int RetryCount { get; set; } = 0;
        public DateTime? LastRetryAt { get; set; }
        public string? HrmBaseUrl { get; set; } = string.Empty;

    }
}