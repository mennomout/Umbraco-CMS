using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.ViewModels.LogViewer;
using Umbraco.Cms.Core.Logging.Viewer;

namespace Umbraco.Cms.Api.Management.Controllers.LogViewer.SavedSearch;

public class CreateSavedSearchLogViewerController : SavedSearchLogViewerControllerBase
{
    private readonly ILogViewer _logViewer;

    public CreateSavedSearchLogViewerController(ILogViewer logViewer)
        : base(logViewer) => _logViewer = logViewer;

    /// <summary>
    ///     Creates a saved log search.
    /// </summary>
    /// <param name="savedSearch">The log search to be saved.</param>
    /// <returns>The name of the saved log search after the creation.</returns>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SavedLogSearchViewModel savedSearch)
    {
        try
        {
            _logViewer.AddSavedSearch(savedSearch.Name, savedSearch.Query);
        }
        catch (DuplicateNameException ex)
        {
            var invalidModelProblem = new ProblemDetails
            {
                Title = "Duplicate log search name",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Type = "Error",
            };

            return await Task.FromResult(BadRequest(invalidModelProblem));
        }

        // FIXME: (elit0451) Make use of the extension method of CreatedAtAction
        return await Task.FromResult(Created($"management/api/v1/log-viewer/saved-search/{savedSearch.Name}", null));
    }
}