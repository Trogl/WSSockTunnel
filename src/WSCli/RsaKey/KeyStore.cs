using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Security.Cryptography;
using System.Text;
using WSCli.Configuration;
using WSCli.WS.RsaKey;
using WSdto.Json;

namespace WSCli
{
    public class keyStore
    {
        public RSAParametersJson Client { get; set; }
        public RSAParametersJson Server { get; set; }

    }


    internal static class KeyStore
    {
        private static RSAParameters client;
        private static RSAParameters server;


        public static void LoadKeys()
        {
            var jStore = ConfigWatcher.GetSection("keyStore");
            var cStore = jStore.ConvertValue<keyStore>();

            client = Convert(cStore.Client);
            server = Convert(cStore.Server);

        }

        public static RSA GetClientKey()
        {

            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(client);
            return rsa;

        }

        public static RSA GetServerKey()
        {
            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(server);
            return rsa;
        }

        private static RSAParameters Convert(RSAParametersJson paramsJson)
        {
            return new RSAParameters
            {
                Modulus = paramsJson.Modulus,
                Exponent = paramsJson.Exponent,
                P = paramsJson.P,
                Q = paramsJson.Q,
                DP = paramsJson.DP,
                DQ = paramsJson.DQ,
                InverseQ = paramsJson.InverseQ,
                D = paramsJson.D
            };

        }

    }
}
