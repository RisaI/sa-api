using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
        public ActionResult<IEnumerable<Data.IDataSource>> GetAllSets()
        {
            return Ok(_DataSources.AllDataSets);
        }
    
        [HttpGet("Sources")]
        public ActionResult<IEnumerable<Data.IDataSource>> GetAllSources()
        {
            return Ok(_DataSources.AllSources);
        }


        [HttpGet("{source}")]
        public ActionResult<object> GetSource([FromRoute] string source)
        {
            return Ok(_DataSources.GetSource(source));
        }

        [HttpGet("{source}/{set}")]
        public async Task<ActionResult> GetDataset([FromRoute] string source, [FromRoute] string set)
        {
            using (var stream = new Data.EncodeDataStream())
            {
                await _DataSources.GetTrace(
                    stream,
                    source,
                    set,
                    new Data.DataSelectionOptions {

                    },
                    new Data.DataManipulationOptions {

                    }
                );

                return Ok(await stream.CreateEncodedString());
            }
        }
    }
}