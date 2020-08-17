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

        [HttpPost("{sourceId}/{setId}")]
        public async Task GetDatasetData([FromRoute] string sourceId, [FromRoute] string setId, [FromQuery] string variant, [FromBody] FetchDataRequest body)
        {
            var source = _DataSources.GetSource(sourceId);
            var set = source?.Datasets?.FirstOrDefault(s => s.Id == setId);

            if (source == null || set == null)
            {
                Response.StatusCode = 400;
                return;
            }

            var range = Helper.ParseRange(set.XType, body.From, body.To);

            using (var encoder = new Data.EncodeDataStream(Response.Body))
            {
                Data.IDataWriter writer = encoder;

                if (body.Manipulations != null)
                {
                    foreach (var man in body.Manipulations)
                    {
                        var idx = man.IndexOf(':');
                        if (idx >= 0)
                            writer = Data.Pipes.Pipe.CompilePipe(man.Substring(0, idx), writer, man.Substring(idx + 1));
                        else
                            writer = Data.Pipes.Pipe.CompilePipe(man, writer, string.Empty);
                    }
                }

                // Allow synchronous IO for this request
                // TODO: properly implement asynchronous serialization
                var syncIOFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
                if (syncIOFeature != null)
                {
                    syncIOFeature.AllowSynchronousIO = true;
                }

                await source.GetData(
                    writer,
                    setId,
                    variant,
                    new Data.DataSelectionOptions {
                        From = range.Item1,
                        To = range.Item2
                    },
                    new Data.DataManipulationOptions {

                    }
                );

                await Response.CompleteAsync();
            }
        }
    }
}