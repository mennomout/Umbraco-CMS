﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.ViewModels.Template;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;

namespace Umbraco.Cms.Api.Management.Controllers.Template;

public class CreateTemplateController : TemplateControllerBase
{
    private readonly ITemplateService _templateService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;

    public CreateTemplateController(
        ITemplateService templateService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor)
    {
        _templateService = templateService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
    }

    [HttpPost]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(TemplateCreateModel createModel)
    {
        Attempt<ITemplate, TemplateOperationStatus> result = await _templateService.CreateAsync(
            createModel.Name,
            createModel.Alias,
            createModel.Content,
            CurrentUserId(_backOfficeSecurityAccessor));

        return result.Success
            ? CreatedAtAction<ByKeyTemplateController>(controller => nameof(controller.ByKey), result.Result.Key)
            : TemplateOperationStatusResult(result.Status);
    }
}
