using Mediator;
using MenuNest.Application.UseCases.Health.Photos.RequestUploadSas;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/photos")]
public sealed class PhotosController : ControllerBase
{
    private readonly IMediator _mediator;

    public PhotosController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Issues a short-lived, single-blob SAS URL the browser uses to PUT
    /// a photo directly to Azure Blob Storage. The client then calls the
    /// parent entity's <c>/photos</c> endpoint with the returned
    /// <c>blobUrl</c> to register the photo.
    /// </summary>
    [HttpPost("upload-sas")]
    public async Task<ActionResult<UploadSasResponse>> RequestUploadSas(
        [FromBody] RequestUploadSasRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RequestUploadSasCommand(request.ContainerKey, request.ParentId, request.ContentType),
            ct);
        return Ok(result);
    }
}

public sealed record RequestUploadSasRequest(
    string ContainerKey,
    Guid ParentId,
    string ContentType);
