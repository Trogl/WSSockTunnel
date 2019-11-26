using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jose;
using Newtonsoft.Json;
using WSdto;


namespace WSCli.MX
{
    public class MessageEncoder
    {
        public async Task<byte[]> Encode(Message message, string kid)
        {
            var messageStr = JsonConvert.SerializeObject(message, Formatting.None, WSdto.Json.JsonSettings.settings);
            var encMessage = await Encrypt(messageStr, kid);
            var signMessage = await Sign(encMessage, kid);
            return Encoding.UTF8.GetBytes(signMessage);
        }
        public async Task<Message> Decode(byte[] bytes)
        {
            var jwsToken = Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(jwsToken))
                throw new Exception("JWS token is empty");

            var authResult = await VerifySign(jwsToken);

            var messageStr = await Decrypt(authResult);

            var message = JsonConvert.DeserializeObject<Message>(messageStr, WSdto.Json.JsonSettings.settings);

            return message;
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

            try
            {
                using var key = KeyStore.GetServerKey();
                result.JweToken = Jose.JWT.Decode(jwsToken, key, JwsAlgorithm.RS256);
                result.Kid = (string)kid;

            }
            catch (Exception ex)
            {
                throw new Exception("The JWS signature is not valid.");
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

            try
            {
                using var key = KeyStore.GetClientKey();


                return Jose.JWT.Decode(aResult.JweToken, key, JweAlgorithm.RSA_OAEP, JweEncryption.A256GCM);


            }
            catch (Exception ex)
            {
                throw new Exception("The JWE Decode error");
            }
        }

        //private Encode
        private async Task<string> Encrypt(string data, string kid)
        {


            var extraHeaders = new Dictionary<string, object>
            {
                { "typ", "JOSE" },
                { "kid", kid },
                { "iat", DateTime.UtcNow }
            };

            using var key = KeyStore.GetServerKey();
            return Jose.JWT.Encode(data, key, JweAlgorithm.RSA_OAEP, JweEncryption.A256GCM, extraHeaders: extraHeaders);

        }
        private async Task<string> Sign(string data, string kid)
        {
            var extraHeaders = new Dictionary<string, object>
            {
                { "typ", "JOSE" },
                { "kid", kid },
                { "iat", DateTime.UtcNow },
                { "cty", "JWE" }
            };

            using var key = KeyStore.GetClientKey();
            return Jose.JWT.Encode(data, key, JwsAlgorithm.RS256, extraHeaders: extraHeaders);

        }
    }
}
