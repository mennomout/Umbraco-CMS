namespace Umbraco.Cms.Api.Management.ViewModels.Log;

public class SavedLogSearchViewModel
{
    /// <summary>
    ///     Gets or sets the name of the saved search.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    ///     Gets or sets the query of the saved search.
    /// </summary>
    public required string Query { get; set; }
}
