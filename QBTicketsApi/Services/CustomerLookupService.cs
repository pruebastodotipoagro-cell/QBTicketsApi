using System.Globalization;

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

            var lines = File.ReadAllLines(path);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');

                if (parts.Length < 2)
                    continue;

                var name = Normalize(parts[0]);
                var nit = parts[1].Trim();

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