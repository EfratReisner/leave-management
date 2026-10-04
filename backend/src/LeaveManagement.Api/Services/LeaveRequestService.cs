using System.Data;
using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagement.Api.Services;

public class LeaveRequestService
{
    private readonly LeaveDbContext _db;

    public LeaveRequestService(LeaveDbContext db)
    {
        _db = db;
    }

    public List<LeaveRequest> GetAll()
    {
        return _db.LeaveRequests
            .Include(r => r.Employee)
            .OrderByDescending(r => r.StartDate)
            .ToList();
    }

    public List<LeaveRequest> SearchByEmployeeName(string? name)
    {
        var query = _db.LeaveRequests
            .Include(r => r.Employee)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(r => r.Employee != null && r.Employee.Name.Contains(name));
        }

        return query.ToList();
    }

    public ServiceResult<LeaveRequest> Create(CreateLeaveRequestDto dto)
    {
        var employee = _db.Employees.FirstOrDefault(e => e.Id == dto.EmployeeId);
        if (employee == null)
            return ServiceResult<LeaveRequest>.Fail(ErrorKind.NotFound, "Employee not found");

        if (dto.EndDate < dto.StartDate)
            return ServiceResult<LeaveRequest>.Fail(
                ErrorKind.BadRequest, "End date must not be before start date");

        var days = (dto.EndDate - dto.StartDate).Days + 1;

        var year = dto.StartDate.Year;
        var used = _db.LeaveRequests
            .Where(r => r.EmployeeId == dto.EmployeeId
                        && r.Type == LeaveType.Vacation
                        && r.Status != LeaveStatus.Rejected
                        && r.StartDate.Year == year)
            .Sum(r => r.Days);

        if (dto.Type == LeaveType.Vacation && used + days > employee.AnnualQuota)
            return ServiceResult<LeaveRequest>.Fail(
                ErrorKind.BadRequest, "Not enough vacation balance");

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

        return ServiceResult<LeaveRequest>.Ok(request);
    }

    public async Task<ServiceResult<LeaveRequest>> ApproveAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var employeeId = await _db.LeaveRequests
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (int?)r.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        if (employeeId is null)
            return ServiceResult<LeaveRequest>.Fail(
                ErrorKind.NotFound, "Leave request not found");

        await using var tx = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        var employee = await LockEmployeeAsync(employeeId.Value, cancellationToken);
        if (employee is null)
            return ServiceResult<LeaveRequest>.Fail(
                ErrorKind.NotFound, "Employee not found");

        var request = await _db.LeaveRequests
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null)
            return ServiceResult<LeaveRequest>.Fail(
                ErrorKind.NotFound, "Leave request not found");

        if (request.Status == LeaveStatus.Approved)
            return ServiceResult<LeaveRequest>.Fail(
                ErrorKind.Conflict, "Leave request is already approved");

        if (request.Status == LeaveStatus.Rejected)
            return ServiceResult<LeaveRequest>.Fail(
                ErrorKind.Conflict, "Leave request is already rejected");

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
                return ServiceResult<LeaveRequest>.Fail(
                    ErrorKind.BadRequest, "Not enough vacation balance");
        }

        request.Status = LeaveStatus.Approved;
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return ServiceResult<LeaveRequest>.Ok(request);
    }

    private async Task<Employee?> LockEmployeeAsync(
        int employeeId, CancellationToken cancellationToken)
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
