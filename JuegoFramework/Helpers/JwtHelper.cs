using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace JuegoFramework.Helpers
{
    public class JwtHelper
    {
        public static string GenerateJwtToken(dynamic accountId, string? claimKey = null)
        {
            var configuration = Global.Configuration!;

            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtSecret = configuration["AUTH:JWT_SECRET"] ?? throw new ArgumentNullException("AUTH:JWT_SECRET", "The JWT Secret is not set in the configuration.");
            var key = Encoding.ASCII.GetBytes(jwtSecret);

            var expiryDaysConfig = configuration["AUTH:JWT_EXPIRY_DAYS"];
            DateTime expiryDate = DateTime.MaxValue;
            if (!string.IsNullOrEmpty(expiryDaysConfig) && int.TryParse(expiryDaysConfig, out int expiryDays))
            {
                expiryDate = DateTime.UtcNow.AddDays(expiryDays);
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(
                [
                    new Claim(claimKey ?? configuration["AUTH:JWT_ID_KEY"] ?? "user_id", accountId.ToString()),
                ]),
                Expires = expiryDate,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public static JwtObject? ValidateJwtToken(string token)
        {
            var configuration = Global.Configuration!;

            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtSecret = configuration["AUTH:JWT_SECRET"] ?? throw new ArgumentNullException("AUTH:JWT_SECRET", "The JWT Secret is not set in the configuration.");
            var key = Encoding.ASCII.GetBytes(jwtSecret);
            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var userData = jwtToken.Claims.First(x => x.Type == configuration["AUTH:JWT_ID_KEY"]).Value;

                // return account id from JWT token if validation successful
                return new JwtObject() { Data = userData, AccessToken = token };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public class JwtObject
        {
            public required object Data { get; set; }
            public required object AccessToken { get; set; }
        }

        public static string DecodeToken(string token, string? key = null)
        {
            var configuration = Global.Configuration!;

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            key ??= configuration["AUTH:JWT_ID_KEY"];

            var value = jwtToken.Claims.First(claim => claim.Type == key).Value;

            return value;
        }
    }
}
