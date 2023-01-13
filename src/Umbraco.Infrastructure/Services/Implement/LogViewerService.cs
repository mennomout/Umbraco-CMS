using Serilog.Events;
using System.Collections.ObjectModel;
using System.Text.Json;
using Umbraco.Cms.Core.Logging.Viewer;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Extensions;
using Umbraco.New.Cms.Core.Models;
using LogLevel = Umbraco.Cms.Core.Logging.LogLevel;

namespace Umbraco.Cms.Core.Services.Implement;

public class LogViewerService : ILogViewerService
{
    private readonly ILogViewerQueryRepository _logViewerQueryRepository;
    private readonly ILogViewer _logViewer;
    private readonly ILogLevelLoader _logLevelLoader;
    private readonly ICoreScopeProvider _provider;

    public LogViewerService(
        ILogViewerQueryRepository logViewerQueryRepository,
        ILogViewer logViewer,
        ILogLevelLoader logLevelLoader,
        ICoreScopeProvider provider)
    {
        _logViewerQueryRepository = logViewerQueryRepository;
        _logViewer = logViewer;
        _logLevelLoader = logLevelLoader;
        _provider = provider;
    }

    public Attempt<PagedModel<ILogEntry>> GetPagedLogs(
        DateTime? startDate,
        DateTime? endDate,
        int skip,
        int take,
        Direction orderDirection = Direction.Descending,
        string? filterExpression = null,
        string[]? logLevels = null)
    {
        LogTimePeriod logTimePeriod = GetTimePeriod(startDate, endDate);

        // We will need to stop the request if trying to do this on a 1GB file
        if (CanViewLogs(logTimePeriod) == false)
        {
            return Attempt<PagedModel<ILogEntry>>.Fail();
        }

        PagedModel<LogMessage> logMessages = _logViewer.GetLogsAsPagedModel(logTimePeriod, skip, take, orderDirection, filterExpression, logLevels);

        var logEntries = new PagedModel<ILogEntry>(logMessages.Total, logMessages.Items.Select(x => ToLogEntry(x)));

        return Attempt<PagedModel<ILogEntry>>.Succeed(logEntries);
    }

    public async Task<IReadOnlyList<ILogViewerQuery>> GetSavedLogQueriesAsync()
    {
        using ICoreScope scope = _provider.CreateCoreScope(autoComplete: true);
        return await Task.FromResult(_logViewerQueryRepository.GetMany().ToList());
    }

    public async Task<ILogViewerQuery?> GetSavedLogQueryByNameAsync(string name)
    {
        using ICoreScope scope = _provider.CreateCoreScope(autoComplete: true);
        return await Task.FromResult(_logViewerQueryRepository.GetByName(name));
    }

    public async Task<bool> AddSavedLogQueryAsync(string name, string query)
    {
        ILogViewerQuery? logViewerQuery = await GetSavedLogQueryByNameAsync(name);

        if (logViewerQuery is not null)
        {
            return false;
        }

        using ICoreScope scope = _provider.CreateCoreScope(autoComplete: true);
        _logViewerQueryRepository.Save(new LogViewerQuery(name, query));

        return true;
    }

    public async Task<bool> DeleteSavedLogQueryAsync(string name)
    {
        ILogViewerQuery? logViewerQuery = await GetSavedLogQueryByNameAsync(name);

        if (logViewerQuery is null)
        {
            return false;
        }

        using ICoreScope scope = _provider.CreateCoreScope(autoComplete: true);
        _logViewerQueryRepository.Delete(logViewerQuery);

        return true;
    }

    public async Task<bool> CanViewLogsAsync(DateTime? startDate, DateTime? endDate)
    {
        LogTimePeriod logTimePeriod = GetTimePeriod(startDate, endDate);

        return await Task.FromResult(CanViewLogs(logTimePeriod));
    }

