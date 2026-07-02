using Microsoft.VisualBasic.FileIO;
using System.Reflection;

namespace QBTicketsApi.Services
{
    public class CustomerLookupService
    {
        private readonly Dictionary<string, string> _customers = new();

        public CustomerLookupService()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith("clientes.csv"));

            if (resourceName == null)
            {
                Console.WriteLine("clientes.csv NO encontrado como recurso incrustado.");
                return;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                Console.WriteLine("No se pudo abrir clientes.csv.");
                return;
            }

            using var reader = new StreamReader(stream);
            using var parser = new TextFieldParser(reader);

            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;

            bool isHeader = true;

            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();

                if (fields == null || fields.Length < 2)
                    continue;

                if (isHeader)
                {
                    isHeader = false;
                    continue;
                }

                var name = Normalize(fields[0]);
                var nit = fields[1]?.Trim() ?? "CF";

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.IsNullOrWhiteSpace(nit))
                    nit = "CF";

                _customers[name] = nit;
            }

            Console.WriteLine($"Clientes cargados desde CSV incrustado: {_customers.Count}");
        }

        public string GetNit(string customerName)
        {
            var key = Normalize(customerName);

            if (_customers.TryGetValue(key, out var nit))
                return nit;

            return "CF";
        }
        public List<string> GetCustomerNames()
        {
            return _customers.Keys.OrderBy(x => x).ToList();
        }

        public int Count()
        {
            return _customers.Count;
        }

        public string DebugSample()
        {
            if (_customers.Count == 0)
                return "VACIO";

            return _customers.Keys.First();
        }

        private static string Normalize(string value)
        {
            return (value ?? "")
                .Trim()
                .ToUpperInvariant();
        }
    }
}