using Neo.SmartContract.Framework;

namespace Neo
{
    public class UInt160
    {
        public static extern UInt160 Zero { [OpCode(OpCode.PUSHDATA1, "140000000000000000000000000000000000000000")] get; }

        [OpCode(OpCode.CONVERT, Helper.StackItemType_ByteString)]
        [OpCode(OpCode.DUP)]
        [OpCode(OpCode.SIZE)]
        [OpCode(OpCode.PUSHINT8, "14")] // 0x14 == 20 bytes expected array size
        [OpCode(OpCode.NUMEQUAL)]
        [OpCode(OpCode.ASSERT)]
        public static extern explicit operator UInt160(byte[] value);

        [Script]
        public static extern implicit operator byte[](UInt160 value);
    }
}