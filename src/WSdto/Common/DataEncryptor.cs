using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace WS.Common
{
    public class DataEncryptor
    {
        public async Task<byte[]> Decrypt(Aes aes, byte[] data)
        {
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            var crypt = aes.CreateDecryptor(aes.Key, aes.IV);
            using var outms = new MemoryStream();
            using var inms = new MemoryStream(data);
            using var cs = new CryptoStream(inms, crypt, CryptoStreamMode.Read);
            await cs.CopyToAsync(outms);
            return outms.ToArray();
        }

        public async Task<byte[]> Encrypt(Aes aes, int blockSize, byte[] data)
        {

            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            var totalSize = 0;
            var crypt = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using var cs = new CryptoStream(ms, crypt, CryptoStreamMode.Write);
            await cs.WriteAsync(data, 0, data.Length);
            totalSize += data.Length;

            if (data.Length < blockSize)
            {
                var fillData = FillBlock(blockSize - data.Length);
                await cs.WriteAsync(fillData, 0, fillData.Length);
                totalSize += fillData.Length;
            }

            var pading = totalSize % aes.BlockSize;
            if (pading != 0)
            {
                var padingData = FillBlock(aes.BlockSize - pading);
                await cs.WriteAsync(padingData, 0, padingData.Length);
            }


            cs.Flush();

            return ms.ToArray();
        }

        private byte[] FillBlock(int blocksize)
        {
            var rng = new RNGCryptoServiceProvider();
            var block = new byte[blocksize];
            rng.GetBytes(block);
            return block;
        }

    }
}