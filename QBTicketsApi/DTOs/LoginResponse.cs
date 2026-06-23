namespace QBTicketsApi.DTOs
{
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Token { get; set; }
        public object User { get; set; }
        public string Error { get; set; }
    }
}