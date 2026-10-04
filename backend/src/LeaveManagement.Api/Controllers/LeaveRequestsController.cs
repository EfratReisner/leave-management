using LeaveManagement.Api.Models;
using LeaveManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaveManagement.Api.Controllers;

[ApiController]
[Route("api/leave-requests")]
public class LeaveRequestsController : ControllerBase
{
    private readonly LeaveRequestService _leaveRequests;

    public LeaveRequestsController(LeaveRequestService leaveRequests)
    {
        _leaveRequests = leaveRequests;
    }

    // GET /api/leave-requests
    [HttpGet]
    public IActionResult GetAll() => Ok(_leaveRequests.GetAll());

    // GET /api/leave-requests/search?name=Dana
    [HttpGet("search")]
    public IActionResult Search([FromQuery] string name) =>
        Ok(_leaveRequests.SearchByEmployeeName(name));

    // POST /api/leave-requests
    [HttpPost]
    public IActionResult Create([FromBody] CreateLeaveRequestDto dto) =>
        ToActionResult(_leaveRequests.Create(dto));

    // POST /api/leave-requests/{id}/approve
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken) =>
        ToActionResult(await _leaveRequests.ApproveAsync(id, cancellationToken));

    private IActionResult ToActionResult<T>(ServiceResult<T> result) =>
        result.Kind switch
        {
            ErrorKind.NotFound => NotFound(result.Error),
            ErrorKind.BadRequest => BadRequest(result.Error),
            ErrorKind.Conflict => Conflict(result.Error),
            _ => Ok(result.Value)
        };
}
