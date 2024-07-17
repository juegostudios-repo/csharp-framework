using API.Models;
using JuegoFramework.Helpers;
using API.Globals;

namespace API.Library
{
    public class LoginService : ILoginService
    {
        public override async Task<dynamic?> ValidateAuthData(JwtHelper.JwtObject authData)
        {
            await Task.CompletedTask;
            return (string)authData.Data == "test" ? new User {
                UserName = (string)authData.Data,
            } : null;
        }
    }
}
