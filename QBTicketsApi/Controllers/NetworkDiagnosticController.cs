using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/network-diagnostic")]
    public class NetworkDiagnosticController : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Test()
        {
            const string host =
                "quickbooks.api.intuit.com";

            var result = new
            {
                Host = host,
                Dns = "",
                Tcp443 = "",
                Https = ""
            };

            string dnsResult;
            string tcpResult;
            string httpsResult;

            try
            {
                var sw = Stopwatch.StartNew();

                IPAddress[] addresses =
                    await Dns.GetHostAddressesAsync(host);

                sw.Stop();

                dnsResult =
                    $"OK {sw.ElapsedMilliseconds}ms: " +
                    string.Join(
                        ", ",
                        addresses.Select(x =>
                            $"{x} ({x.AddressFamily})"
                        )
                    );
            }
            catch (Exception ex)
            {
                dnsResult =
                    $"ERROR: {ex.GetType().Name}: {ex.Message}";
            }

            try
            {
                var sw = Stopwatch.StartNew();

                using var tcp =
                    new TcpClient(
                        AddressFamily.InterNetwork
                    );

                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                await tcp.ConnectAsync(
                    host,
                    443,
                    cts.Token
                );

                sw.Stop();

                tcpResult =
                    $"OK {sw.ElapsedMilliseconds}ms";
            }
            catch (Exception ex)
            {
                tcpResult =
                    $"ERROR: {ex.GetType().Name}: {ex.Message}";
            }

            try
            {
                using var handler =
                    new SocketsHttpHandler
                    {
                        ConnectTimeout =
                            TimeSpan.FromSeconds(10)
                    };

                using var client =
                    new HttpClient(handler)
                    {
                        Timeout =
                            TimeSpan.FromSeconds(15)
                    };

                var sw = Stopwatch.StartNew();

                using HttpResponseMessage response =
                    await client.GetAsync(
                        "https://quickbooks.api.intuit.com/"
                    );

                sw.Stop();

                httpsResult =
                    $"OK HTTP {(int)response.StatusCode} " +
                    $"{response.StatusCode} " +
                    $"{sw.ElapsedMilliseconds}ms";
            }
            catch (Exception ex)
            {
                httpsResult =
                    $"ERROR: {ex.GetType().Name}: {ex.Message}";
            }

            return Ok(new
            {
                Host = host,
                Dns = dnsResult,
                Tcp443 = tcpResult,
                Https = httpsResult
            });
        }
    }
}