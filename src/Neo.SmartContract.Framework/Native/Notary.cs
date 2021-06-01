#pragma warning disable CS0626

using Neo.Cryptography.ECC;
using System.Numerics;

namespace Neo.SmartContract.Framework.Native
{
    [Contract("0x9632cc22a3e6a37307decc287704d88bf31ca82d")]
    public class Notary
    {
        public static extern UInt160 Hash { [ContractHash] get; }
        public static extern bool LockDepositUntil(UInt160 addr, uint till);
        public static extern void Withdraw(UInt160 from, UInt160 to);
        public static extern BigInteger BalanceOf(UInt160 acc);
        public static extern uint ExpirationOf(UInt160 acc);
        public static extern bool Verify(byte[] sig);
        public static extern ECPoint[] GetNotaryNodes();
        public static extern uint GetMaxNotValidBeforeDelta();
        public static extern void SetMaxNotValidBeforeDelta(uint value);
        public static extern long GetNotaryServiceFeePerKey();
        public static extern void SetNotaryServiceFeePerKey(long value);
    }
}
