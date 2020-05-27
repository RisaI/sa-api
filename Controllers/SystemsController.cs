using System;
using Microsoft.AspNetCore.Mvc;

namespace SAApi.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route( "api/v{version:apiVersion}/systems" )]
    public class SystemsController : ControllerBase
    {
        [HttpPut("{sysName}/{compType}/{compName}/externals")]
        public IActionResult PutExternals(
            [FromRoute] string sysName,
            [FromRoute] string compName,
            [FromRoute] string compType,
            [FromBody] Models.ExternalRequest req)
        {
            // TODO: create a component key, put externals, transform result
            return Ok("PLACEHOLDER");
        }

        [HttpPost("{compType}/{sysName}/metrics")]
        [HttpPost("{sysName}/{compType}/{compName}/metrics")]
        [HttpPost("{sysName}/chas/{compName}/{compType}/{portName}/metrics")]
        public IActionResult PostSimpleMetric(
            [FromRoute] string sysName,
            [FromRoute] string compName,
            [FromRoute] string compType,
            [FromRoute] string portName,
            [FromBody] Models.MetricRequest req)
        {
            // TODO:
            return Ok("PLACEHOLDER");
        }

        [HttpPost("{sysName}/pools/{compName}/latencyPerBlockSize")]
        public IActionResult PostMultiValueMetric(
            [FromRoute] string sysName,
            [FromRoute] string compName)
        {
            // TODO:
            return Ok("PLACEHOLDER");
        }

        [HttpPut("{compType}/{sysName}/status")]
        [HttpPut("systems/{sysName}/{compType}/{compName}/status")]
        [HttpPut("systems/{sysName}/chas/{compName}/{compType}/{portName}/status")]
        public IActionResult PutStatus(
            [FromRoute] string sysName,
            [FromRoute] string compName,
            [FromRoute] string compType,
            [FromRoute] string portName,
            [FromBody] Models.ChangeStatusRequest req)
        {
            // TODO:
            return Ok("PLACEHOLDER");
        }
    }
}