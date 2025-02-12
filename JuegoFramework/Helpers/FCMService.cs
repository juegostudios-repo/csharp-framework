using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;


public class FCMService
{
    private static readonly string  firebasePrivateKey = Environment.GetEnvironmentVariable("FIREBASE_PRIVATE_KEY") ?? "";
    private static bool _isInitialized = false;

    public FCMService()
    {
        InitializeFirebase();
    }

    private void InitializeFirebase()
    {
        if (!_isInitialized)
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions()
                {
                    Credential = GoogleCredential.FromFile(firebasePrivateKey)
                });
                _isInitialized = true;
            }
        }
    }

    public async Task<bool> SendPushNotificationAsync(string deviceToken, string title, string body)
    {
        var message = new Message()
        {
            Token = deviceToken,
            Notification = new Notification
            {
                Title = title,
                Body = body
            }
        };

        try
        {
            string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            Console.WriteLine("Notification Sent! Response: " + response);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending notification: {ex.Message}");
            return false;
        }
    }


}
