using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using WSSrv.MX;

namespace WSSrv.RsaKeys
{
    public class UserKeyInfo
    {
        public string UserId { get; set; } 
        public bool IsEnable { get; set; }
        public RSAParameters Server { get; set; }
        public RSAParameters Client { get; set; }
    }


    public class DataCache
    {
        private ConcurrentDictionary<string, Task<UserKeyInfo>> keyStore = new ConcurrentDictionary<string, Task<UserKeyInfo>>();
        private ConcurrentDictionary<string, Task<UserInfo>> userStore = new ConcurrentDictionary<string, Task<UserInfo>>();


        public Task<UserKeyInfo> GetUserKeys(string kid)
        {
            return keyStore.GetOrAdd(kid, LoadKeys);
        }

        private Task<UserKeyInfo> LoadKeys(string kid)
        {
            if (kid == "Jopas")
            {
               return Task.FromResult(new UserKeyInfo
                {
                    IsEnable = true,
                    UserId = "User1",
                    Client = new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(
                            "wbtvYC6or+2Oqa4WJ1YhMAboe/uOZRJO6gX1nDpJZ5cgOwG2vFITMa/6cTrZOKDeBBUo/nm+2nVOs87aLwLbeCIs59kZiiv4u2IMqL1VnMd50B0kdvwVZiLBKUVkFdLuKSOrSh4V7UreQBn44s98Oar7NgT0jPlihZ2Cc/ra+anaUHc2mIroOztTVgbtiYdfcFcpivRoJVoqJYDahHwKxgb+AFoGfNSY+HLETbF5AFpqHTMne2qvmtbywzaJUxXuK+L3O/oJGUWyWF2Aczr5vp4wviI9LL90S1xBh+OB5o4tyV3/XJazNgkoVR8Ivai2nmVSdkxAhJsSdddPT1YJKh9t27yHVsGqpCKfiVGx0pfGyoJ/yjoiWTkssJ57zYpYwv+eNdJVkJ3hURUInDRkLx2evmy/JKqTHgoaY6nPSGZyPFHw+jJJgWpv2WjfgGtydEZ2IDgFGJmpczeM35Ei+GURP1lcCnulXOeq4jvFeXoDNlBp74Ee96Ccw4uny9/yJ4hWUET8S8NrIK6cwT1xKkEZ5DqwZBk0w/Y7hfciLcNOclL4MeGNOSgpDXfnMBhE2dRnKS91b0ZrX91xOObbXmDpDPpidtYVPEiqOzKOMrrtydeCWok6EXj6xcwPEjb1YmEP0D5/80ihL6f4YpDPzwThaTLNEiJsrAcwwvb0UiE="),
                        Exponent = Convert.FromBase64String("AQAB")

                    },
                    Server = new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(
                            "zdDQvhcfzwi3tP5KWDttlmKsAb/Yz/PQV79IV59ouHYUx6qJjAmz/1vRzJf+zvlpZDgmfcSuZj/jFX5/rxo17T+JKTq7SM/rTw1WCDKYEMw3TPO44soBp/7sYy1906QWT64ugabxWYvPri2yvAiHJ3NH7vANcx85jKmRAfWyZhrOwTGTa5DiFhx7d+DGGQPCSA+kqVtRQ+30zm7HVAvBeUdQ1KZYj+DAD1df26Geonlnt/u68/yc4lmpYypfwyzb0Hymx/A1/D1Gc6btecPwuB3Jyc1bGKqAYnxgG2ol2bRY4M+lJUGPWQbJmDqE3odFgBWwB4zu3zAJU+ZBuHA9Lad4SdIvUKRT8/+h9icDTT115QKJDRcJ7lBZGBWPXxGAAcMaI/atxT5e7FM0ImRKjlf9bpGsVJfrF9EBQ9viJpeAaddNI17zXXo0ENXQORavS9tUwjOZLZrPwKH+1n6j33rDn9raQyDGKihzoXT4ZrqhJPSN9eAHtThq2uLB8Ybpd7T7Zz3zikmIAuSKv9tBUPp6R3x8BFmczEQw8Z1GTtX+YgW5MLkbfUrTxpVwna3AGAWNtj48cDjMN/QERcPdVL+QzQiRtdlRZn32fUXL4r2nyRVJU88yw5TU7uchvQSdIBDOWexxyvK+K4uqlqhBCZUhAi+h7Xjo8kXojHqTiy0="),
                        Exponent = Convert.FromBase64String("AQAB"),
                        P = Convert.FromBase64String(
                            "1oQP97XC47GhV4hyWGbGMoWKCshKLw2DJ9N6AzBCj/2zkkbX3cs+cjoM27qqgS8YlauuW2xI5uYOlL/Qap1OLTf3k8xVmZ0Skr5WzF693E09hTmVFCcNMz6oT2saabrsicEb71I4bshLX62PNRJ9SWhkbq8nPkiUvp7LJN1uObpO8RKCIPJHUrE5X1f61tmCg4q1Ggywwu3TMlIESy0GQ8EW1EBhvdxnn2qIs6irixJElv7/5abwD7X0FRzYhfF4JNmq80P2n2hgPqUkb9ZZbWbnwk3hHRiUFiEljT5carZrt0c8qvBQoFMYKRSJUifXVo5oy429ARfQqmwj9+CE/w=="),
                        Q = Convert.FromBase64String(
                            "9Z4Jfe74BaFm2by8TfUqYl3/f5cQ1Zea2IpgC7Epi718yqfnyl6x70zcmv1eDxS95hxYMMh70dDt1s1LBvB+eeIfp9TLzNlJo/PRLQF0kPtsZyI/Xk1GUegqOK1kXFyh7Inz+AM2QUq/newSN16I2VmC0S7G6RK52b0P1iwEXpS/cDevs+y+/LDBRpUDDzClLGQ/gMu+ya3wE8Wa+nYkXuZ8He+Z5T0YYPex7en4i4LAgtoFEqpRXjJYH6tUR/6oyCUJBqc/wQPIHplt9Gcvx1wybjD4T6SNujaUHDAcuspT6MxNUxWNnrlfOGBCDvbrbspdRMi7KSUrohguulkT0w=="),
                        DP = Convert.FromBase64String(
                            "iXzTDxy78Fuk4Qle4DHezfqw4wBEK6wpZ5kvFmQUBV3BfftR16GwQF3cZ+hX57xbsXH7qjTY9MR2i/f0iKCRvoGkeGH6ax41DUBZOVtvrIcE6yJg3i25VCLQlTY8E4/uksvmL5ku+jH9vTDsHEPhcO8rj5VLPawfSZ1U7ifNwcobVn9aT+t4sxNLhkRJTPLTp6N7N1ry37y2JAZVIimVXk+fZiJtgtaEw7PwMdXlPJlUxMJjGLnKGwevjOiUDiUZr+SioI/qvXiUVxJZLCTh5DCUUgDAS3m5UAWmn8RcTzjkCO/rflPQGTGoxouXB8TpS1yy5ePOQ6kT4Ga3FuQb2Q=="),
                        DQ = Convert.FromBase64String(
                            "6GQ3JuhL00f7QFjK8hfdmmTFsbsFOpLO98M1TNq7LHSE9loXfepLANgAgsTnke1WH7sB1mZagRLldi+XpWE2yauht/InQhL1EiNG7wZJfEPnNU26F0eWGTlJeYbVRm5+5odARpEDbJOE6a7LLYhMgxmmJLXVjgEhx1qS+Vl8aODkoRCPNfXyXrP+qwGie2TTY0UWsI4WXkwsswhssj3F++Sn2ssxGSzNPDIgL7MIbzevXh9aXWa4xh9sMcqxW80fdP9Vou3r7HvfhNQ2rOBU3JPnQ0siJnjTgDTTvjfndvSon8NuBgaGkH9kELtCxVXrPFBMHyCttShuOFgZHkfZ8Q=="),
                        InverseQ = Convert.FromBase64String(
                            "UUZeHnot0TxYv1IeytVpnFgaOBNc23BmJCio2K/8zvjzUgGO0vu1Pay4PEypUx5I8ssZ+bVdOB8MzuyIhQNaaPMJjVCGZBTktDrRFGwHHi18hdJnWwtMh6/Ibx5NfTMUFAErkCD3npSf6qNSlHuAkCx0IGR+NGqfBWWs0CjXeMeN4VZTjBCdht7ePvduZ24SglKUqWz+/W0CqxrIO0dELgCmjvmfcNkXW0AGAZvspbyXcmf9v3eO+1jBimivIR0+TB8b21YjLwhK1GraSC8PggDAf3GhComL9lUVZ/d808DyNtesMKTQfSa5YAB/DV+9Y3/J1YVcd4523B8FrS5tQw=="),
                        D = Convert.FromBase64String(
                            "plCqjiGjm6rAwOqIazpCuTatJpDABHNSlcXGEMCJYB5TdnGxys8AfEbXh4v/5YMYjlrth85K2+eeen0JcxcsIraoAQAr3Y/e57ewINm5lkFgIrgEXIe+xOG0ZgSZ3E+JlAP+ItkjySe4wFi/SUFe7hszMrsbMz81Qxy3SC0iZ24cS3PjXBXtDM8hWuLxUb9+3Lp/ZjebuNfubm6Idrs1MerWP7DYehO1P/BsTtAQn9yZWsx567XjlOm9fpv8XHzAeH5yS7kp9tYRO13WIwKaYD36FS/0AD+vlWZKR30EbyNUev5wxmVvEBLzDyeivSv0lqdv4voZAZQQxZKY4xEX7XvF7zio6IwAF41f3ftxwueZtX6puvMwMkGfliTRpjbCQDBeuiTUVUvM5qsDQd/bOfIh2hgo4MMOmWDnd81Mso0YMoMHgWQqp73EmcagZFG51PuGfqIIRI8iPp4BTDueb828Gx8+5GjZ5kK7fTFnLEAtrvB3GxY5goiu9k5rAW+QqbJuzelHIcsftqPa2aRtWdmaMnSRuqnav0iOb8LP2239JUL1KkfJfcQNc3myk4F71kNQdbw5a0ua2KNlTgpVDv6G/uae1v17yjOoS47PeDyuB0Js+G7d/cYJI9/jH+KITMpru6E/5zviSuOdKtIlubEobqUfVgEn5pIMUsE7JiU=")
                    }
                });
            }


            return Task.FromResult((UserKeyInfo)null);
        }


        public Task<UserInfo> GetUserInfo(string userId)
        {
            return userStore.GetOrAdd(userId, LoadUser);
        }

        private Task<UserInfo> LoadUser(string userId)
        {
            if (userId == "User1")
            {
                return Task.FromResult(new UserInfo
                {
                    IsEnable = true,
                    Passwd = "test1",
                    UserId = "User1",
                    Kid = "Jopas",
                    


                });
            }


            return Task.FromResult((UserInfo)null);
        }
    }


}
