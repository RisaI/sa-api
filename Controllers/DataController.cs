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
        private readonly Services.ResourceCache _ResCache;
    
        public DataController(ILogger<DataController> logger, Services.DataSourceService dataSources, Services.ResourceCache resCache)
        {
            _Logger = logger;
            _DataSources = dataSources;
            _ResCache = resCache;
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

        [HttpPost("{sourceId}/features/{feature}")]
        public async Task<ActionResult<object>> PostFeature([FromRoute] string sourceId, [FromRoute] string feature)
        {
            var source = _DataSources.GetSource(sourceId);

            if (source == null)
                return NotFound();

            return Ok(await source.ActivateFeatureAsync(feature, Request));
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

        [HttpGet("{sourceId}/{setId}/variants")]
        public ActionResult<Data.Dataset> GetVariants([FromRoute] string sourceId, [FromRoute] string setId)
        {
            var source = _DataSources.GetSource(sourceId);
            var set = source?.Datasets?.FirstOrDefault(s => s.Id == setId);

            if (source == null || set == null)
                return NotFound();

            return Ok(set.Variants);
        }

        [HttpPost("{sourceId}/{setId}/bulk")]
        public async Task BulkVariants([FromRoute] string sourceId, [FromRoute] string setId, [FromBody] BulkDataRequest request)
        {
            var source = _DataSources.GetSource(sourceId);
            var set = source?.Datasets?.FirstOrDefault(s => s.Id == setId);

            if (source == null || set == null)
            {
                Response.StatusCode = 404;
                await Response.CompleteAsync();
                return;
            }

            if (request.Variants == null)
                request = request with { Variants = set.Variants };
            
            var range = Data.DataRange.Create(Helper.ParseRange(
                set.XType,
                request.From,
                request.To
            ));

            Response.ContentType = "application/octet-stream";
            await source.GetBulkData(setId, request.Variants, range, Response.Body);
            await Response.CompleteAsync();
        }

        [HttpPost]
        public async Task GetPipelineData([FromBody] FetchDataRequest body)
        {
            Response.ContentType = "application/octet-stream";

            var pipelines = await Task.WhenAll(body.Pipelines.Select(p =>
                Data.Pipes.PipelineCompiler.Compile(p, _DataSources, _ResCache)));

            // Parse and apply X range
            foreach (var pipeline in pipelines)
            {
                pipeline.ApplyXRange(
                    Helper.ParseRange(
                        pipeline.QueryLeafXType(),
                        body.From,
                        body.To
                    )
                );
            }

            using (var encoder = new Data.BinaryDataBuffer())
            {
                // Drain the pipeline into a stream
                foreach (var pipeline in pipelines)
                {
                    await encoder.Consume(pipeline);
                    await encoder.FlushAsync(Response.Body);
                }
            }

            await Response.CompleteAsync();
        }

        [HttpPost("specs")]
        public async Task<IActionResult> GetPipelineSpecs([FromBody] FetchDataRequest body)
        {
            return Ok(
                (await Task.WhenAll(body.Pipelines.Select(p => Data.Pipes.PipelineCompiler.Compile(p, _DataSources, _ResCache)))).Select(p => p.GetSpecs())
            );
        }
    }
}