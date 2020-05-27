using System;
using Microsoft.AspNetCore.Mvc;

namespace SAApi.Controllers
{
    [ApiVersion("2.0")]
    [ApiController]
    [Route( "api/v{version:apiVersion}/storage-controller" )]
    public class StorageEntitiesController : ControllerBase
    {
        [HttpPost]
        public IActionResult Post([FromBody] Models.StorageEntityRequest request)
        {
            // TODO: create an entity from the request, transform it and return
            return Ok("PLACEHOLDER");
        }
    }
}