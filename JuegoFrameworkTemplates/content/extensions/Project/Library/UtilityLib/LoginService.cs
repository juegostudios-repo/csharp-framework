using ProjectName.Models;
using JuegoFramework.Helpers;

namespace ProjectName.Library
{
    public class LoginService : ILoginService
    {
        public override async Task<dynamic?> ValidateAuthData(JwtHelper.JwtObject authData)
        {
#if DbTypeMySql
            var whereClause = new Dictionary<string, object?>()
            {
                { "user_id", authData.Data },
                { "access_token", authData.AccessToken },
                { "status", Constants.STATUS.ACTIVE }
            };
            return await SQLManager.FindOne<User>(whereClause) ?? null;
#else
            await Task.CompletedTask;
            return (string)authData.Data == "test" ? new User
            {
                UserName = (string)authData.Data,
                DeviceId = string.Empty,
            } : null;
#endif
        }
    }
}
