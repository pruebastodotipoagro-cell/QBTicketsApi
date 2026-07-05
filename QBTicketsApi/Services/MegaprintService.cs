using System.Xml.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace QBTicketsApi.Services
{
    public class MegaprintService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public MegaprintService(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        public async Task<string> SolicitarTokenAsync()
        {
            string usuario = _config["Megaprint:Usuario"] ?? "";
            string apiKey = _config["Megaprint:ApiKey"] ?? "";
            string url = _config["Megaprint:TokenUrl"] ?? "";

            var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<SolicitaTokenRequest>
  <usuario>{usuario}</usuario>
  <apikey>{apiKey}</apikey>
</SolicitaTokenRequest>";

            var client = _httpClientFactory.CreateClient();

            var content = new StringContent(xml, Encoding.UTF8, "application/xml");

            var response = await client.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception("Error solicitando token Megaprint: " + responseText);

            var doc = XDocument.Parse(responseText);

            var tipo = doc.Descendants("tipo_respuesta").FirstOrDefault()?.Value;
            if (tipo != "0")
                throw new Exception("Megaprint respondió error: " + responseText);

            var token = doc.Descendants("token").FirstOrDefault()?.Value;

            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("Megaprint no devolvió token: " + responseText);

            return token;
        }
    }
}