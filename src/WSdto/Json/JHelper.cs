using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WSdto.Json
{
    public static class JHelper
    {
        // base
        public static T ConvertValue<T>(this JToken jToken)
        {
            return (T)jToken.ConvertValue(typeof(T));
        }

        public static object ConvertValue(this JToken jToken, Type type)
        {


            var underlyingType = Nullable.GetUnderlyingType(type);

            if (jToken == null)
            {
                if (underlyingType != null || type.IsClass)
                    return null;
                else
                    throw new InvalidCastException("null value to not Nullable type");
            }

            if (jToken is JProperty jProperty)
            {
                return jProperty.ConvertValue(type);
            }

            if (type == typeof(object))
            {
                return jToken;
            }



            if (jToken is JValue jVal)
            {
                if (jVal.GetType() == type)
                {
                    return jVal.Value;
                }
                else
                {





                    /*                    if (type.IsEnum)
                                        {
                                            return jVal.ToObject(type);
                                        }

                                        if (jVal.Value is IConvertible)
                                        {

                                            return Convert.ChangeType(jVal.Value, underlyingType ?? type);
                                        }*/
                    return jVal.ToObject(type);
                    // throw new InvalidCastException($"Type: \"{jVal.GetType().Name}\" can't convert to type \"{type.Name}\".");
                }
            }

            if (type == typeof(string))
            {
                return jToken.ToJson();
            }

            if (jToken is JObject jObj)
            {
                if (!type.IsClass)
                    throw new InvalidCastException($"Object can't convert to type \"{type.Name}\".");

                return jObj.ToObject(type);

            }


            if (jToken is JArray jArr)
            {
                if (!type.IsArray)
                    throw new InvalidCastException($"Array can't convert to type \"{type.Name}\".");

                return jArr.ToObject(type);
            }




            throw new InvalidCastException($"Unknown type can't convert to type \"{type.Name}\".");

        }

        public static JToken GetValueIC(this JToken jToken, string propertyName)
        {
            if (jToken is JObject jObj)
                return jObj.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
            return jToken?.SelectToken(propertyName);
        }


        public static string ToJson(this object jToken)
        {
            return JsonConvert.SerializeObject(jToken, Formatting.None, JsonSettings.settings);
        }

        public static string ToIndentedJson(this object jToken)
        {
            return JsonConvert.SerializeObject(jToken,Formatting.Indented ,JsonSettings.settings);
        }

        // string

        public static T ConvertValue<T>(this string str)
        {
            return (T)str.ConvertValue(typeof(T));
        }

        public static object ConvertValue(this string str, Type type)
        {
            var jToken = JToken.Parse(str);
            return jToken.ConvertValue(type);
        }

        // JObject

        public static void AddOrUpdate(this JObject jObj, string propertyName, object value)
        {
            if (jObj.GetValueIC(propertyName)?.Parent is JProperty jprop)
            {
                jprop.Value = value != null ? JToken.FromObject(value) : JValue.CreateNull();
            }
            else
            {
                if (value != null)
                    jObj.Add(propertyName, JToken.FromObject(value));

            }

        }

        public static bool TryGetValue(this JObject jObj, string propertyName, Type type, out object value)
        {
            try
            {
                value = jObj.GetValueIC(propertyName).ConvertValue(type);
                return value != null;
            }
            catch
            {
                value = null;
            }
            return false;
        }

        public static bool TryGetValue<T>(this JObject jObj, string propertyName, out T value)
        {
            try
            {
                value = jObj.GetValueIC(propertyName).ConvertValue<T>();
                return value != null;
            }
            catch
            {
                value = default(T);
            }
            return false;
        }

        public static object GetValue(this JObject jObj, string propertyName, Type type)
        {
            return jObj.GetValueIC(propertyName).ConvertValue(type);
        }

        public static T GetValue<T>(this JObject jObj, string propertyName)
        {
            return jObj.GetValueIC(propertyName).ConvertValue<T>();
        }

        public static object GetSafeValue(this JObject jObj, string propertyName, Type type, object safeValue)
        {
            try
            {
                return jObj.GetValueIC(propertyName).ConvertValue(type);
            }
            catch
            {
                return safeValue;
            }
        }

        public static T GetSafeValue<T>(this JObject jObj, string propertyName, T safeValue)
        {
            try
            {

                var token = jObj.GetValueIC(propertyName);
                if (token == null)
                    return safeValue;
                return token.ConvertValue<T>();
            }
            catch
            {
                return safeValue;
            }

        }

        public static bool ContainsKey(this JObject jObj, string propertyName)
        {
            return jObj.GetValueIC(propertyName) != null;
        }

        //JProperty

        public static T ConvertValue<T>(this JProperty jProperty)
        {
            return (T)jProperty.ConvertValue(typeof(T));
        }

        public static object ConvertValue(this JProperty jProperty, Type type)
        {
            return jProperty.Value.ConvertValue(type);
        }


        // object

        public static T ConvertValue<T>(this object obj)
        {
            return (T)obj.ConvertValue(typeof(T));
        }

        public static object ConvertValue(this object obj, Type type)
        {
            var jToken = obj as JToken ?? JToken.FromObject(obj);
            return jToken.ConvertValue(type);
        }

        public static bool TryGetValue(this object obj, string propertyName, Type type, out object value)
        {
            var jObj = obj as JObject ?? JObject.FromObject(obj);
            return jObj.TryGetValue(propertyName, type, out value);
        }

        public static bool TryGetValue<T>(this object obj, string propertyName, out T value)
        {
            var jObj = obj as JObject ?? JObject.FromObject(obj);
            return jObj.TryGetValue(propertyName, out value);
        }

        public static object GetValue(this object obj, string propertyName, Type type)
        {
            var jObj = obj as JObject ?? JObject.FromObject(obj);
            return jObj.GetValueIC(propertyName).ConvertValue(type);
        }

        public static T GetValue<T>(this object obj, string propertyName)
        {
            var jObj = obj as JObject ?? JObject.FromObject(obj);
            return jObj.GetValueIC(propertyName).ConvertValue<T>();
        }

        public static object GetSafeValue(this object obj, string propertyName, Type type, object safeValue)
        {
            try
            {
                var jObj = obj as JObject ?? JObject.FromObject(obj);
                var prop = jObj.GetValueIC(propertyName);
                if (prop == null)
                    return safeValue;
                return prop.ConvertValue(type);
            }
            catch
            {
                return safeValue;
            }
        }

        public static T GetSafeValue<T>(this object obj, string propertyName, T safeValue)
        {
            try
            {
                var jObj = obj as JObject ?? JObject.FromObject(obj);

                var prop = jObj.GetValueIC(propertyName);
                if (prop == null)
                    return safeValue;
                return prop.ConvertValue<T>();
            }
            catch
            {
                return safeValue;
            }

        }

        public static bool ContainsKey(this object obj, string propertyName)
        {
            var jObj = obj as JObject ?? JObject.FromObject(obj);
            return jObj.SelectToken(propertyName) != null;
        }


        //foreach

        public static JToken ForEach(this JToken src, Action<JToken> action)
        {
            if (src == null)
                return null;
            if (action == null)
                return src;

            var objectToken = src.First;

            while (objectToken != null)
            {
                action(objectToken);
                objectToken = objectToken.Next;
            }

            return src;
        }

        public static TResult ToObject<TResult>(this string json) where TResult : class, new()
        {
            return string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json).ToObject<TResult>();
        }

        public static JObject ToJObjectSafe(this string json)
        {
            return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
        }

        public static JObject ToJObjectSafe(this object obj)
        {
            return obj == null ? new JObject() : JObject.FromObject(obj);
        }

    }
}