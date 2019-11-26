using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using WSdto.Json;

namespace WSCli.Configuration
{
    public static class ConfigWatcher 
    {
        private static JObject configurationRoot;
        private static readonly JsonMergeSettings mergeSettings;
        private static bool inited = false;

        static ConfigWatcher()
        {
            mergeSettings = new JsonMergeSettings
            {
                MergeNullValueHandling = MergeNullValueHandling.Ignore,
                MergeArrayHandling = MergeArrayHandling.Union
            };
        }

        public static JObject Init(string configDir)
        {
            if (inited)
                return (JObject)configurationRoot.DeepClone();
            configurationRoot = MakeConfig();
            inited = true;
            return (JObject)configurationRoot.DeepClone();
        }

        private static void ReadConfigurationFile(string fileName, JObject config)
        {
            try
            {
                var configData = File.ReadAllText(fileName);
                var jData = JToken.Parse(configData);
                config.Merge(jData, mergeSettings);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошика чтения файла {fileName} ", ex);
            }
        }

        private static JObject MakeConfig()
        {
            var config = new JObject();

            foreach (var fn in Directory.EnumerateFiles(CurrentConfiguration.ConfigPath, "*.json"))
            {
                ReadConfigurationFile(fn, config);
            }

            return config;
        }

        public static JObject GetSection(string sectionName)
        {
            return (JObject)configurationRoot.GetValueIC(sectionName)?.DeepClone() ;
        }

        public static JObject GetConfig()
        {
            return (JObject)configurationRoot.DeepClone();
        }
    }
}
