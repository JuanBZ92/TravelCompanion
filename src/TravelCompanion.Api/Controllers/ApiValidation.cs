using Microsoft.AspNetCore.Mvc;

namespace TravelCompanion.Api.Controllers;

internal static class ApiValidation
{
    public static ActionResult ValidationError(this ControllerBase controller, string field, string message)
    {
        controller.ModelState.AddModelError(field, message);
        return controller.ValidationProblem(controller.ModelState);
    }
}
