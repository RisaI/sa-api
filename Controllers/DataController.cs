using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SAApi.Models;

namespace SAApi.Controllers
{
    [ApiVersion("2.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/data")]
    public class DataController : ControllerBase
    {
        private readonly ILogger _Logger;
        private readonly Services.DataSourceService _DataSources;
    
        public DataController(ILogger<DataController> logger, Services.DataSourceService dataSources)
        {
            _Logger = logger;
            _DataSources = dataSources;
        }
    
        [HttpGet]
        public ActionResult<IEnumerable<Data.DataSource>> GetAllSets()
        {
            return Ok(_DataSources.AllSources);
        }

        [HttpGet("{source}")]
        public ActionResult<object> GetSource([FromRoute] string source)
        {
            return Ok(_DataSources.GetSource(source));
        }

        [HttpGet("{sourceId}/{setId}")]
        public ActionResult<Data.Dataset> GetDataset([FromRoute] string sourceId, [FromRoute] string setId)
        {
            var source = _DataSources.GetSource(sourceId);
            var set = source?.Datasets?.FirstOrDefault(s => s.Id == setId);

            if (source == null || set == null)
                return NotFound();

            return Ok(set);
        }

        [HttpPost]
        public async Task GetPipelineData([FromBody] FetchDataRequest body)
        {
            var pipeline = await Data.Pipes.PipelineCompiler.Compile(
                body.Pipeline,
                _DataSources
            );

            // Parse and apply X range
            pipeline.ApplyXRange(
                Helper.ParseRange(
                    pipeline.QueryLeafXType(),
                    body.From,
                    body.To
                )
            );

            using (var encoder = new Data.EncodeDataStream(Response.Body))
            {
                // Allow synchronous IO for this request
                // TODO: properly implement asynchronous serialization
                var syncIOFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
                if (syncIOFeature != null)
                {
                    syncIOFeature.AllowSynchronousIO = true;
                }
                
                // Drain the pipeline into a stream
                await encoder.Consume(pipeline);
                await Response.CompleteAsync();
            }
        }

        [HttpPost("specs")]
        public async Task<IActionResult> GetPipelineSpecs([FromBody] FetchDataRequest body)
        {
            var pipeline = await Data.Pipes.PipelineCompiler.Compile(
                body.Pipeline,
                _DataSources
            );

            return Ok(new PipelineSpecs {
                XType = pipeline.XType,
                YType = pipeline.YType,
            });
        }
    }
}