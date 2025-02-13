using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace JuegoFramework.Helpers
{

    public class FCMService
    {
        private static readonly string firebasePrivateKey = Environment.GetEnvironmentVariable("FIREBASE_PRIVATE_KEY") ?? "";
        private static bool _isInitialized = false;

        static FCMService()
        {
            InitializeFirebase();
        }

        private static void InitializeFirebase()
        {
            if (_isInitialized) return;

            try
            {
                if (FirebaseApp.DefaultInstance == null)
                {
                    FirebaseApp.Create(new AppOptions()
                    {
                        Credential = GoogleCredential.FromFile(firebasePrivateKey)
                    });
                    _isInitialized = true;
                    Log.Information("Firebase initialized successfully.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error initializing Firebase: {ex.Message}");
            }
        }

        public static async Task<bool> SendPushNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string> data)
        {
            var message = new Message
            {
                Token = deviceToken,
                Notification = new Notification { Title = title, Body = body },
                Data = data
            };

            try
            {
                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                Log.Information($"Notification Sent! Response: {response}");
                return true;
            }
            catch (FirebaseMessagingException ex)
            {
                Log.Error($"FirebaseMessagingException: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending notification: {ex.Message}");
            }

            return false;
        }

        public static async Task<bool> SendPushNotificationToChannelAsync(string topic, string title, string body, Dictionary<string, string> data)
        {
            var message = new Message
            {
                Topic = topic,
                Notification = new Notification { Title = title, Body = body },
                Data = data
            };

            try
            {
                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                Log.Information($"Notification sent to topic '{topic}'. Response: {response}");
                return true;
            }
            catch (FirebaseMessagingException ex)
            {
                Log.Error($"FirebaseMessagingException for topic '{topic}': {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending notification to topic '{topic}': {ex.Message}");
            }

            return false;
        }

        public static async Task<bool> SendPushNotificationToMultipleTokensAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string> data)
        {
            if (deviceTokens == null || deviceTokens.Count == 0)
            {
                Log.Warning("No device tokens provided for batch notification.");
                return false;
            }

            var message = new MulticastMessage
            {
                Tokens = deviceTokens,
                Notification = new Notification { Title = title, Body = body },
                Data = data
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                Log.Information($"Batch Notification Sent! Success: {response.SuccessCount}, Failures: {response.FailureCount}");
                return response.FailureCount == 0;
            }
            catch (FirebaseMessagingException ex)
            {
                Log.Error($"FirebaseMessagingException: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending batch notification: {ex.Message}");
            }

            return false;
        }
    }
}
