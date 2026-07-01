using Microsoft.VisualBasic.FileIO;
using System.IO;

namespace QBTicketsApi.Services
{
    public class CustomerLookupService
    {
        private readonly Dictionary<string, string> _customers = new();

        public CustomerLookupService(IWebHostEnvironment env)
        {
            var path1 = Path.Combine(env.ContentRootPath, "Data", "clientes.csv");
            var path2 = Path.Combine(AppContext.BaseDirectory, "Data", "clientes.csv");

            Console.WriteLine("ContentRootPath: " + env.ContentRootPath);
            Console.WriteLine("BaseDirectory: " + AppContext.BaseDirectory);
            Console.WriteLine("Path1 exists: " + File.Exists(path1));
            Console.WriteLine("Path2 exists: " + File.Exists(path2));

            var files = Directory.GetFiles(AppContext.BaseDirectory, "*", System.IO.SearchOption.AllDirectories);

            foreach (var file in files)
            {
                Console.WriteLine("FILE FOUND: " + file);
            }

            var path = path1;

            if (!File.Exists(path))
                path = path2;

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