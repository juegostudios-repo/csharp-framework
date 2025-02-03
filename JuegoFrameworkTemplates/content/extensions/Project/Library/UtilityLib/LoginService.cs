using ProjectName.Models;
using JuegoFramework.Helpers;

namespace ProjectName.Library
{
    public class LoginService : ILoginService
    {
        public override async Task<dynamic?> ValidateAuthData(JwtHelper.JwtObject authData)
        {
            await Task.CompletedTask;
            return (string)authData.Data == "test" ? new User
            {
                UserName = (string)authData.Data,
            } : null;
        }
    }
}
