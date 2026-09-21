using Microsoft.AspNetCore.Mvc;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[RetiredPlanningFeature]
[Route("api/mobile/thematic-routes")]
public sealed class ThematicRoutesController : ControllerBase
{
    // Keep the HTTP compatibility surface; editorial route administration is independent.
    [HttpGet]
    public ActionResult<IReadOnlyList<ThematicRouteDto>> List(CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpPost]
    public ActionResult<ThematicRouteDto> Create(CreateThematicRouteDto request, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpPut("{id:guid}")]
    public ActionResult<ThematicRouteDto> Update(Guid id, UpdateThematicRouteDto request, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpPost("{id:guid}/copy")]
    public ActionResult<ThematicRouteDto> Copy(Guid id, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpPost("{id:guid}/prepare-application")]
    public ActionResult<DayProposalDto> Apply(Guid id, ApplyThematicRouteDto request, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();

    [HttpDelete("{id:guid}")]
    public ActionResult Delete(Guid id, [FromQuery] bool removeActivities,
        [FromQuery] int? expectedRevision, CancellationToken ct) =>
        RetiredPlanningFeatureAttribute.CreateResult();
}
