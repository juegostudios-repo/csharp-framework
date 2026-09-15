using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
using API.Models;

namespace API.Controllers
{
    // Benchmark-only endpoint. Drives the SQLManager DB path directly (no auth) so a load
    // test can exercise connection pooling + per-query SetTypeMap under concurrency.
    [Route("/bench")]
    [ApiController]
    [Response]
    public class BenchController : ControllerBase
    {
        // How many rows were seeded; random lookups are spread across [1, SeededRows].
        private static readonly int SeededRows =
            int.TryParse(Environment.GetEnvironmentVariable("BENCH_SEED_ROWS"), out var r) ? r : 1000;

        // GET /bench/queries?n=5  -> runs n independent SQLManager.FindOne<User> lookups.
        // Each lookup rents a pooled connection, so n scales the per-request pool pressure.
        [HttpGet("queries")]
        public async Task<IActionResult> Queries([FromQuery] int n = 5)
        {
            var found = 0;
            for (var i = 0; i < n; i++)
            {
                var id = Random.Shared.Next(1, SeededRows + 1);
                var user = await SQLManager.FindOne<User>(new { user_id = id });
                if (user != null)
                {
                    found++;
                }
            }

            return ApiResponse.setResponse("SUCCESS", new { queries = n, found });
        }
    }
}
