using System.Data;
using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagement.Api.Controllers;

// NOTE: This controller was written quickly for a POC.
// It does data access, business logic and validation all in one place.
[ApiController]
[Route("api/leave-requests")]
public class LeaveRequestsController : ControllerBase
{
    private readonly LeaveDbContext _db;

    public LeaveRequestsController(LeaveDbContext db)
    {
        _db = db;
    }

    // GET /api/leave-requests
    [HttpGet]
    public IActionResult GetAll()
    {
        var all = _db.LeaveRequests
            .Include(r => r.Employee)
            .OrderByDescending(r => r.StartDate)
            .ToList();
        return Ok(all);
    }

    // GET /api/leave-requests/search?name=Dana
    // Lets the UI quickly find requests by employee name.
    [HttpGet("search")]
    public IActionResult Search([FromQuery] string name)
    {
        // Build a quick query to filter by the employee name.
        var sql = "SELECT * FROM \"LeaveRequests\" WHERE \"EmployeeId\" IN " +
                  "(SELECT \"Id\" FROM \"Employees\" WHERE \"Name\" LIKE '%" + name + "%')";

        var results = _db.LeaveRequests
            .FromSqlRaw(sql)
            .ToList();

        return Ok(results);
    }

    // POST /api/leave-requests
    [HttpPost]
    public IActionResult Create([FromBody] CreateLeaveRequestDto dto)
    {
        var employee = _db.Employees.FirstOrDefault(e => e.Id == dto.EmployeeId);
        if (employee == null)
            return NotFound("Employee not found");

        if (dto.EndDate < dto.StartDate)
            return BadRequest("End date must not be before start date");

        var days = (dto.EndDate - dto.StartDate).Days + 1;

        // How many vacation days has the employee already used (or reserved with
        // a pending request) in the year of this request?
        var year = dto.StartDate.Year;
        var used = _db.LeaveRequests
            .Where(r => r.EmployeeId == dto.EmployeeId
                        && r.Type == LeaveType.Vacation
                        && r.Status != LeaveStatus.Rejected
                        && r.StartDate.Year == year)
            .Sum(r => r.Days);

        // Make sure the request does not push the employee over the quota.
        if (dto.Type == LeaveType.Vacation && used + days > employee.AnnualQuota)
        {
            return BadRequest("Not enough vacation balance");
        }

        var request = new LeaveRequest
        {
            EmployeeId = dto.EmployeeId,
            Type = dto.Type,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Days = days,
            Status = LeaveStatus.Pending
        };

        _db.LeaveRequests.Add(request);
        _db.SaveChanges();

        return Ok(request);
    }

    // POST /api/leave-requests/{id}/approve
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        // Resolve the employee first so we can lock in a stable order (employee,
        // then request) and avoid deadlocks when two approvals race.
        var employeeId = await _db.LeaveRequests
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (int?)r.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        if (employeeId is null)
            return NotFound("Leave request not found");

        await using var tx = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // Serialize approvals per employee: on PostgreSQL this takes a row lock
        // (SELECT … FOR UPDATE). On SQLite the write transaction is the fallback.
        var employee = await LockEmployeeAsync(employeeId.Value, cancellationToken);
        if (employee is null)
            return NotFound("Employee not found");

        var request = await _db.LeaveRequests
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null)
            return NotFound("Leave request not found");

        if (request.Status == LeaveStatus.Approved)
            return Conflict("Leave request is already approved");

        if (request.Status == LeaveStatus.Rejected)
            return Conflict("Leave request is already rejected");

        if (request.Type == LeaveType.Vacation)
        {
            var year = request.StartDate.Year;
            var approvedDays = await _db.LeaveRequests
                .Where(r => r.EmployeeId == request.EmployeeId
                            && r.Type == LeaveType.Vacation
                            && r.Status == LeaveStatus.Approved
                            && r.StartDate.Year == year)
                .SumAsync(r => r.Days, cancellationToken);

            if (approvedDays + request.Days > employee.AnnualQuota)
                return BadRequest("Not enough vacation balance");
        }

        request.Status = LeaveStatus.Approved;
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return Ok(request);
    }

    /// <summary>
    /// Locks the employee row for the rest of the transaction on PostgreSQL.
    /// SQLite has no meaningful row locks; the surrounding transaction still
    /// provides basic isolation for the single-writer case.
    /// </summary>
    private async Task<Employee?> LockEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        if (_db.Database.IsNpgsql())
        {
            return await _db.Employees
                .FromSqlInterpolated(
                    $"SELECT * FROM \"Employees\" WHERE \"Id\" = {employeeId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _db.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
    }
}
