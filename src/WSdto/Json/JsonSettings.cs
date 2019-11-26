using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace WSdto.Json
{
    public static class JsonSettings
    {
        public static readonly JsonSerializerSettings settings;

        static JsonSettings()
        {
            settings = new JsonSerializerSettings
            {

                ContractResolver = new DefaultContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                TypeNameHandling = TypeNameHandling.None
            };
            settings.Converters.Add(new StringEnumConverter());

        }
    }
}
