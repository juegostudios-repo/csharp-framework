namespace JuegoFramework.Helpers
{
    public abstract class ILoginService
    {
        public abstract Task<dynamic?> ValidateAuthData(JwtHelper.JwtObject authData);
    }
}
