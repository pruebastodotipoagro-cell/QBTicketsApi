using System.Globalization;
using System.Xml.Linq;

namespace QBTicketsApi.Services
{
    public class FelCancellationXmlBuilderService
    {
        private const string FelNamespace =
            "http://www.sat.gob.gt/dte/fel/0.1.0";

        public string BuildCancellationXml(
            string authorizationNumber,
            string issuerNit,
            string receiverNit,
            DateTime originalIssueDate,
            string cancellationReason)
        {
            authorizationNumber =
                (authorizationNumber ?? "")
                    .Trim()
                    .ToUpperInvariant();

            issuerNit =
                LimpiarNit(
                    issuerNit
                );

            receiverNit =
                LimpiarNit(
                    receiverNit
                );

            cancellationReason =
                (cancellationReason ?? "")
                    .Trim();

            if (string.IsNullOrWhiteSpace(
                authorizationNumber))
            {
                throw new Exception(
                    "La factura no tiene número de autorización FEL."
                );
            }

            if (string.IsNullOrWhiteSpace(
                issuerNit))
            {
                throw new Exception(
                    "No está configurado el NIT del emisor."
                );
            }

            if (string.IsNullOrWhiteSpace(
                receiverNit))
            {
                receiverNit =
                    "CF";
            }

            if (string.IsNullOrWhiteSpace(
                cancellationReason))
            {
                throw new Exception(
                    "Debe indicar el motivo de la anulación."
                );
            }

            if (cancellationReason.Length < 5)
            {
                throw new Exception(
                    "El motivo de anulación debe contener " +
                    "al menos 5 caracteres."
                );
            }

            /*
             * La hora de anulación debe enviarse con
             * la zona horaria de Guatemala.
             */
            DateTimeOffset cancellationDate =
                ObtenerHoraGuatemala();

            DateTimeOffset originalDate =
                ConvertirAFechaGuatemala(
                    originalIssueDate
                );

            XNamespace dte =
                FelNamespace;

            var document =
                new XDocument(
                    new XDeclaration(
                        "1.0",
                        "UTF-8",
                        null
                    ),

                    new XElement(
                        dte + "GTAnulacionDocumento",

                        new XAttribute(
                            XNamespace.Xmlns + "dte",
                            FelNamespace
                        ),

                        new XAttribute(
                            "Version",
                            "0.1"
                        ),

                        new XElement(
                            dte + "SAT",

                            new XElement(
                                dte + "AnulacionDTE",

                                new XAttribute(
                                    "ID",
                                    "DatosCertificados"
                                ),

                                new XElement(
                                    dte + "DatosGenerales",

                                    new XAttribute(
                                        "ID",
                                        "DatosAnulacion"
                                    ),

                                    new XAttribute(
                                        "NumeroDocumentoAAnular",
                                        authorizationNumber
                                    ),

                                    new XAttribute(
                                        "NITEmisor",
                                        issuerNit
                                    ),

                                    new XAttribute(
                                        "IDReceptor",
                                        receiverNit
                                    ),

                                    new XAttribute(
                                        "FechaEmisionDocumentoAnular",
                                        FormatearFecha(
                                            originalDate
                                        )
                                    ),

                                    new XAttribute(
                                        "FechaHoraAnulacion",
                                        FormatearFecha(
                                            cancellationDate
                                        )
                                    ),

                                    new XAttribute(
                                        "MotivoAnulacion",
                                        cancellationReason
                                    )
                                )
                            )
                        )
                    )
                );

            return document.ToString(
                SaveOptions.DisableFormatting
            );
        }

        public DateTime ExtractOriginalIssueDate(
            string certifiedXml)
        {
            if (string.IsNullOrWhiteSpace(
                certifiedXml))
            {
                throw new Exception(
                    "El XML certificado está vacío."
                );
            }

            XDocument document;

            try
            {
                document =
                    XDocument.Parse(
                        certifiedXml
                    );
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "No se pudo leer el XML certificado.",
                    ex
                );
            }

            XElement datosGenerales =
                document
                    .Descendants()
                    .FirstOrDefault(
                        element =>
                            element.Name.LocalName ==
                            "DatosGenerales"
                    );

            if (datosGenerales == null)
            {
                throw new Exception(
                    "No se encontró DatosGenerales " +
                    "en el XML certificado."
                );
            }

            string value =
                datosGenerales
                    .Attribute(
                        "FechaHoraEmision"
                    )
                    ?.Value
                ?? "";

            if (string.IsNullOrWhiteSpace(
                value))
            {
                throw new Exception(
                    "El XML certificado no contiene " +
                    "FechaHoraEmision."
                );
            }

            DateTimeOffset parsed;

            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsed))
            {
                return parsed.UtcDateTime;
            }

            DateTime parsedDate;

            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsedDate))
            {
                return DateTime.SpecifyKind(
                    parsedDate,
                    DateTimeKind.Unspecified
                );
            }

            throw new Exception(
                "La FechaHoraEmision del XML " +
                "certificado no tiene un formato válido."
            );
        }

        private static string LimpiarNit(
            string nit)
        {
            return (nit ?? "")
                .Trim()
                .Replace("-", "")
                .Replace(" ", "")
                .ToUpperInvariant();
        }

        private static string FormatearFecha(
            DateTimeOffset date)
        {
            return date.ToString(
                "yyyy-MM-ddTHH:mm:sszzz",
                CultureInfo.InvariantCulture
            );
        }

        private static DateTimeOffset
            ObtenerHoraGuatemala()
        {
            TimeZoneInfo guatemala =
                ObtenerZonaHorariaGuatemala();

            return TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                guatemala
            );
        }

        private static DateTimeOffset
            ConvertirAFechaGuatemala(
                DateTime date)
        {
            TimeZoneInfo guatemala =
                ObtenerZonaHorariaGuatemala();

            if (date.Kind ==
                DateTimeKind.Utc)
            {
                return TimeZoneInfo.ConvertTime(
                    new DateTimeOffset(
                        date
                    ),
                    guatemala
                );
            }

            DateTime unspecified =
                DateTime.SpecifyKind(
                    date,
                    DateTimeKind.Unspecified
                );

            TimeSpan offset =
                guatemala.GetUtcOffset(
                    unspecified
                );

            return new DateTimeOffset(
                unspecified,
                offset
            );
        }

        private static TimeZoneInfo
            ObtenerZonaHorariaGuatemala()
        {
            try
            {
                /*
                 * Windows.
                 */
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Central America Standard Time"
                );
            }
            catch
            {
                /*
                 * Linux/Railway.
                 */
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "America/Guatemala"
                );
            }
        }
    }
}