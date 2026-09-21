using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TravelCompanion.Api.Controllers;

[AttributeUsage(AttributeTargets.Class)]
public sealed class RetiredPlanningFeatureAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context) =>
        context.Result = CreateResult();

    internal static ObjectResult CreateResult() =>
        new(new { message = "Rutas y reorganización ya no están disponibles. Usa Mejorar el día desde Today." })
        { StatusCode = StatusCodes.Status410Gone };
}
