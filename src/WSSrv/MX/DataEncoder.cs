using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing.Constraints;

namespace WSSrv.MX
{
    public class DataEncoder
    {
        public Task<(Guid, byte[])> Encode(Aes aes, byte[] data)
        {
            throw new NotImplementedException();
        }

        public Task<byte[]> Decode(Aes aes, int blockSize, byte[] data)
        {
            throw new NotImplementedException();
        }

    }
}