    public Attempt<LogLevelCounts> GetLogLevelCounts(DateTime? startDate, DateTime? endDate)
    {
        LogTimePeriod logTimePeriod = GetTimePeriod(startDate, endDate);

        // We will need to stop the request if trying to do this on a 1GB file
        if (CanViewLogs(logTimePeriod) == false)
        {
            return Attempt<LogLevelCounts>.Fail();
        }

        return Attempt<LogLevelCounts>.Succeed(_logViewer.GetLogLevelCounts(logTimePeriod));
    }

    public Attempt<IEnumerable<LogTemplate>> GetMessageTemplates(DateTime? startDate, DateTime? endDate)
    {
        LogTimePeriod logTimePeriod = GetTimePeriod(startDate, endDate);

        // We will need to stop the request if trying to do this on a 1GB file
        if (CanViewLogs(logTimePeriod) == false)
        {
            return Attempt<IEnumerable<LogTemplate>>.Fail();
        }

        return Attempt<IEnumerable<LogTemplate>>.Succeed(_logViewer.GetMessageTemplates(logTimePeriod));
    }

    public ReadOnlyDictionary<string, LogLevel> GetLogLevelsFromSinks()
    {
        var configuredLogLevels = _logLevelLoader.GetLogLevelsFromSinks();

        return configuredLogLevels.ToDictionary(logLevel => logLevel.Key, logLevel => Enum.Parse<LogLevel>(logLevel.Value!.ToString()!)).AsReadOnly();
    }

    /// <summary>
    ///     Get the minimum log level value from the config file.
    /// </summary>
    public LogLevel GetGlobalMinLogLevel()
    {
        var serilogLogLevel = _logLevelLoader.GetGlobalMinLogLevel();

        return Enum.Parse<LogLevel>(serilogLogLevel!.ToString()!);
    }

    /// <summary>
    ///     Returns a <see cref="LogTimePeriod" /> representation from a start and end date for filtering log files.
    /// </summary>
    /// <param name="startDate">The start date for the date range (can be null).</param>
    /// <param name="endDate">The end date for the date range (can be null).</param>
    /// <returns>The LogTimePeriod object used to filter logs.</returns>
    private LogTimePeriod GetTimePeriod(DateTime? startDate, DateTime? endDate)
    {
        if (startDate is null || endDate is null)
        {
            DateTime now = DateTime.Now;
            if (startDate is null)
            {
                startDate = now.AddDays(-1);
            }

            if (endDate is null)
            {
                endDate = now;
            }
        }

        return new LogTimePeriod(startDate.Value, endDate.Value);
    }

    /// <summary>
    ///     Returns a value indicating whether to stop a GET request that is attempting to fetch logs from a 1GB file.
    /// </summary>
    /// <param name="logTimePeriod">The time period to filter the logs.</param>
    /// <returns>The value whether or not you are able to view the logs.</returns>
    private bool CanViewLogs(LogTimePeriod logTimePeriod)
    {
        // Check if the interface can deal with large files
        if (_logViewer.CanHandleLargeLogs)
        {
            return true;
        }

        return _logViewer.CheckCanOpenLogs(logTimePeriod);
    }

    private ILogEntry ToLogEntry(LogMessage logMessage)
    {
        return new LogEntry()
        {
            Timestamp = logMessage.Timestamp,
            Level = Enum.Parse<LogLevel>(logMessage.Level.ToString()),
            MessageTemplateText = logMessage.MessageTemplateText,
            RenderedMessage = logMessage.RenderedMessage,
            Properties = MapLogMessageProperties(logMessage.Properties),
            Exception = logMessage.Exception
        };
    }

    private IReadOnlyDictionary<string, string?> MapLogMessageProperties(IReadOnlyDictionary<string, LogEventPropertyValue>? properties)
    {
        var result = new Dictionary<string, string?>();

        if (properties is not null)
        {
            foreach (KeyValuePair<string, LogEventPropertyValue> property in properties)
            {
                string? value;

                if (property.Value is ScalarValue scalarValue)
                {
                    value = scalarValue.Value?.ToString();
                }
                else
                {
                    // When polymorphism is implemented, this should be changed
                    value = JsonSerializer.Serialize(property.Value as object);
                }

                result.Add(property.Key, value);
            }
        }

        return result.AsReadOnly();
    }
}