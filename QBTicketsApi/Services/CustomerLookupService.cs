using Microsoft.VisualBasic.FileIO;

namespace QBTicketsApi.Services
{
    public class CustomerLookupService
    {
        private readonly Dictionary<string, string> _customers = new();

        public CustomerLookupService(IWebHostEnvironment env)
        {
            var path = Path.Combine(env.ContentRootPath, "Data", "clientes.csv");

            if (!File.Exists(path))
                return;

            using var parser = new TextFieldParser(path);
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
        }

        public string GetNit(string customerName)
        {
            var key = Normalize(customerName);

            if (_customers.TryGetValue(key, out var nit))
                return nit;

            return "CF";
        }

        private static string Normalize(string value)
        {
            return (value ?? "")
                .Trim()
                .ToUpperInvariant();
        }
    }
}