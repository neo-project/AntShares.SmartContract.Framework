using System;
using System.Collections.Generic;
using System.Text;
using Neo.VM;
namespace Neo.Compiler.Optimizer
{
    class Parser_DeleteDeadCode : IOptimizeParser
    {
        public bool NeedRightAddr => true;
        public bool Is8BitAddress(NefInstruction inst)
        {
            if (inst.AddressCountInData == 9)
                return false;
            for (var i = 0; i < inst.AddressCountInData; i++)
            {
                var addr = inst.GetAddressInData(i);
                if (addr >= sbyte.MinValue && addr <= sbyte.MaxValue)
                {
                    //smallAddr = true;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
        public void Parse(List<INefItem> Items)
        {
            List<int> touchCodes = new List<int>();

            Touch(Items, touchCodes, 0);

            touchCodes.Sort();
            //so 一个循环我们得到有可能可以执行到的那些指令

            //死码剔除优化器可以最后一步做
            //这个死代码剔除比较简单，还没有处理JMPIF这类条件跳转
            //对JMPIF这类指令 如果他前一条指令是PUSH，可以提前确定JMPIF是否也是死指令

            for (var i = Items.Count - 1; i >= 0; i--)
            {
                var inst = Items[i] as NefInstruction;
                if (inst == null)
                    continue;
                var addr = Items[i].Offset;
                if (!touchCodes.Contains(addr))
                    Items.RemoveAt(i);
            }
        }

        private static void Touch(List<INefItem> Items, List<int> touchCodes, int beginaddr)
        {
            bool bTouchMode = true;

            bool findbegin = false;
            for (int x = 0; x < Items.Count; x++)
            {

                var inst = Items[x] as NefInstruction;
                if (inst == null)
                    continue;
                if (!findbegin)
                {
                    if (inst.Offset >= beginaddr)
                        findbegin = true;
                    else
                        continue;
                }

                if (bTouchMode)
                {
                    if (inst.OpCode == OpCode.NOP)//NOP指令永不touch
                        continue;

                    if (touchCodes.Contains(inst.Offset) == false)
                    {
                        touchCodes.Add(inst.Offset);
                    }

                    if (inst.AddressCountInData > 0)//这是个含地址的指令，他可能有跳转
                    {
                        for (var i = 0; i < inst.AddressCountInData; i++)
                        {
                            var addr = inst.GetAddressInData(i) + inst.Offset;
                            if (touchCodes.Contains(addr) == false)
                            {
                                touchCodes.Add(addr);
                                Touch(Items, touchCodes, addr);
                            }
                        }
                    }

                }
                else
                {
                    //遇到一个标记过被跳转到的指令，打开
                    if (touchCodes.Contains(inst.Offset))
                    {
                        bTouchMode = true;
                    }
                }

                //这些指令之后的指令不可能会执行 （除非由另一条指令跳转而来）
                if (inst.OpCode == OpCode.JMP ||
                    inst.OpCode == OpCode.JMP_L ||
                    inst.OpCode == OpCode.RET ||
                    inst.OpCode == OpCode.THROW ||
                    inst.OpCode == OpCode.THROWIF ||
                    inst.OpCode == OpCode.THROWIFNOT)
                    bTouchMode = false;

            }
        }
    }
}
