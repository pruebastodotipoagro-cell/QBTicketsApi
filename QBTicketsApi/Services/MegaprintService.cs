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

        public async Task<(
    string xmlAnulacionCertificado,
    string uuidAnulacion
)> AnularDocumentoAsync(
    string xmlAnulacion,
    string token)
        {
            if (string.IsNullOrWhiteSpace(xmlAnulacion))
            {
                throw new Exception(
                    "El XML de anulación está vacío."
                );
            }

            string url =
                _config["Megaprint:AnulacionUrl"]
                ?? "";

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new Exception(
                    "No está configurada la URL " +
                    "Megaprint:AnulacionUrl."
                );
            }

            string requestId =
                Guid.NewGuid()
                    .ToString()
                    .ToUpperInvariant();

            var requestXml =
                new XElement(
                    "AnulaDocumentoXMLRequest",

                    new XAttribute(
                        "id",
                        requestId
                    ),

                    new XElement(
                        "xml_dte",
                        new XCData(
                            xmlAnulacion
                        )
                    )
                );

            string body =
                new XDeclaration(
                    "1.0",
                    "UTF-8",
                    null
                ) +
                Environment.NewLine +
                requestXml;

            var client =
                _httpClientFactory
                    .CreateClient();

            var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url
                );

            request.Content =
                new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/xml"
                );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token
                );

            HttpResponseMessage response =
                await client.SendAsync(
                    request
                );

            string responseText =
                await response.Content
                    .ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    "Error HTTP anulando documento " +
                    "en Megaprint: " +
                    responseText
                );
            }

            XDocument doc;

            try
            {
                doc =
                    XDocument.Parse(
                        responseText
                    );
            }
            catch
            {
                throw new Exception(
                    "Megaprint devolvió una respuesta " +
                    "inválida al anular:\n" +
                    responseText
                );
            }

            string tipoRespuesta =
                doc.Descendants()
                    .FirstOrDefault(
                        element =>
                            element.Name.LocalName
                                .Equals(
                                    "tipo_respuesta",
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                    )
                    ?.Value
                ?? "1";

            if (tipoRespuesta != "0")
            {
                string codigo =
                    doc.Descendants()
                        .FirstOrDefault(
                            element =>
                                element.Name.LocalName
                                    .Equals(
                                        "cod_error",
                                        StringComparison
                                            .OrdinalIgnoreCase
                                    )
                        )
                        ?.Value
                    ?? "sin código";

                string descripcion =
                    doc.Descendants()
                        .FirstOrDefault(
                            element =>
                                element.Name.LocalName
                                    .Equals(
                                        "desc_error",
                                        StringComparison
                                            .OrdinalIgnoreCase
                                    )
                        )
                        ?.Value
                    ?? "Megaprint rechazó la anulación.";

                throw new Exception(
                    "Megaprint rechazó la anulación [" +
                    codigo +
                    "]: " +
                    descripcion
                );
            }

            string xmlCertificado =
                doc.Descendants()
                    .FirstOrDefault(
                        element =>
                            element.Name.LocalName
                                .Equals(
                                    "xml_dte",
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                    )
                    ?.Value
                ?? "";

            string uuidAnulacion =
                doc.Descendants()
                    .FirstOrDefault(
                        element =>
                            element.Name.LocalName
                                .Equals(
                                    "uuid",
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                    )
                    ?.Value
                ?? "";

            if (string.IsNullOrWhiteSpace(
                uuidAnulacion))
            {
                throw new Exception(
                    "Megaprint aceptó la solicitud, " +
                    "pero no devolvió el UUID de anulación.\n" +
                    responseText
                );
            }

            return (
                xmlCertificado,
                uuidAnulacion
            );
        }

        public async Task<string> RetornarXmlAsync(
    string uuid,
    string token)
        {
            uuid =
                (uuid ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(uuid))
            {
                throw new Exception(
                    "Debe indicar el UUID del documento."
                );
            }

            string url =
                _config["Megaprint:RetornarXmlUrl"]
                ?? "";

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new Exception(
                    "No está configurada la URL " +
                    "Megaprint:RetornarXmlUrl."
                );
            }

            string requestId =
                Guid.NewGuid()
                    .ToString()
                    .ToUpperInvariant();

            var requestXml =
                new XElement(
                    "RetornaXMLRequest",

                    new XAttribute(
                        "id",
                        requestId
                    ),

                    new XElement(
                        "uuid",
                        uuid
                    )
                );

            string body =
                new XDeclaration(
                    "1.0",
                    "UTF-8",
                    null
                ) +
                Environment.NewLine +
                requestXml;

            var client =
                _httpClientFactory
                    .CreateClient();

            var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url
                );

            request.Content =
                new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/xml"
                );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token
                );

            HttpResponseMessage response =
                await client.SendAsync(
                    request
                );

            string responseText =
                await response.Content
                    .ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    "Error HTTP obteniendo XML " +
                    "de Megaprint: " +
                    responseText
                );
            }

            XDocument doc;

            try
            {
                doc =
                    XDocument.Parse(
                        responseText
                    );
            }
            catch
            {
                throw new Exception(
                    "Megaprint devolvió una respuesta " +
                    "inválida al consultar el XML:\n" +
                    responseText
                );
            }

            string tipoRespuesta =
                doc.Descendants()
                    .FirstOrDefault(
                        element =>
                            element.Name.LocalName
                                .Equals(
                                    "tipo_respuesta",
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                    )
                    ?.Value
                ?? "1";

            if (tipoRespuesta != "0")
            {
                string codigo =
                    doc.Descendants()
                        .FirstOrDefault(
                            element =>
                                element.Name.LocalName
                                    .Equals(
                                        "cod_error",
                                        StringComparison
                                            .OrdinalIgnoreCase
                                    )
                        )
                        ?.Value
                    ?? "sin código";

                string descripcion =
                    doc.Descendants()
                        .FirstOrDefault(
                            element =>
                                element.Name.LocalName
                                    .Equals(
                                        "desc_error",
                                        StringComparison
                                            .OrdinalIgnoreCase
                                    )
                        )
                        ?.Value
                    ?? "Megaprint rechazó la consulta del XML.";

                throw new Exception(
                    "Megaprint rechazó la consulta [" +
                    codigo +
                    "]: " +
                    descripcion
                );
            }

            string xmlCertificado =
                doc.Descendants()
                    .FirstOrDefault(
                        element =>
                            element.Name.LocalName
                                .Equals(
                                    "xml_dte",
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                    )
                    ?.Value
                ?? "";

            if (string.IsNullOrWhiteSpace(
                xmlCertificado))
            {
                throw new Exception(
                    "Megaprint no devolvió el XML certificado."
                );
            }

            return xmlCertificado;
        }

        public async Task<string> RetornarNombreClienteAsync(string nit)
        {
            if (string.IsNullOrWhiteSpace(nit))
                throw new Exception("Debe ingresar un NIT.");

            nit = nit.Trim().Replace("-", "");

            if (nit.Equals("CF", StringComparison.OrdinalIgnoreCase))
                return "Consumidor Final";

            string token = await SolicitarTokenAsync();

            string url = _config["Megaprint:DatosClienteUrl"] ?? "";

            if (string.IsNullOrWhiteSpace(url))
                throw new Exception(
                    "No está configurada la URL Megaprint:DatosClienteUrl."
                );

            var requestXml = new XElement(
                "RetornaDatosClienteRequest",
                new XElement("nit", nit)
            );

            string body =
                new XDeclaration("1.0", "UTF-8", null) +
                Environment.NewLine +
                requestXml;

            var client = _httpClientFactory.CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/xml"
                )
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    "Error HTTP consultando NIT en Megaprint: " +
                    responseText
                );
            }

            XDocument doc;

            try
            {
                doc = XDocument.Parse(responseText);
            }
            catch
            {
                throw new Exception(
                    "Megaprint devolvió una respuesta inválida: " +
                    responseText
                );
            }

            string tipoRespuesta =
                doc.Descendants()
                   .FirstOrDefault(x => x.Name.LocalName == "tipo_respuesta")
                   ?.Value ?? "1";

            if (tipoRespuesta != "0")
            {
                string codigo =
                    doc.Descendants()
                       .FirstOrDefault(x => x.Name.LocalName == "cod_error")
                       ?.Value ?? "sin código";

                string descripcion =
                    doc.Descendants()
                       .FirstOrDefault(x => x.Name.LocalName == "desc_error")
                       ?.Value ?? "No se pudo consultar el NIT.";

                throw new Exception(
                    $"Megaprint rechazó la consulta [{codigo}]: {descripcion}"
                );
            }

            string nombre =
                doc.Descendants()
                   .FirstOrDefault(x => x.Name.LocalName == "nombre")
                   ?.Value ?? "";

            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new Exception(
                    "Megaprint no devolvió el nombre asociado al NIT."
                );
            }

            return nombre.Trim();
        }
    }
}