using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SUNS
{
    class NotificationSubscriptionTriggerEventArgs : EventArgs
    {
        public EndPoint Address { get; init; }
        public string Key { get; init; }

        public NotificationSubscriptionTriggerEventArgs(EndPoint address, string key)
        {
            Address = address;
            Key = key;
        }
    }

    class NotificationSubscriberInfo
    {
        public DateTime lastSubscribed = DateTime.Now;
        public DateTime lastNotified = DateTime.Now-new TimeSpan(24,0,0);
    }

    class NotificationCategory
    {
        public event EventHandler<NotificationSubscriptionTriggerEventArgs> notificationSubscriptionTriggered;

        private void OnNotificationSubscriptionTriggered(EndPoint address, string key)
        {
            notificationSubscriptionTriggered?.Invoke(this,new NotificationSubscriptionTriggerEventArgs( address, key));
        }

        public int NotificationLifetime { get; init; } = 60000;
        public int SubscriptionLifetime { get; init; } = 180000;
        public int ResendTimeout { get; init; } = 5000;
        public int Port { get; init; } = 5015;
        public int SubscribePort { get; init; } = 5016;
        Regex keyRegex { get; init; }
        Dictionary<string, DateTime> notifications = new Dictionary<string, DateTime>();
        Dictionary<string, Dictionary<EndPoint, NotificationSubscriberInfo>> subscribers = new Dictionary<string, Dictionary<EndPoint, NotificationSubscriberInfo>>();


        public NotificationCategory(string regexText, int notificationLifetime, int subscriptionLifetime,int resendTimeOut, int port, int subscribePort)
        {
            keyRegex = new Regex(regexText, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            NotificationLifetime = notificationLifetime;
            SubscriptionLifetime = subscriptionLifetime;
            Port = port;
            SubscribePort = subscribePort;
            ResendTimeout = resendTimeOut;
            InitSocket();
            InitSubscribeSocket();
            this.notificationSubscriptionTriggered += NotificationCategory_notificationSubscriptionTriggered;
        }

        public bool ActivateKey(string key)
        {
            if (key is null)
            {
                Console.WriteLine("Attempted to activate null key.");
            }
            if (keyRegex.Match(key).Success)
            {
                Console.WriteLine("Received matching key.");
                notifications[key] = DateTime.Now;
                if (subscribers.ContainsKey(key))
                {
                    foreach(KeyValuePair<EndPoint, NotificationSubscriberInfo> subscriber in subscribers[key])
                    {
                        if ((DateTime.Now-subscriber.Value.lastSubscribed).TotalMilliseconds < SubscriptionLifetime)
                        {
                            OnNotificationSubscriptionTriggered(subscriber.Key,key);
                            subscriber.Value.lastNotified = DateTime.Now;
                        }
                    }
                }
                return true;
            } else
            {
                Console.WriteLine("Received non-matching key.");
            }
            return false;
        }
        public bool CheckForNotificationResend()
        {
            foreach(var subscriptiosOfKeyWord in subscribers)
            {
                foreach(var subscriber in subscriptiosOfKeyWord.Value)
                {

                    if ((DateTime.Now - subscriber.Value.lastSubscribed).TotalMilliseconds >= SubscriptionLifetime) continue;
                    if ((DateTime.Now - subscriber.Value.lastNotified).TotalMilliseconds < ResendTimeout) continue;
                    if (!notifications.ContainsKey(subscriptiosOfKeyWord.Key) || (DateTime.Now - notifications[subscriptiosOfKeyWord.Key]).TotalMilliseconds >= NotificationLifetime) continue;

                    Console.WriteLine("Resending notification.");
                    OnNotificationSubscriptionTriggered(subscriber.Key, subscriptiosOfKeyWord.Key);
                    subscriber.Value.lastNotified = DateTime.Now;

                }
            }
            return false;
        }
        public bool AddSubscriber(EndPoint address, string key)
        {
            if (keyRegex.Match(key).Success)
            {
                if (!subscribers.ContainsKey(key))
                {
                    subscribers.Add(key,new Dictionary<EndPoint, NotificationSubscriberInfo>());
                }
                subscribers[key][address] = new NotificationSubscriberInfo() { lastSubscribed = DateTime.Now, lastNotified = DateTime.Now - new TimeSpan(0, 0, 0, 0, ResendTimeout * 2) };
                if (notifications.ContainsKey(key) && (DateTime.Now - notifications[key]).TotalMilliseconds < NotificationLifetime)
                {
                    subscribers[key][address].lastNotified = DateTime.Now;
                    OnNotificationSubscriptionTriggered(address, key);
                }
                return true;
            } else
            {
                Console.WriteLine("Received non-matching key from subscriber.");
            }
            return false;
        }

        ~NotificationCategory(){
            try
            {

                socket.Close();
                Console.WriteLine("Socket closed.");
            } catch(Exception ex)
            {
                Console.WriteLine("Unable to close socket.");
            }
            try
            {

                subscribeSocket.Close();
                Console.WriteLine("Subscriber socket closed.");
            } catch(Exception ex)
            {
                Console.WriteLine("Unable to close subscriber socket.");
            }
        }

        Socket socket = null;
        IPEndPoint endPoint = null;
        private void InitSocket()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking=false,EnableBroadcast=true};
            try
            {
                endPoint = new IPEndPoint(IPAddress.Any, Port);
                socket.Bind(endPoint);
                Console.WriteLine($"NOTE: Bound to port {endPoint.Port}.");
            }
            catch(SocketException e)
            {
                switch (e.SocketErrorCode) {
                    case SocketError.AddressAlreadyInUse:
                        Console.WriteLine($"ERROR: Unable to bind to port {Port}.");
                        break;
                }

            }

        }

        Socket subscribeSocket = null;
        IPEndPoint subscribeEndPoint = null;
        private void InitSubscribeSocket()
        {
            subscribeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking=false,EnableBroadcast=true};
            try
            {
                subscribeEndPoint = new IPEndPoint(IPAddress.Any, SubscribePort);
                subscribeSocket.Bind(subscribeEndPoint);
                Console.WriteLine($"NOTE: Bound subscribers to port {subscribeEndPoint.Port}.");
            }
            catch(SocketException e)
            {
                switch (e.SocketErrorCode) {
                    case SocketError.AddressAlreadyInUse:
                        Console.WriteLine($"ERROR: Unable to bind subscribers to port {SubscribePort}.");
                        break;
                }

            }

        }

        public unsafe void CheckForNewMessages()
        {
            bool tryForMore = true;
            while (tryForMore)
            {
                tryForMore = false;
                try
                {
                    if(!socket.Poll(0, SelectMode.SelectRead))
                    {
                        return;
                    }
                    byte[] messageData = new byte[5000];
                    EndPoint fromWho = new IPEndPoint(0, 0);
                    int receivedWhat = socket.ReceiveFrom(messageData,SocketFlags.None,ref fromWho);
                    tryForMore = true;
                    string dataString = null;
                    fixed(byte* data = messageData)
                    {
                        dataString = Encoding.Latin1.GetString(data, receivedWhat);
                    }
                    Console.WriteLine($"Received {receivedWhat} bytes from {fromWho.ToString()}");
                    ActivateKey(dataString);
                }
                catch (SocketException e)
                {
                    switch (e.SocketErrorCode) {
                        case SocketError.NotConnected:
                        case SocketError.Shutdown:
                            Console.WriteLine($"ERROR: Socket at port {Port} is not connected/shut down. Attempting to bind again.");
                            InitSocket();
                            tryForMore = true;
                            break;
                    }

                }
            }
        }

        public unsafe void CheckForNewSubscribeMessages()
        {
            bool tryForMore = true;
            while (tryForMore)
            {
                tryForMore = false;
                try
                {
                    if(!subscribeSocket.Poll(0, SelectMode.SelectRead))
                    {
                        return;
                    }
                    byte[] messageData = new byte[5000];
                    EndPoint fromWho = new IPEndPoint(0, 0);
                    int receivedWhat = subscribeSocket.ReceiveFrom(messageData,SocketFlags.None,ref fromWho);
                    tryForMore = true;
                    string dataString = null;
                    fixed(byte* data = messageData)
                    {
                        dataString = Encoding.Latin1.GetString(data, receivedWhat);
                    }
                    Console.WriteLine($"Received {receivedWhat} bytes from subscriber {fromWho.ToString()}");
                    AddSubscriber(fromWho,dataString);
                }
                catch (SocketException e)
                {
                    switch (e.SocketErrorCode) {
                        case SocketError.NotConnected:
                        case SocketError.Shutdown:
                            Console.WriteLine($"ERROR: Socket at port {SubscribePort} is not connected/shut down. Attempting to bind again.");
                            InitSubscribeSocket();
                            tryForMore = true;
                            break;
                    }

                }
            }
        }


        private unsafe void NotificationCategory_notificationSubscriptionTriggered(object sender, NotificationSubscriptionTriggerEventArgs e)
        {
            bool tryForMore = true;
            while (tryForMore)
            {
                try
                {
                    byte[] messageData = Encoding.Latin1.GetBytes(e.Key);
                    int sentWhat = subscribeSocket.SendTo(messageData,e.Address);
                    tryForMore = false;
                    if(sentWhat < messageData.Length)
                    {
                        Console.WriteLine($"Tried to send notification to {e.Address.ToString()} but only sent {sentWhat} out of {messageData.Length} bytes");
                    } else {
                        Console.WriteLine($"Sent notification to {e.Address.ToString()}");
                    }
                }
                catch (SocketException ex)
                {
                    switch (ex.SocketErrorCode)
                    {
                        default:
                            Console.WriteLine($"Cannot send notification: {ex.ToString()}");
                            tryForMore = false;
                            break;
                        case SocketError.NotConnected:
                        case SocketError.Shutdown:
                            Console.WriteLine($"ERROR: Socket at port {SubscribePort} is not connected/shut down. Attempting to bind again.");
                            InitSubscribeSocket();
                            tryForMore = true;
                            break;
                    }

                }
            }
        }


    }
}
