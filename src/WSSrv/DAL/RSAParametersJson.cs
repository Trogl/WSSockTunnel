﻿namespace WSSrv.RsaKeys
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
}