using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace WSCli.MX
{
    public class DataEncoder
    {
        public Task<(Guid, byte[])> Encode(Aes aes, byte[] data)
        {

        }

        public Task<byte[]> Decode(Aes aes, int blockSize, byte[] data)
        {

        }

    }
}