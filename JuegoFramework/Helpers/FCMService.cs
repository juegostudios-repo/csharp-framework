using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;


public class FCMService
{
    private static readonly string firebasePrivateKey = Environment.GetEnvironmentVariable("FIREBASE_PRIVATE_KEY") ?? "";
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

    public async Task<bool> SendPushNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string> data)
    {
        var message = new Message()
        {
            Token = deviceToken,
            Notification = new Notification
            {
                Title = title,
                Body = body
            },
            Data = data
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

    public async Task<bool> SendPushNotificationToChannelAsync(string topic, string title, string body, Dictionary<string, string> data)
    {
        var message = new Message()
        {
            Topic = topic,
            Notification = new Notification
            {
                Title = title,
                Body = body
            },
            Data = data
        };

        try
        {
            string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            Console.WriteLine($"Notification sent to topic '{topic}'. Response: {response}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending notification to topic '{topic}': {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendPushNotificationToMultipleTokensAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string> data)
    {
        if (deviceTokens == null || deviceTokens.Count == 0)
        {
            Console.WriteLine("Error: No device tokens provided for batch notification.");
            return false;
        }

        var message = new MulticastMessage()
        {
            Tokens = deviceTokens, // List of tokens
            Notification = new Notification
            {
                Title = title,
                Body = body
            },
            Data = data
        };

        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            Console.WriteLine($"Batch Notification Sent! Success Count: {response.SuccessCount}, Failure Count: {response.FailureCount}");
            return response.FailureCount == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending batch notification: {ex.Message}");
            return false;
        }
    }


}
