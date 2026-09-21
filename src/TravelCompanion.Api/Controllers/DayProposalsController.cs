using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[RetiredPlanningFeature]
[Route("api/mobile/proposals")]
public sealed class DayProposalsController : ControllerBase
{
    // Preserve legacy routes and binding contracts without activating retired planning services.
    [HttpPost]
    public ActionResult<DayProposalDto> Create(DayProposalRequestDto request, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpGet("{id:guid}")]
    public ActionResult<DayProposalDto> Get(Guid id, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpPatch("{id:guid}")]
    public ActionResult<DayProposalDto> Revise(Guid id, ReviseDayProposalDto request, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpPost("{id:guid}/apply")]
    public ActionResult<ItineraryChangeSetDto> Apply(Guid id, ApplyDayProposalDto request, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpPost("operations/{id:guid}/undo")]
    public ActionResult<ItineraryChangeSetDto> Undo(Guid id, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();
}
