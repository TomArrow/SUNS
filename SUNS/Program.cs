using Salaros.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SUNS
{
    

    class Program
    {
        static Dictionary<string, NotificationCategory> notificationCategories = new Dictionary<string, NotificationCategory>();
        static int Main(string[] args)
        {
            if (!File.Exists("sunsConfig.ini"))
            {
                Console.WriteLine("sunsConfig.ini not found. Quitting.");
                return 1;
            }
            ConfigParser cfgParse = new ConfigParser("sunsConfig.ini");
            foreach(ConfigSection section in cfgParse.Sections)
            {
                string key = section["keyRegex"];
                if (string.IsNullOrWhiteSpace(key))
                {
                    string pureKey = section["key"];
                    key = string.IsNullOrWhiteSpace(pureKey) ? null: Regex.Escape(pureKey);
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    Console.WriteLine($"Section [{section.SectionName}] is invalid, no key or keyRegex provided.");
                } else
                {
                    int notificationLifetime = 60000;
                    int subscriptionLifetime = 180000;
                    int resendTimeOut = 5000;
                    int port = 5015;
                    int subscriberPort = 5016;
                    if (!int.TryParse(section["notificationLifetime"], out notificationLifetime))
                    {
                        notificationLifetime = 60000;
                    }
                    if (!int.TryParse(section["subscriptionLifetime"], out subscriptionLifetime))
                    {
                        subscriptionLifetime = 180000;
                    }
                    if (!int.TryParse(section["port"], out port))
                    {
                        port = 5015;
                    }
                    if (!int.TryParse(section["subscriberPort"], out subscriberPort))
                    {
                        subscriberPort = 5016;
                    }
                    if (!int.TryParse(section["resendTimeOut"], out resendTimeOut))
                    {
                        resendTimeOut = 5000;
                    }
                    Console.WriteLine($"Category {section.SectionName} activated with key {key}.");
                    notificationCategories[section.SectionName] = new NotificationCategory(key, notificationLifetime, subscriptionLifetime, resendTimeOut, port, subscriberPort);
                }
            }

            if(notificationCategories.Count > 0)
            {
                Console.WriteLine($"Running with {notificationCategories.Count} active categories.");
                while (true)
                {
                    System.Threading.Thread.Sleep(100);
                    foreach(var notificationCategory in notificationCategories)
                    {
                        notificationCategory.Value.CheckForNewMessages();
                        notificationCategory.Value.CheckForNewSubscribeMessages();
                        notificationCategory.Value.CheckForNotificationResend();
                    }
                }
            } else
            {
                Console.WriteLine("No valid notification categories were processed. Existing.");
                return 1;
            }

            return 0;
        }
    }
}
