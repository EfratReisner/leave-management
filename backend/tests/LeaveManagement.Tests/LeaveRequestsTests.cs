using LeaveManagement.Api.Controllers;
using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using LeaveManagement.Api.Services;
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

    private static LeaveRequestsController CreateController(LeaveDbContext db) =>
        new(new LeaveRequestService(db));

    [Fact]
    public void Create_WithinQuota_Succeeds()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var controller = CreateController(db);

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

        var controller = CreateController(db);

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

        var controller = CreateController(db);

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

        var controller = CreateController(db);

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

        var controller = CreateController(db);

        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 5),
            EndDate = new DateTime(2026, 3, 1)
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Approve_PendingRequest_Succeeds()
    {
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var request = new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 3),
            Days = 3,
            Status = LeaveStatus.Pending
        };
        db.LeaveRequests.Add(request);
        db.SaveChanges();

        var controller = CreateController(db);

        var result = await controller.Approve(request.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var approved = Assert.IsType<LeaveRequest>(ok.Value);
        Assert.Equal(LeaveStatus.Approved, approved.Status);
        Assert.Equal(LeaveStatus.Approved, db.LeaveRequests.Single().Status);
    }

    [Fact]
    public async Task Approve_UnknownId_ReturnsNotFound()
    {
        using var db = _database.NewDb();
        var controller = CreateController(db);

        var result = await controller.Approve(999, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_ReturnsConflict()
    {
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var request = new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 3),
            Days = 3,
            Status = LeaveStatus.Approved
        };
        db.LeaveRequests.Add(request);
        db.SaveChanges();

        var controller = CreateController(db);

        var result = await controller.Approve(request.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Approve_AlreadyRejected_ReturnsConflict()
    {
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var request = new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 3),
            Days = 3,
            Status = LeaveStatus.Rejected
        };
        db.LeaveRequests.Add(request);
        db.SaveChanges();

        var controller = CreateController(db);

        var result = await controller.Approve(request.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(LeaveStatus.Rejected, db.LeaveRequests.Single().Status);
    }

    [Fact]
    public async Task Approve_SecondRequestExceedingQuota_ReturnsBadRequest()
    {
        // Two pending requests that each fit alone but together exceed the quota.
        // Seeded directly to simulate a race / legacy data that Create would block.
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var first = new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 12),
            Days = 12,
            Status = LeaveStatus.Pending
        };
        var second = new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 4, 1),
            EndDate = new DateTime(2026, 4, 12),
            Days = 12,
            Status = LeaveStatus.Pending
        };
        db.LeaveRequests.AddRange(first, second);
        db.SaveChanges();

        var controller = CreateController(db);

        var firstResult = await controller.Approve(first.Id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(firstResult);

        var secondResult = await controller.Approve(second.Id, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(secondResult);
        Assert.Equal(LeaveStatus.Pending, db.LeaveRequests.Single(r => r.Id == second.Id).Status);
    }
}
