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

        public async Task<string> SolicitarFirmaAsync(string xmlDte, string token)
        {
            string url = _config["Megaprint:FirmaUrl"] ?? "";
            string requestId = Guid.NewGuid().ToString().ToUpperInvariant();

            // Usamos XElement + XCData para que el XML se genere bien formado,
            // sin depender de que un humano copie/pegue el contenido a mano.
            var requestXml = new XElement("FirmaDocumentoRequest",
                new XAttribute("id", requestId),
                new XElement("xml_dte", new XCData(xmlDte))
            );

            string body = new XDeclaration("1.0", "UTF-8", null) + Environment.NewLine + requestXml;

            var client = _httpClientFactory.CreateClient();
            var content = new StringContent(body, Encoding.UTF8, "application/xml");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception("Error HTTP solicitando firma Megaprint: " + responseText);

            var doc = XDocument.Parse(responseText);

            var tipo = doc.Descendants("tipo_respuesta").FirstOrDefault()?.Value;
            if (tipo != "0")
            {
                var codErr = doc.Descendants("cod_error").FirstOrDefault()?.Value ?? "sin código";
                var descErr = doc.Descendants("desc_error").FirstOrDefault()?.Value ?? "sin descripción";
                throw new Exception($"Megaprint rechazó la firma [{codErr}]: {descErr}");
            }

            var xmlFirmado = doc.Descendants("xml_dte").FirstOrDefault()?.Value;

            if (string.IsNullOrWhiteSpace(xmlFirmado))
                throw new Exception("Megaprint no devolvió xml_dte firmado: " + responseText);

            return xmlFirmado;
        }

        public async Task<(string xmlCertificado, string uuid)> RegistrarDocumentoAsync(string xmlDteFirmado, string token)
        {
            string url = _config["Megaprint:RegistroUrl"] ?? "";
            string requestId = Guid.NewGuid().ToString().ToUpperInvariant();

            var requestXml = new XElement("RegistraDocumentoXMLRequest",
                new XAttribute("id", requestId),
                new XElement("xml_dte", new XCData(xmlDteFirmado))
            );

            string body = new XDeclaration("1.0", "UTF-8", null) + Environment.NewLine + requestXml;

            var client = _httpClientFactory.CreateClient();
            var content = new StringContent(body, Encoding.UTF8, "application/xml");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception("Error HTTP registrando documento Megaprint: " + responseText);

            var doc = XDocument.Parse(responseText);

            var tipo = doc.Descendants("tipo_respuesta").FirstOrDefault()?.Value;
            if (tipo != "0")
            {
                var codErr = doc.Descendants("cod_error").FirstOrDefault()?.Value ?? "sin código";
                var descErr = doc.Descendants("desc_error").FirstOrDefault()?.Value ?? "sin descripción";
                throw new Exception($"Megaprint rechazó el registro [{codErr}]: {descErr}");
            }

            var xmlCertificado = doc.Descendants("xml_dte").FirstOrDefault()?.Value ?? "";
            var uuid = doc.Descendants("uuid").FirstOrDefault()?.Value ?? "";

            if (string.IsNullOrWhiteSpace(uuid))
                throw new Exception("Megaprint no devolvió UUID de certificación: " + responseText);

            return (xmlCertificado, uuid);
        }
    }
}