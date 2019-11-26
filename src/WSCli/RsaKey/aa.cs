using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using WSdto.Json;

namespace WSCli.WS.RsaKey
{
    public class RSAParametersJson
    {
        public byte[] Modulus { get; set; }
        public byte[] Exponent { get; set; }
        public byte[] P { get; set; }
        public byte[] Q { get; set; }
        public byte[] DP { get; set; }
        public byte[] DQ { get; set; }
        public byte[] InverseQ { get; set; }
        public byte[] D { get; set; }
    }
    internal static class RSAHelpers
    {
        internal static void FromJsonString(this RSA rsa, string jsonString)
        {

            try
            {
                var paramsJson = JsonConvert.DeserializeObject<RSAParametersJson>(jsonString, WSdto.Json.JsonSettings.settings);

                var parameters = new RSAParameters();

                parameters.Modulus = paramsJson.Modulus;
                parameters.Exponent = paramsJson.Exponent;
                parameters.P = paramsJson.P;
                parameters.Q = paramsJson.Q;
                parameters.DP = paramsJson.DP;
                parameters.DQ = paramsJson.DQ;
                parameters.InverseQ = paramsJson.InverseQ;
                parameters.D = paramsJson.D;
                rsa.ImportParameters(parameters);
            }
            catch
            {
                throw new Exception("Invalid JSON RSA key.");
            }
        }

        internal static string ToJsonString(this RSA rsa, bool includePrivateParameters)
        {
            var parameters = rsa.ExportParameters(includePrivateParameters);

            var parasJson = new RSAParametersJson()
            {
                Modulus = parameters.Modulus,
                Exponent = parameters.Exponent,
                P = parameters.P,
                Q = parameters.Q,
                DP = parameters.DP,
                DQ = parameters.DQ,
                InverseQ = parameters.InverseQ,
                D = parameters.D
            };

            return JsonConvert.SerializeObject(parasJson, JsonSettings.settings);
        }

        



    }
}
