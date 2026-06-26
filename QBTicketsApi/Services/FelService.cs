namespace QBTicketsApi.Services
{
    public class FelResult
    {
        public string Serie { get; set; } = "";
        public string DteNumber { get; set; } = "";
        public string AuthorizationNumber { get; set; } = "";
        public DateTime CertificationDate { get; set; }
        public string Qr { get; set; } = "";
    }

    public class FelService
    {
        public FelResult CertifyMock(string quickBooksId, string invoiceNumber)
        {
            return new FelResult
            {
                Serie = "TEST",
                DteNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? quickBooksId : invoiceNumber,
                AuthorizationNumber = Guid.NewGuid().ToString().ToUpper(),
                CertificationDate = DateTime.UtcNow,
                Qr = ""
            };
        }
    }
}