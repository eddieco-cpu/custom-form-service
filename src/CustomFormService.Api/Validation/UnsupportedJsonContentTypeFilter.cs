using CustomFormService.Api.Contracts.Errors;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CustomFormService.Api.Validation;

// Authentication/authorization middleware runs before MVC. Only replace the
// input formatter's unsupported-media error, before MVC's order -3000 filter.
public sealed class UnsupportedJsonContentTypeFilter : IActionFilter, IOrderedFilter
{
    public int Order => -3001;
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.Values.SelectMany(value => value.Errors)
            .Any(error => error.Exception is UnsupportedContentTypeException))
            context.Result = ApiErrors.Validation([RequestJson.Invalid("body", "The request Content-Type must be application/json.")]);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
