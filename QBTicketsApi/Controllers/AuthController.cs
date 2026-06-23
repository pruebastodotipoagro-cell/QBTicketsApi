using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.Helpers;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AuthController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var user = _db.Users.FirstOrDefault(u => u.Username == request.Username);

            if (user == null || user.Password != request.Password)
            {
                return Unauthorized(new
                {
                    success = false,
                    error = "Usuario o contraseña incorrectos."
                });
            }

            return Ok(new
            {
                success = true,
                token = "TEMP_TOKEN_CSHARP",
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    role = user.Role,
                    name = user.Name
                }
            });
        }
    }
}