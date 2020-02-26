using Neo.VM;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Neo.Compiler.Optimizer
{
    [DebuggerDisplay("Offset={Offset}, OpCode={OpCode}")]
    public class NefInstruction : INefItem
    {
        private static readonly uint[] OperandSizePrefixTable = new uint[256];
        private static readonly uint[] OperandSizeTable = new uint[256];

        public OpCode OpCode { get; private set; }
        public uint Size => (1 + DataPrefixSize + DataSize);

        private uint DataPrefixSize => GetOperandPrefixSize(OpCode);
        private uint DataSize => DataPrefixSize > 0 ? (uint)(Data?.Length ?? 0) : GetOperandSize(OpCode);

        public byte[] Data { get; private set; }
        public int Offset { get; private set; }
        public string[] Labels { get; private set; }

        /// <summary>
        /// address type
        /// like JMP =2 bytes
        /// or JMP_L =4 bytes
        /// </summary>
        public int AddressSize { get; private set; }

        public int AddressCountInData => Labels == null ? 0 : Labels.Length;

        /// <summary>
        /// Static constructor
        /// </summary>
        static NefInstruction()
        {
            foreach (var field in typeof(OpCode).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = field.GetCustomAttribute<OperandSizeAttribute>();
                if (attribute == null) continue;
                int index = (int)(OpCode)field.GetValue(null);
                OperandSizePrefixTable[index] = (uint)attribute.SizePrefix;
                OperandSizeTable[index] = (uint)attribute.Size;
            }
        }

        public NefInstruction(OpCode opCode, byte[] data = null, int offset = -1)
        {
            SetOpCode(opCode);
            SetData(data);
            SetOffset(offset);
        }

        public void SetData(byte[] data)
        {
            data ??= new byte[0];
            if (DataPrefixSize == 0 && data.Length != DataSize)
                throw new Exception("error DataSize");

            Data = data;
        }

        public static uint GetOperandSize(OpCode opcode)
        {
            return OperandSizeTable[(int)opcode];
        }

        public static uint GetOperandPrefixSize(OpCode opcode)
        {
            return OperandSizePrefixTable[(int)opcode];
        }

        public int GetAddressInData(int index)
        {
            //Include Address
            if (AddressSize == 0)
                throw new Exception("this data have not Addresses");

            byte[] buf = new byte[4];
            Array.Copy(Data, AddressSize * index, buf, 0, AddressSize);
            var addr = BitConverter.ToInt32(buf, 0);
            return addr;
        }

        public void SetAddressInData(int index, int addr)
        {
            if (AddressSize == 0)
                throw new Exception("this data have not Addresses");

            byte[] buf = BitConverter.GetBytes(addr);
            Array.Copy(buf, 0, Data, AddressSize * index, AddressSize);
        }

        public void SetOffset(int offset)
        {
            Offset = offset;
        }

        public void SetOpCode(OpCode _OpCode)
        {
            this.OpCode = _OpCode;

            uint opprefix = GetOperandPrefixSize(_OpCode);
            if (opprefix == 0)
            {
                uint oplen = GetOperandSize(_OpCode);
                byte[] newdata = new byte[oplen];
                Data ??= new byte[0];
                if (oplen > 0)
                {
                    Array.Copy(Data, 0, newdata, 0, Math.Min(Data.Length, oplen));
                }
                Data = newdata;
            }

            var oldlabels = Labels;
            Labels = null;
            AddressSize = 0;
            switch (_OpCode)
            {
                case OpCode.PUSHA:
                case OpCode.CALL_L:

                case OpCode.JMP_L:
                case OpCode.JMPIF_L:
                case OpCode.JMPLE_L:
                case OpCode.JMPLT_L:
                case OpCode.JMPNE_L:
                case OpCode.JMPIFNOT_L:
                case OpCode.JMPEQ_L:
                case OpCode.JMPGE_L:
                case OpCode.JMPGT_L:
                    {
                        Labels = new string[1]; // is an address
                        if (oldlabels != null && oldlabels.Length >= 1)
                            Labels[0] = oldlabels[0];
                        AddressSize = 4; // 32 bit
                        break;
                    }
                //TODO case OpCode.TRY_L: 32 + 32 bit
                //TODO case OpCode.TRY: 8 + 8 bit
                case OpCode.CALL:

                case OpCode.JMP:
                case OpCode.JMPIF:
                case OpCode.JMPLE:
                case OpCode.JMPLT:
                case OpCode.JMPNE:
                case OpCode.JMPIFNOT:
                case OpCode.JMPEQ:
                case OpCode.JMPGE:
                case OpCode.JMPGT:
                    {
                        Labels = new string[1]; // an address
                        if (oldlabels != null && oldlabels.Length >= 1)
                            Labels[0] = oldlabels[0];
                        AddressSize = 1; //8 bit
                        break;
                    }
            }
        }

        public static NefInstruction ReadFrom(Stream stream)
        {
            var offset = (int)stream.Position;

            byte[] buf = new byte[4];
            var readlen = stream.Read(buf, 0, 1);
            if (readlen == 0)
                return null;

            uint datalen;
            var opcode = (OpCode)buf[0];
            var prefixlen = GetOperandPrefixSize(opcode);
            if (prefixlen > 0)
            {
                stream.Read(buf, 0, (int)prefixlen);
                datalen = BitConverter.ToUInt32(buf, 0);
            }
            else
            {
                datalen = GetOperandSize(opcode);
            }

            var data = new byte[datalen];
            if (datalen > 0)
            {
                var readOperandlen = stream.Read(data, 0, (int)datalen);
                if (readOperandlen != datalen)
                    throw new Exception("error read Instruction");
            }

            return new NefInstruction(opcode, data, offset);
        }

        public static void WriteTo(NefInstruction instruction, Stream stream)
        {
            stream.WriteByte((byte)instruction.OpCode);

            if (instruction.DataPrefixSize > 0)
            {
                var buflen = BitConverter.GetBytes(instruction.DataSize);
                stream.Write(buflen, 0, (int)instruction.DataPrefixSize);
            }
            if (instruction.DataSize > 0)
            {
                stream.Write(instruction.Data, 0, (int)instruction.DataSize);
            }
        }
    }
}
