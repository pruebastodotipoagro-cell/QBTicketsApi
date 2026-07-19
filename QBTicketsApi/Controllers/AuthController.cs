using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _configuration;

        public AuthController(
            AppDbContext db,
            IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public IActionResult Login(
    [FromBody] LoginRequest request)
        {
            string username =
                request.Username?.Trim() ?? "";

            string password =
                request.Password?.Trim() ?? "";

            var user =
                _db.Users.FirstOrDefault(
                    u => u.Username.ToLower() ==
                         username.ToLower()
                );

            if (user == null)
            {
                return Unauthorized(new
                {
                    success = false,
                    error = "El usuario no existe."
                });
            }

            if (user.Password.Trim() != password)
            {
                return Unauthorized(new
                {
                    success = false,
                    error = "La contraseña es incorrecta."
                });
            }

            string token =
                GenerateJwtToken(user);

            return Ok(new
            {
                success = true,
                token,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    role = user.Role,
                    name = user.Name,
                    cashierName = user.CashierName,
                    canViewAllSales = user.CanViewAllSales
                }
            });
        }

        private string GenerateJwtToken(
            QBTicketsApi.Models.User user)
        {
            string jwtKey =
                _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException(
                    "No está configurada Jwt:Key."
                );

            string jwtIssuer =
                _configuration["Jwt:Issuer"]
                ?? "QBTicketsApi";

            string jwtAudience =
                _configuration["Jwt:Audience"]
                ?? "QBTicketsNative";

            var claims =
                new List<Claim>
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        user.Id.ToString()
                    ),

                    new Claim(
                        ClaimTypes.Name,
                        user.Username
                    ),

                    new Claim(
                        ClaimTypes.Role,
                        user.Role ?? ""
                    ),

                    new Claim(
                        "displayName",
                        user.Name ?? ""
                    ),

                    new Claim(
                        "cashierName",
                        user.CashierName ?? ""
                    ),

                    new Claim(
                        "canViewAllSales",
                        user.CanViewAllSales
                            ? "true"
                            : "false"
                    )
                };

            var signingKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey)
                );

            var credentials =
                new SigningCredentials(
                    signingKey,
                    SecurityAlgorithms.HmacSha256
                );

            var jwt =
                new JwtSecurityToken(
                    issuer: jwtIssuer,
                    audience: jwtAudience,
                    claims: claims,
                    notBefore: DateTime.UtcNow,
                    expires: DateTime.UtcNow.AddHours(12),
                    signingCredentials: credentials
                );

            return new JwtSecurityTokenHandler()
                .WriteToken(jwt);
        }
    }
}