using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace BobsBookstoreClassic.Data
{
    public sealed class BookstoreConfiguration
    {
        private static readonly Lazy<BookstoreConfiguration> Lazy =
            new Lazy<BookstoreConfiguration>(() => new BookstoreConfiguration());

        private static BookstoreConfiguration Instance => Lazy.Value;

        private readonly Dictionary<string, string> _appSettings = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _connectionStrings = new Dictionary<string, string>();

        private BookstoreConfiguration() { }

        /// <summary>
        /// Seeds the static configuration store from an ASP.NET Core IConfiguration instance.
        /// Call this once during application startup before any GetSetting() calls.
        /// </summary>
        public static void Initialize(IConfiguration configuration)
        {
            // Load flat key-value pairs (e.g. "Services/Authentication" = "local")
            foreach (var kvp in configuration.AsEnumerable())
            {
                if (kvp.Value != null)
                {
                    Instance._appSettings[kvp.Key] = kvp.Value;
                }
            }

            // Load connection strings section
            var connectionStrings = configuration.GetSection("ConnectionStrings");
            foreach (var cs in connectionStrings.GetChildren())
            {
                if (cs.Value != null)
                {
                    Instance._connectionStrings[cs.Key] = cs.Value;
                }
            }

            // Also check environment variable overrides
            foreach (var key in new List<string>(Instance._appSettings.Keys))
            {
                var envValue = Environment.GetEnvironmentVariable(key);
                if (envValue != null)
                {
                    Instance._appSettings[key] = envValue;
                }
            }
        }

        public static void AddSetting(string key, string value)
        {
            Instance._appSettings[key] = value;
        }

        public static string GetSetting(string key)
        {
            return Instance._appSettings.TryGetValue(key, out var value) ? value : null;
        }

        public static T GetSetting<T>(string key)
        {
            var value = Instance._appSettings[key];
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public static void AddConnectionString(string key, string value)
        {
            Instance._connectionStrings[key] = value;
        }

        public static string GetConnectionString(string key)
        {
            return Instance._connectionStrings.TryGetValue(key, out var value) ? value : null;
        }
    }
}
