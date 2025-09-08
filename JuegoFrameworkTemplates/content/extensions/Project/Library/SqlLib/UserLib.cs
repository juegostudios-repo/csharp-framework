using JuegoFramework.Helpers;
using ProjectName.Models;
using static ProjectName.Globals.Constants;

namespace ProjectName.Library;

public class UserLib : MySqlLib<User>
{
    public static async Task ConnectSocket(string token, string connectionId)
    {
        try
        {
            dynamic? userId = JwtHelper.ValidateJwtToken(token)?.Data;

            if (userId == null)
            {
                return;
            }

            var isTokenPresent = await FindOne(new
            {
                user_id = userId,
                access_token = token,
                status = STATUS.ACTIVE
            }) ?? throw new ArgumentNullException(nameof(token), "The Token is Invalid!");

            await Update(
                new { user_id = userId },
                new { connection_id = connectionId }
            );
        }
        catch (Exception e)
        {
            Log.Error(e, "An Exception Occurred");
            throw;
        }
    }

    public static async Task DisconnectSocket(string connectionId)
    {
        await Update(
            new { connection_id = connectionId },
            new { connection_id = string.Empty }
        );
    }

    public static async Task<List<User>> GetWebsocketConnectedUsersList()
    {
        return await SQLManager.Query<User>("SELECT * FROM user WHERE connection_id !='' AND connection_id IS NOT NULL");
    }
}
