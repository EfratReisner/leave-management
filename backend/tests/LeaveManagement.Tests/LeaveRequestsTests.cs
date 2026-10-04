using LeaveManagement.Api.Controllers;
using LeaveManagement.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LeaveManagement.Tests;

public class LeaveRequestsTests : IClassFixture<TestDatabase>
{
    private readonly TestDatabase _database;

    public LeaveRequestsTests(TestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public void Create_WithinQuota_Succeeds()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var controller = new LeaveRequestsController(db);

        // Act: request 3 days, well within the quota.
        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 3)
        });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Single(db.LeaveRequests);
    }

    [Fact]
    public void Create_ExceedsRemainingBalance_ReturnsBadRequest()
    {
        // Arrange: quota 20, already used 15 approved vacation days this year.
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        db.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 15),
            Days = 15,
            Status = LeaveStatus.Approved
        });
        db.SaveChanges();

        var controller = new LeaveRequestsController(db);

        // Act: 6 days is under the quota on its own, but 15 + 6 = 21 > 20.
        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 6)
        });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(db.LeaveRequests);
    }

    [Fact]
    public void Create_ExactlyUsesRemainingBalance_Succeeds()
    {
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        db.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 15),
            Days = 15,
            Status = LeaveStatus.Approved
        });
        db.SaveChanges();

        var controller = new LeaveRequestsController(db);

        // 15 used + 5 requested = 20, exactly the quota.
        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 5)
        });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_UsedDaysFromPreviousYear_DoNotCount()
    {
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        db.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 18),
            Days = 18,
            Status = LeaveStatus.Approved
        });
        db.SaveChanges();

        var controller = new LeaveRequestsController(db);

        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 10)
        });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_EndBeforeStart_ReturnsBadRequest()
    {
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var controller = new LeaveRequestsController(db);

        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 5),
            EndDate = new DateTime(2026, 3, 1)
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
