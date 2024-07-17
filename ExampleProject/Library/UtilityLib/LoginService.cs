using API.Models;
using JuegoFramework.Helpers;
using API.Globals;

namespace API.Library
{
    public class LoginService : ILoginService
    {
        public override async Task<dynamic?> ValidateAuthData(JwtHelper.JwtObject authData)
        {
            var whereClause = new Dictionary<string, object?>()
            {
                { "user_id", authData.Data },
                { "access_token", authData.AccessToken },
                { "status", Constants.STATUS.ACTIVE }
            };
            return await SQLManager.FindOne<User>(whereClause) ?? null;
        }
    }
}
