using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jose;
using Newtonsoft.Json;
using WSdto;
using WSSrv.RsaKeys;

namespace WSSrv.MX
{
    public class MessageEncoder
    {
        private DataCache keyStore;

        public MessageEncoder(DataCache keyStore)
        {
            this.keyStore = keyStore;
        }


        public async Task<byte[]> Encode(Message message, string kid)
        {
            var messageStr = JsonConvert.SerializeObject(message, Formatting.None, WSdto.Json.JsonSettings.settings);
            var encMessage = await Encrypt(messageStr, kid);
            var signMessage = await Sign(encMessage, kid);
            return Encoding.UTF8.GetBytes(signMessage);
        }
        public async Task<(Message, string)> Decode(byte[] bytes)
        {
            var jwsToken = Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(jwsToken))
                throw new Exception("JWS token is empty");

            var authResult = await VerifySign(jwsToken);

            var messageStr = await Decrypt(authResult);

            var message = JsonConvert.DeserializeObject<Message>(messageStr, WSdto.Json.JsonSettings.settings);

            return (message, authResult.UserId);
        }

        //private  Decode
        private async Task<AuthResult> VerifySign(string jwsToken)
        {
            var result = new AuthResult
            {
                JwsHeader = Jose.JWT.Headers(jwsToken)
            };


            if (!result.JwsHeader.TryGetValue("alg", out var alg))
                throw new Exception("Required Element Missing (JWS.alg)");


            if (!result.JwsHeader.TryGetValue("kid", out var kid))
                throw new Exception("Required Element Missing (JWS.kid)");


            if (!result.JwsHeader.TryGetValue("typ", out var typ))
                throw new Exception("Required Element Missing (JWS.typ)");


            if (!result.JwsHeader.TryGetValue("cty", out var cty))
                throw new Exception("Required Element Missing (JWS.cty)");


            // load clientKey
            var rsaKeys = await keyStore.GetUserKeys((string)kid);
            if (rsaKeys == null)
                throw new Exception($"KeyId :{kid} not found.");


            if (!rsaKeys.IsEnable)
                throw new Exception($"KeyId :{kid} is disabled.");

            try
            {
                using var clientKey = new RSACryptoServiceProvider();
                clientKey.ImportParameters(rsaKeys.Client);
                result.JweToken = Jose.JWT.Decode(jwsToken, clientKey, JwsAlgorithm.RS256);
                result.Kid = (string)kid;
                result.UserId = rsaKeys.UserId;

            }
            catch (Exception ex)
            {
                throw new Exception($"The JWS signature is not valid: {ex.Message}");
            }

            return result;
        }
        private async Task<string> Decrypt(AuthResult aResult)
        {
            var jweHeader = Jose.JWT.Headers(aResult.JweToken);


            if (!jweHeader.TryGetValue("alg", out var alg))
                throw new Exception("Required Element Missing (JWE.alg)");


            if (!jweHeader.TryGetValue("typ", out var typ))
                throw new Exception("Required Element Missing (JWE.typ)");


            if (!jweHeader.TryGetValue("enc", out var enc))
                throw new Exception("Required Element Missing (JWE.enc)");


            var rsaKeys = await keyStore.GetUserKeys(aResult.Kid);

            if (rsaKeys == null)
                throw new Exception($"KeyId :{aResult.Kid} not found.");

            try
            {
                using var key = new RSACryptoServiceProvider();
                key.ImportParameters(rsaKeys.Server);

                return Jose.JWT.Decode(aResult.JweToken, key, JweAlgorithm.RSA_OAEP, JweEncryption.A256CBC_HS512);


            }
            catch (Exception ex)
            {
                throw new Exception($"The JWE Decode error: {ex.Message}");
            }
        }

        //private Encode
        private async Task<string> Encrypt(string data, string kid)
        {
            var rsaKeys = await keyStore.GetUserKeys(kid);

            if (rsaKeys == null)
                throw new Exception($"KeyId :{kid} not found.");

            var extraHeaders = new Dictionary<string, object>
            {
                { "typ", "JOSE" },
                { "kid", kid },
                { "iat", DateTime.UtcNow }
            };

            using var clientKey = new RSACryptoServiceProvider();

            clientKey.ImportParameters(rsaKeys.Client);
            return Jose.JWT.Encode(data, clientKey, JweAlgorithm.RSA_OAEP, JweEncryption.A256CBC_HS512, extraHeaders: extraHeaders);

        }
        private async Task<string> Sign(string data, string kid)
        {
            var rsaKeys = await keyStore.GetUserKeys(kid);

            if (rsaKeys == null)
                throw new Exception($"KeyId :{kid} not found.");

            var extraHeaders = new Dictionary<string, object>
            {
                { "typ", "JOSE" },
                { "kid", kid },
                { "iat", DateTime.UtcNow },
                { "cty", "JWE" }
            };

            using var clientKey = new RSACryptoServiceProvider();

            clientKey.ImportParameters(rsaKeys.Server);
            return Jose.JWT.Encode(data, clientKey, JwsAlgorithm.RS256, extraHeaders: extraHeaders);

        }
    }
}
