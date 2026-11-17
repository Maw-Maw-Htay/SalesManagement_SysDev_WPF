namespace SalesManagement_SysDev;

public sealed class EmployeeAssignmentHistory
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
    public int ActorEmployeeId { get; set; }
    public string ActorName { get; set; } = "";
    public int? BeforeOfficeId { get; set; }
    public string? BeforeOfficeName { get; set; }
    public int AfterOfficeId { get; set; }
    public string AfterOfficeName { get; set; } = "";
    public int? BeforePositionId { get; set; }
    public string? BeforeRoleName { get; set; }
    public int AfterPositionId { get; set; }
    public string AfterRoleName { get; set; } = "";
    public string Operation { get; set; } = "";
}
